using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Verification;

/// <summary>
/// Apple App Store Server API verifier. Both the API request JWT and Apple's signed
/// transaction response are cryptographically verified.
/// </summary>
public sealed class AppleStoreVerifier : IStoreVerifier, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ILogger<AppleStoreVerifier> _logger;
    private readonly AppleVerifierConfig _config;
    private readonly IAppleJwsVerifier _jwsVerifier;

    public AppleStoreVerifier(
        AppleVerifierConfig config,
        ILogger<AppleStoreVerifier> logger,
        HttpClient? httpClient = null,
        IAppleJwsVerifier? jwsVerifier = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient == null;
        _jwsVerifier = jwsVerifier ?? CreateJwsVerifier(config);
    }

    public string Platform => Domain.Platform.Apple;

    public Task<VerificationResult> VerifyOneTimePurchaseAsync(
        VerifyRequest request,
        CancellationToken ct = default) =>
        VerifyWithServerApiAsync(request, false, ct);

    public Task<VerificationResult> VerifySubscriptionAsync(
        VerifyRequest request,
        CancellationToken ct = default) =>
        VerifyWithServerApiAsync(request, true, ct);

    private async Task<VerificationResult> VerifyWithServerApiAsync(
        VerifyRequest request,
        bool isSubscription,
        CancellationToken ct)
    {
        try
        {
            var baseUrl = _config.UseSandbox
                ? "https://api.storekit-sandbox.itunes.apple.com"
                : "https://api.storekit.itunes.apple.com";
            var encodedTransactionId = Uri.EscapeDataString(request.TransactionId);

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"{baseUrl}/inApps/v1/transactions/{encodedTransactionId}");
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwt());

            using var response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Apple App Store Server API returned {StatusCode}", response.StatusCode);
                return VerificationResult.StoreError($"Apple API error: {response.StatusCode}");
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var responseDocument = JsonDocument.Parse(responseBody);
            if (!responseDocument.RootElement.TryGetProperty("signedTransactionInfo", out var signedElement) ||
                signedElement.ValueKind != JsonValueKind.String)
            {
                return VerificationResult.InvalidReceipt("Apple response has no signed transaction");
            }

            var signedTransaction = signedElement.GetString();
            var jwsResult = _jwsVerifier.Verify(signedTransaction ?? string.Empty);
            if (!jwsResult.IsValid)
            {
                _logger.LogWarning(
                    "Apple signed transaction rejected: {ValidationCode}",
                    jwsResult.ErrorCode);
                return VerificationResult.InvalidReceipt("Apple transaction signature is invalid");
            }

            using var transaction = JsonDocument.Parse(jwsResult.Payload!);
            var root = transaction.RootElement;
            if (!MatchesString(root, "bundleId", _config.BundleId) ||
                !MatchesString(root, "environment", _config.ExpectedEnvironment))
            {
                return VerificationResult.InvalidReceipt("Apple transaction application identity mismatch");
            }

            var productId = GetRequiredString(root, "productId");
            if (!string.Equals(productId, request.ProductId, StringComparison.Ordinal))
            {
                return VerificationResult.ProductMismatch(request.ProductId, productId);
            }

            var transactionId = GetRequiredString(root, "transactionId");
            if (!string.Equals(transactionId, request.TransactionId, StringComparison.Ordinal))
            {
                return VerificationResult.InvalidReceipt("Apple transaction ID mismatch");
            }

            var result = VerificationResult.Valid() with
            {
                ProductId = productId,
                TransactionId = transactionId,
                PurchaseDateUtc = DateTimeOffset.FromUnixTimeMilliseconds(
                    root.GetProperty("purchaseDate").GetInt64()).UtcDateTime,
                IsSandbox = string.Equals(
                    root.GetProperty("environment").GetString(),
                    "Sandbox",
                    StringComparison.OrdinalIgnoreCase)
            };

            if (isSubscription)
            {
                result = result with
                {
                    IsSubscription = true,
                    OriginalTransactionId = GetRequiredString(root, "originalTransactionId"),
                    ExpirationDateUtc = root.TryGetProperty("expiresDate", out var expiration)
                        ? DateTimeOffset.FromUnixTimeMilliseconds(expiration.GetInt64()).UtcDateTime
                        : null,
                    SubscriptionStatus = DetermineSubscriptionStatus(root)
                };
            }

            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Apple verification response JSON rejected: {ErrorType}", ex.GetType().Name);
            return VerificationResult.InvalidReceipt("Apple response is malformed");
        }
        catch (Exception ex)
        {
            _logger.LogError("Apple verification failed: {ErrorType}", ex.GetType().Name);
            return VerificationResult.StoreError("Apple verification is temporarily unavailable");
        }
    }

    private string GenerateJwt()
    {
        var now = DateTimeOffset.UtcNow;
        var headerBase64 = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            alg = "ES256",
            kid = _config.KeyId,
            typ = "JWT"
        }));
        var payloadBase64 = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = _config.IssuerId,
            iat = now.ToUnixTimeSeconds(),
            exp = now.AddMinutes(5).ToUnixTimeSeconds(),
            aud = "appstoreconnect-v1",
            bid = _config.BundleId
        }));
        var signingInput = $"{headerBase64}.{payloadBase64}";

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(_config.PrivateKeyBase64), out _);
        var signature = ecdsa.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"{signingInput}.{Base64UrlEncode(signature)}";
    }

    private static bool MatchesString(JsonElement element, string propertyName, string expected) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        string.Equals(property.GetString(), expected, StringComparison.OrdinalIgnoreCase);

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new JsonException($"Missing required property '{propertyName}'.");
        }

        return property.GetString()!;
    }

    private static SubscriptionStatus DetermineSubscriptionStatus(JsonElement transaction)
    {
        if (transaction.TryGetProperty("revocationDate", out _))
        {
            return SubscriptionStatus.Refunded;
        }

        if (transaction.TryGetProperty("expiresDate", out var expiration) &&
            DateTimeOffset.FromUnixTimeMilliseconds(expiration.GetInt64()) < DateTimeOffset.UtcNow)
        {
            return SubscriptionStatus.Expired;
        }

        if (transaction.TryGetProperty("gracePeriodExpiresDate", out _))
        {
            return SubscriptionStatus.GracePeriod;
        }

        return SubscriptionStatus.Active;
    }

    private static IAppleJwsVerifier CreateJwsVerifier(AppleVerifierConfig config)
    {
        if (!Enum.TryParse<X509RevocationMode>(
                config.CertificateRevocationMode,
                true,
                out var revocationMode))
        {
            throw new InvalidOperationException("Invalid Apple certificate revocation mode.");
        }

        return new AppleJwsVerifier(
            new AppleJwsVerificationOptions
            {
                TrustedRootCertificatesBase64 = config.TrustedRootCertificatesBase64,
                RevocationMode = revocationMode
            },
            NullLogger<AppleJwsVerifier>.Instance);
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}

public sealed class AppleVerifierConfig
{
    public string IssuerId { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public string PrivateKeyBase64 { get; set; } = string.Empty;
    public string BundleId { get; set; } = string.Empty;
    public string TrustedRootCertificatesBase64 { get; set; } = string.Empty;
    public string ExpectedEnvironment { get; set; } = "Production";
    public string CertificateRevocationMode { get; set; } = "Online";
    public bool UseSandbox { get; set; }
}