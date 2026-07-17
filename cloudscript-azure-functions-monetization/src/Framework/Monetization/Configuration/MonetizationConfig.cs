using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private const int MaxProducts = 512;
    private const int MaxProductIdLength = ProductGrantLimits.MaxProductIdLength;
    private const int MaxEconomyItemsPerProduct = ProductGrantLimits.MaxEconomyItemsPerProduct;
    private const int MaxEconomyItemIdLength = ProductGrantLimits.MaxEconomyItemIdLength;
    private const int MaxConsumableQuantity = ProductGrantLimits.MaxConsumableQuantity;
    private const int MaxTierKeyLength = ProductGrantLimits.MaxTierKeyLength;
    private const int MaxTierPrecedence = ProductGrantLimits.MaxTierPrecedence;
    private const int MaxGrantMetadataEntries = ProductGrantLimits.MaxMetadataEntries;
    private const int MaxGrantMetadataKeyLength = ProductGrantLimits.MaxMetadataKeyLength;
    private const int MaxGrantMetadataValueLength = ProductGrantLimits.MaxMetadataValueLength;
    private const int MaxGrantMetadataUtf8Bytes = ProductGrantLimits.MaxMetadataUtf8Bytes;

    private static readonly JsonSerializerOptions ProductJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

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
                Enabled = GetEnvBool("APPLE_STORE_ENABLED", true),
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
                RequireAppAccountToken = GetEnvBool(
                    "APPLE_REQUIRE_APP_ACCOUNT_TOKEN",
                    !IsDevelopmentEnvironment(environmentName)),
                HostEnvironmentName = environmentName
            },
            Google = new GoogleStoreConfig
            {
                Enabled = GetEnvBool("GOOGLE_STORE_ENABLED", true),
                PackageName = GetEnv("GOOGLE_PACKAGE_NAME"),
                ServiceAccountEmail = GetEnv("GOOGLE_SERVICE_ACCOUNT_EMAIL"),
                PrivateKeyBase64 = GetEnv("GOOGLE_PRIVATE_KEY_BASE64"),
                PubSubAudience = GetEnv("GOOGLE_PUBSUB_AUDIENCE"),
                PubSubServiceAccountEmail = GetEnv("GOOGLE_PUBSUB_SERVICE_ACCOUNT_EMAIL"),
                MaxMessageAgeSeconds = GetEnvInt("GOOGLE_MAX_MESSAGE_AGE_SECONDS", 604800),
                RequireObfuscatedAccountId = GetEnvBool(
                    "GOOGLE_REQUIRE_OBFUSCATED_ACCOUNT_ID",
                    !IsDevelopmentEnvironment(environmentName))
            }
        };

        var productsJson = GetEnv("ALLOWED_PRODUCTS_JSON");
        if (!string.IsNullOrWhiteSpace(productsJson))
        {
            config.Products = ParseProductAllowlistJson(productsJson);
        }

        config.Products.AllowSandboxInProduction =
            GetEnvBool("ALLOW_SANDBOX_IN_PRODUCTION", false);
        config.ValidateForStartup();
        return config;
    }

    public static ProductAllowlistConfig ParseProductAllowlistJson(string productsJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productsJson);
        try
        {
            return JsonSerializer.Deserialize<ProductAllowlistConfig>(
                       productsJson,
                       ProductJsonOptions)
                   ?? throw new InvalidOperationException(
                       "ALLOWED_PRODUCTS_JSON resolved to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("ALLOWED_PRODUCTS_JSON is not valid JSON.", ex);
        }
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

        if (!Apple.Enabled && !Google.Enabled)
        {
            errors.Add("At least one store must be enabled with APPLE_STORE_ENABLED or GOOGLE_STORE_ENABLED.");
        }

        ValidateProductAllowlist(errors, requireEnabledProduct: !IsDevelopment);

        if (IsDevelopment)
        {
            ThrowIfConfigurationErrors(errors);
            return;
        }

        RequireSecret(errors, StorageConnectionString, "MONETIZATION_STORAGE_CONNECTION");
        if (StorageConnectionString.Contains("UseDevelopmentStorage=true", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Production cannot use the development storage emulator.");
        }

        RequireSecret(errors, PlayFabTitleId, "PLAYFAB_TITLE_ID");
        RequireSecret(errors, PlayFabSecretKey, "PLAYFAB_DEV_SECRET_KEY");
        if (Apple.Enabled)
        {
            RequireSecret(errors, Apple.BundleId, "APPLE_BUNDLE_ID");
            if (Apple.AppAppleId <= 0)
            {
                errors.Add("APPLE_APP_ID must be a positive integer in production.");
            }
            RequireSecret(errors, Apple.IssuerId, "APPLE_ISSUER_ID");
            RequireSecret(errors, Apple.KeyId, "APPLE_KEY_ID");
            RequireSecret(errors, Apple.PrivateKeyBase64, "APPLE_PRIVATE_KEY_BASE64");
            RequireSecret(errors, Apple.TrustedRootCertificatesBase64, "APPLE_ROOT_CA_BASE64");
            if (!Apple.RequireAppAccountToken)
            {
                errors.Add("APPLE_REQUIRE_APP_ACCOUNT_TOKEN must be true outside development.");
            }
        }

        if (Google.Enabled)
        {
            RequireSecret(errors, Google.PackageName, "GOOGLE_PACKAGE_NAME");
            RequireSecret(errors, Google.ServiceAccountEmail, "GOOGLE_SERVICE_ACCOUNT_EMAIL");
            RequireSecret(errors, Google.PrivateKeyBase64, "GOOGLE_PRIVATE_KEY_BASE64");
            RequireSecret(errors, Google.PubSubAudience, "GOOGLE_PUBSUB_AUDIENCE");
            RequireSecret(errors, Google.PubSubServiceAccountEmail, "GOOGLE_PUBSUB_SERVICE_ACCOUNT_EMAIL");
            if (!Google.RequireObfuscatedAccountId)
            {
                errors.Add("GOOGLE_REQUIRE_OBFUSCATED_ACCOUNT_ID must be true outside development.");
            }
        }

        if (Apple.Enabled &&
            (!Enum.TryParse<X509RevocationMode>(Apple.CertificateRevocationMode, true, out var revocationMode) ||
             revocationMode == X509RevocationMode.NoCheck))
        {
            errors.Add("APPLE_CERTIFICATE_REVOCATION_MODE must be Online or Offline outside development.");
        }

        if (Apple.Enabled &&
            string.Equals(Apple.Environment, "Sandbox", StringComparison.OrdinalIgnoreCase) &&
            !Products.AllowSandboxInProduction)
        {
            errors.Add("Apple Sandbox is disabled in production. Set ALLOW_SANDBOX_IN_PRODUCTION=true only for an intentional non-production deployment.");
        }

        if (Apple.Enabled && Apple.MaxNotificationAgeSeconds is < 60 or > 604800)
        {
            errors.Add("APPLE_MAX_NOTIFICATION_AGE_SECONDS must be between 60 and 604800.");
        }

        if (Google.Enabled && Google.MaxMessageAgeSeconds is < 60 or > 604800)
        {
            errors.Add("GOOGLE_MAX_MESSAGE_AGE_SECONDS must be between 60 and 604800.");
        }

        ThrowIfConfigurationErrors(errors);
    }

    public AppleVerifierConfig ToAppleVerifierConfig() => new()
    {
        BundleId = Apple.BundleId,
        AppAppleId = Apple.AppAppleId,
        IssuerId = Apple.IssuerId,
        KeyId = Apple.KeyId,
        PrivateKeyBase64 = Apple.PrivateKeyBase64,
        TrustedRootCertificatesBase64 = Apple.TrustedRootCertificatesBase64,
        ExpectedEnvironment = Apple.Environment,
        CertificateRevocationMode = Apple.CertificateRevocationMode,
        RequireAppAccountToken = Apple.RequireAppAccountToken,
        UseSandbox = string.Equals(Apple.Environment, "Sandbox", StringComparison.OrdinalIgnoreCase)
    };

    public GoogleVerifierConfig ToGoogleVerifierConfig() => new()
    {
        PackageName = Google.PackageName,
        ServiceAccountEmail = Google.ServiceAccountEmail,
        PrivateKeyBase64 = Google.PrivateKeyBase64,
        RequireObfuscatedAccountId = Google.RequireObfuscatedAccountId
    };

    public AppleNotificationConfig ToAppleNotificationConfig() => new()
    {
        Enabled = Apple.Enabled,
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

    private static bool IsDevelopmentEnvironment(string environmentName) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(environmentName, "Local", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase);

    private static void RequireSecret(ICollection<string> errors, string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("REPLACE_ME", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{name} is required and cannot contain a template placeholder.");
        }
    }

    private void ValidateProductAllowlist(
        ICollection<string> errors,
        bool requireEnabledProduct)
    {
        if (Products == null || Products.Products == null)
        {
            errors.Add("ALLOWED_PRODUCTS_JSON products must be a non-null object.");
            return;
        }

        var products = Products.Products;
        if (products.Count > MaxProducts)
        {
            errors.Add($"ALLOWED_PRODUCTS_JSON cannot contain more than {MaxProducts} products.");
        }

        var enabledCount = 0;
        foreach (var pair in products)
        {
            var dictionaryKey = pair.Key;
            var product = pair.Value;
            if (string.IsNullOrWhiteSpace(dictionaryKey) ||
                dictionaryKey.Length > MaxProductIdLength)
            {
                errors.Add(
                    $"ALLOWED_PRODUCTS_JSON contains an empty or overlong product dictionary key (max {MaxProductIdLength}).");
                continue;
            }

            if (product == null)
            {
                errors.Add($"Product '{dictionaryKey}' configuration cannot be null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(product.ProductId) ||
                product.ProductId.Length > MaxProductIdLength)
            {
                errors.Add(
                    $"Product '{dictionaryKey}' productId is required and limited to {MaxProductIdLength} characters.");
            }
            else if (!string.Equals(dictionaryKey, product.ProductId, StringComparison.Ordinal))
            {
                errors.Add(
                    $"Product dictionary key '{dictionaryKey}' must exactly match productId '{product.ProductId}'.");
            }

            if (!Enum.IsDefined(typeof(ProductType), product.Type))
            {
                errors.Add($"Product '{dictionaryKey}' has an unsupported product type.");
            }

            ValidateGrantMetadata(errors, dictionaryKey, product.GrantMetadata);

            if (product.Enabled)
            {
                enabledCount++;
            }

            ValidateEconomyItems(errors, dictionaryKey, product.EconomyItemIds, product.Enabled);

            if (product.Type == ProductType.Consumable)
            {
                if (product.Quantity is < 1 or > MaxConsumableQuantity)
                {
                    errors.Add(
                        $"Consumable product '{dictionaryKey}' quantity must be between 1 and {MaxConsumableQuantity}.");
                }
            }
            else if (product.Quantity != 1)
            {
                errors.Add($"Non-consumable product '{dictionaryKey}' quantity must be exactly 1.");
            }

            if (product.Type == ProductType.Subscription)
            {
                if (string.IsNullOrWhiteSpace(product.TierKey) ||
                    product.TierKey.Length > MaxTierKeyLength)
                {
                    errors.Add(
                        $"Subscription product '{dictionaryKey}' tierKey is required and limited to {MaxTierKeyLength} characters.");
                }

                if (product.TierPrecedence is < 0 or > MaxTierPrecedence)
                {
                    errors.Add(
                        $"Subscription product '{dictionaryKey}' tierPrecedence must be between 0 and {MaxTierPrecedence}.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(product.TierKey) || product.TierPrecedence != 0)
            {
                errors.Add(
                    $"Non-subscription product '{dictionaryKey}' cannot define tierKey or tierPrecedence.");
            }
        }

        if (requireEnabledProduct && enabledCount == 0)
        {
            errors.Add("ALLOWED_PRODUCTS_JSON must contain at least one enabled product in production.");
        }
    }

    private static void ValidateEconomyItems(
        ICollection<string> errors,
        string productId,
        IReadOnlyCollection<string>? itemIds,
        bool enabled)
    {
        if (itemIds == null || itemIds.Count == 0)
        {
            if (enabled)
            {
                errors.Add($"Enabled product '{productId}' must grant at least one Economy item.");
            }

            return;
        }

        if (itemIds.Count > MaxEconomyItemsPerProduct)
        {
            errors.Add(
                $"Product '{productId}' cannot grant more than {MaxEconomyItemsPerProduct} Economy items.");
        }

        var uniqueItemIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var itemId in itemIds)
        {
            if (string.IsNullOrWhiteSpace(itemId) || itemId.Length > MaxEconomyItemIdLength)
            {
                errors.Add(
                    $"Product '{productId}' has an empty or overlong Economy item ID (max {MaxEconomyItemIdLength}).");
                continue;
            }

            if (!uniqueItemIds.Add(itemId))
            {
                errors.Add($"Product '{productId}' contains duplicate Economy item ID '{itemId}'.");
            }
        }
    }

    private static void ValidateGrantMetadata(
        ICollection<string> errors,
        string productId,
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata == null)
        {
            return;
        }

        if (metadata.Count > MaxGrantMetadataEntries)
        {
            errors.Add(
                $"Product '{productId}' grantMetadata cannot contain more than {MaxGrantMetadataEntries} entries.");
        }

        var totalUtf8Bytes = 0;
        foreach (var pair in metadata)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) ||
                pair.Key.Length > MaxGrantMetadataKeyLength)
            {
                errors.Add(
                    $"Product '{productId}' has an empty or overlong grantMetadata key (max {MaxGrantMetadataKeyLength}).");
                continue;
            }

            if (pair.Value == null || pair.Value.Length > MaxGrantMetadataValueLength)
            {
                errors.Add(
                    $"Product '{productId}' grantMetadata value for '{pair.Key}' exceeds {MaxGrantMetadataValueLength} characters or is null.");
                continue;
            }

            totalUtf8Bytes += Encoding.UTF8.GetByteCount(pair.Key);
            totalUtf8Bytes += Encoding.UTF8.GetByteCount(pair.Value);
        }

        if (totalUtf8Bytes > MaxGrantMetadataUtf8Bytes)
        {
            errors.Add(
                $"Product '{productId}' grantMetadata exceeds {MaxGrantMetadataUtf8Bytes} UTF-8 bytes.");
        }
    }

    private static void ThrowIfConfigurationErrors(IReadOnlyCollection<string> errors)
    {
        if (errors.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "Unsafe or incomplete monetization configuration:" + Environment.NewLine +
            string.Join(Environment.NewLine, errors.Select(error => $"- {error}")));
    }
}

public sealed class AppleStoreConfig
{
    public bool Enabled { get; set; } = true;
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
    public bool RequireAppAccountToken { get; set; } = true;
    public string HostEnvironmentName { get; set; } = "Production";
}

public sealed class GoogleStoreConfig
{
    public bool Enabled { get; set; } = true;
    public string PackageName { get; set; } = string.Empty;
    public string ServiceAccountEmail { get; set; } = string.Empty;
    public string PrivateKeyBase64 { get; set; } = string.Empty;
    public string PubSubAudience { get; set; } = string.Empty;
    public string PubSubServiceAccountEmail { get; set; } = string.Empty;
    public int MaxMessageAgeSeconds { get; set; } = 604800;
    public bool RequireObfuscatedAccountId { get; set; } = true;
}
