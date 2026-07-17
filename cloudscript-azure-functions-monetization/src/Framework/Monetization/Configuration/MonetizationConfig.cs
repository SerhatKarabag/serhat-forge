using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;
using Serhat.Forge.CloudScript.Framework.Monetization.Verification;
using Serhat.Forge.CloudScript.Framework.Monetization.Webhooks;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Configuration;

/// <summary>
/// Monetization configuration loaded from environment variables.
/// Unknown environments are treated as production so unsafe fallbacks fail closed.
/// </summary>
public sealed class MonetizationConfig
{
    public string EnvironmentName { get; set; } = "Production";
    public string StorageConnectionString { get; set; } = string.Empty;
    public string PlayFabTitleId { get; set; } = string.Empty;
    public string PlayFabSecretKey { get; set; } = string.Empty;
    public AppleStoreConfig Apple { get; set; } = new();
    public GoogleStoreConfig Google { get; set; } = new();
    public ProductAllowlistConfig Products { get; set; } = new();
    public bool UseFakeVerifier { get; set; }

    public bool IsDevelopment =>
        string.Equals(EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(EnvironmentName, "Local", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(EnvironmentName, "Test", StringComparison.OrdinalIgnoreCase);

    public static MonetizationConfig LoadFromEnvironment()
    {
        var environmentName = FirstNonEmpty(
            GetEnv("AZURE_FUNCTIONS_ENVIRONMENT"),
            GetEnv("DOTNET_ENVIRONMENT"),
            "Production");

        var config = new MonetizationConfig
        {
            EnvironmentName = environmentName,
            StorageConnectionString = FirstNonEmpty(
                GetEnv("MONETIZATION_STORAGE_CONNECTION"),
                GetEnv("AzureWebJobsStorage")),
            PlayFabTitleId = GetEnv("PLAYFAB_TITLE_ID"),
            PlayFabSecretKey = FirstNonEmpty(
                GetEnv("PLAYFAB_DEV_SECRET_KEY"),
                GetEnv("PLAYFAB_SECRET_KEY")),
            UseFakeVerifier = GetEnvBool("USE_FAKE_VERIFIER", false),
            Apple = new AppleStoreConfig
            {
                BundleId = GetEnv("APPLE_BUNDLE_ID"),
                AppAppleId = GetEnvLong("APPLE_APP_ID", 0),
                IssuerId = GetEnv("APPLE_ISSUER_ID"),
                KeyId = GetEnv("APPLE_KEY_ID"),
                PrivateKeyBase64 = GetEnv("APPLE_PRIVATE_KEY_BASE64"),
                TrustedRootCertificatesBase64 = GetEnv("APPLE_ROOT_CA_BASE64"),
                Environment = FirstNonEmpty(GetEnv("APPLE_ENVIRONMENT"), "Production"),
                CertificateRevocationMode = FirstNonEmpty(
                    GetEnv("APPLE_CERTIFICATE_REVOCATION_MODE"),
                    "Online"),
                MaxNotificationAgeSeconds = GetEnvInt("APPLE_MAX_NOTIFICATION_AGE_SECONDS", 604800),
                SkipSignatureValidation = GetEnvBool("APPLE_SKIP_SIGNATURE_VALIDATION", false),
                HostEnvironmentName = environmentName
            },
            Google = new GoogleStoreConfig
            {
                PackageName = GetEnv("GOOGLE_PACKAGE_NAME"),
                ServiceAccountEmail = GetEnv("GOOGLE_SERVICE_ACCOUNT_EMAIL"),
                PrivateKeyBase64 = GetEnv("GOOGLE_PRIVATE_KEY_BASE64"),
                PubSubAudience = GetEnv("GOOGLE_PUBSUB_AUDIENCE"),
                PubSubServiceAccountEmail = GetEnv("GOOGLE_PUBSUB_SERVICE_ACCOUNT_EMAIL"),
                MaxMessageAgeSeconds = GetEnvInt("GOOGLE_MAX_MESSAGE_AGE_SECONDS", 604800)
            }
        };

        var productsJson = GetEnv("ALLOWED_PRODUCTS_JSON");
        if (!string.IsNullOrWhiteSpace(productsJson))
        {
            try
            {
                config.Products = JsonSerializer.Deserialize<ProductAllowlistConfig>(productsJson)
                    ?? throw new InvalidOperationException("ALLOWED_PRODUCTS_JSON resolved to null.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("ALLOWED_PRODUCTS_JSON is not valid JSON.", ex);
            }
        }

        config.Products.AllowSandboxInProduction =
            GetEnvBool("ALLOW_SANDBOX_IN_PRODUCTION", false);
        config.ValidateForStartup();
        return config;
    }

    /// <summary>
    /// Rejects development-only bypasses outside an explicitly local/test environment and
    /// validates all production secrets/configuration before the Function host starts.
    /// </summary>
    public void ValidateForStartup()
    {
        var errors = new List<string>();

        if (!IsDevelopment && UseFakeVerifier)
        {
            errors.Add("USE_FAKE_VERIFIER must be false outside Development/Local/Test.");
        }

        if (!IsDevelopment && Apple.SkipSignatureValidation)
        {
            errors.Add("APPLE_SKIP_SIGNATURE_VALIDATION must be false outside Development/Local/Test.");
        }

        if (IsDevelopment)
        {
            return;
        }

        RequireSecret(errors, StorageConnectionString, "MONETIZATION_STORAGE_CONNECTION");
        if (StorageConnectionString.Contains("UseDevelopmentStorage=true", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Production cannot use the development storage emulator.");
        }

        RequireSecret(errors, PlayFabTitleId, "PLAYFAB_TITLE_ID");
        RequireSecret(errors, PlayFabSecretKey, "PLAYFAB_DEV_SECRET_KEY");
        RequireSecret(errors, Apple.BundleId, "APPLE_BUNDLE_ID");
        if (Apple.AppAppleId <= 0)
        {
            errors.Add("APPLE_APP_ID must be a positive integer in production.");
        }
        RequireSecret(errors, Apple.IssuerId, "APPLE_ISSUER_ID");
        RequireSecret(errors, Apple.KeyId, "APPLE_KEY_ID");
        RequireSecret(errors, Apple.PrivateKeyBase64, "APPLE_PRIVATE_KEY_BASE64");
        RequireSecret(errors, Apple.TrustedRootCertificatesBase64, "APPLE_ROOT_CA_BASE64");
        RequireSecret(errors, Google.PackageName, "GOOGLE_PACKAGE_NAME");
        RequireSecret(errors, Google.ServiceAccountEmail, "GOOGLE_SERVICE_ACCOUNT_EMAIL");
        RequireSecret(errors, Google.PrivateKeyBase64, "GOOGLE_PRIVATE_KEY_BASE64");
        RequireSecret(errors, Google.PubSubAudience, "GOOGLE_PUBSUB_AUDIENCE");
        RequireSecret(errors, Google.PubSubServiceAccountEmail, "GOOGLE_PUBSUB_SERVICE_ACCOUNT_EMAIL");

        if (!Enum.TryParse<X509RevocationMode>(Apple.CertificateRevocationMode, true, out var revocationMode) ||
            revocationMode == X509RevocationMode.NoCheck)
        {
            errors.Add("APPLE_CERTIFICATE_REVOCATION_MODE must be Online or Offline outside development.");
        }

        if (string.Equals(Apple.Environment, "Sandbox", StringComparison.OrdinalIgnoreCase) &&
            !Products.AllowSandboxInProduction)
        {
            errors.Add("Apple Sandbox is disabled in production. Set ALLOW_SANDBOX_IN_PRODUCTION=true only for an intentional non-production deployment.");
        }

        if (Apple.MaxNotificationAgeSeconds is < 60 or > 604800)
        {
            errors.Add("APPLE_MAX_NOTIFICATION_AGE_SECONDS must be between 60 and 604800.");
        }

        if (Google.MaxMessageAgeSeconds is < 60 or > 604800)
        {
            errors.Add("GOOGLE_MAX_MESSAGE_AGE_SECONDS must be between 60 and 604800.");
        }

        if (Products.Products.Count == 0 || Products.Products.All(pair => !pair.Value.Enabled))
        {
            errors.Add("ALLOWED_PRODUCTS_JSON must contain at least one enabled product in production.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Unsafe or incomplete monetization configuration:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(error => $"- {error}")));
        }
    }

    public AppleVerifierConfig ToAppleVerifierConfig() => new()
    {
        BundleId = Apple.BundleId,
        IssuerId = Apple.IssuerId,
        KeyId = Apple.KeyId,
        PrivateKeyBase64 = Apple.PrivateKeyBase64,
        TrustedRootCertificatesBase64 = Apple.TrustedRootCertificatesBase64,
        ExpectedEnvironment = Apple.Environment,
        CertificateRevocationMode = Apple.CertificateRevocationMode,
        UseSandbox = string.Equals(Apple.Environment, "Sandbox", StringComparison.OrdinalIgnoreCase)
    };

    public GoogleVerifierConfig ToGoogleVerifierConfig() => new()
    {
        PackageName = Google.PackageName,
        ServiceAccountEmail = Google.ServiceAccountEmail,
        PrivateKeyBase64 = Google.PrivateKeyBase64
    };

    public AppleNotificationConfig ToAppleNotificationConfig() => new()
    {
        BundleId = Apple.BundleId,
        AppAppleId = Apple.AppAppleId,
        ExpectedEnvironment = Apple.Environment,
        TrustedRootCertificatesBase64 = Apple.TrustedRootCertificatesBase64,
        CertificateRevocationMode = Apple.CertificateRevocationMode,
        MaxNotificationAge = TimeSpan.FromSeconds(Apple.MaxNotificationAgeSeconds),
        SkipSignatureValidation = Apple.SkipSignatureValidation,
        HostEnvironmentName = EnvironmentName
    };

    public GoogleRtdnConfig ToGoogleRtdnConfig() => new()
    {
        ExpectedPackageName = Google.PackageName,
        ExpectedAudience = Google.PubSubAudience,
        ExpectedServiceAccountEmail = Google.PubSubServiceAccountEmail,
        MaxMessageAge = TimeSpan.FromSeconds(Google.MaxMessageAgeSeconds)
    };

    private static string GetEnv(string name) =>
        Environment.GetEnvironmentVariable(name)?.Trim() ?? string.Empty;

    private static bool GetEnvBool(string name, bool defaultValue)
    {
        var value = GetEnv(name);
        return string.IsNullOrEmpty(value)
            ? defaultValue
            : value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";
    }

    private static int GetEnvInt(string name, int defaultValue)
    {
        var value = GetEnv(name);
        if (string.IsNullOrEmpty(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, out var parsed))
        {
            throw new InvalidOperationException($"Environment variable '{name}' must be an integer.");
        }

        return parsed;
    }

    private static long GetEnvLong(string name, long defaultValue)
    {
        var value = GetEnv(name);
        if (string.IsNullOrEmpty(value))
        {
            return defaultValue;
        }

        if (!long.TryParse(value, out var parsed))
        {
            throw new InvalidOperationException($"Environment variable '{name}' must be an integer.");
        }

        return parsed;
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static void RequireSecret(ICollection<string> errors, string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("REPLACE_ME", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{name} is required and cannot contain a template placeholder.");
        }
    }
}

public sealed class AppleStoreConfig
{
    public string BundleId { get; set; } = string.Empty;
    public long AppAppleId { get; set; }
    public string IssuerId { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public string PrivateKeyBase64 { get; set; } = string.Empty;
    public string TrustedRootCertificatesBase64 { get; set; } = string.Empty;
    public string Environment { get; set; } = "Production";
    public string CertificateRevocationMode { get; set; } = "Online";
    public int MaxNotificationAgeSeconds { get; set; } = 604800;
    public bool SkipSignatureValidation { get; set; }
    public string HostEnvironmentName { get; set; } = "Production";
}

public sealed class GoogleStoreConfig
{
    public string PackageName { get; set; } = string.Empty;
    public string ServiceAccountEmail { get; set; } = string.Empty;
    public string PrivateKeyBase64 { get; set; } = string.Empty;
    public string PubSubAudience { get; set; } = string.Empty;
    public string PubSubServiceAccountEmail { get; set; } = string.Empty;
    public int MaxMessageAgeSeconds { get; set; } = 604800;
}