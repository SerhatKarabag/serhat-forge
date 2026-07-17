using System;
using System.Net;
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
    private const int MaxResponseBodyCharacters = 1_048_576;
    private const string AppleSubscriptionType = "Auto-Renewable Subscription";
    private const string AppleConsumableType = "Consumable";
    private const string AppleNonConsumableType = "Non-Consumable";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ILogger<AppleStoreVerifier> _logger;
    private readonly AppleVerifierConfig _config;
    private readonly IAppleJwsVerifier _jwsVerifier;
    private readonly Func<DateTimeOffset> _utcNow;

    public AppleStoreVerifier(
        AppleVerifierConfig config,
        ILogger<AppleStoreVerifier> logger,
        HttpClient? httpClient = null,
        IAppleJwsVerifier? jwsVerifier = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient == null;
        _jwsVerifier = jwsVerifier ?? CreateJwsVerifier(config);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public string Platform => Domain.Platform.Apple;

    public Task<VerificationResult> VerifyOneTimePurchaseAsync(
        VerifyRequest request,
        CancellationToken ct = default) =>
        VerifyWithServerApiAsync(request, isSubscription: false, ct);

    public Task<VerificationResult> VerifySubscriptionAsync(
        VerifyRequest request,
        CancellationToken ct = default) =>
        VerifyWithServerApiAsync(request, isSubscription: true, ct);

    private async Task<VerificationResult> VerifyWithServerApiAsync(
        VerifyRequest request,
        bool isSubscription,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestError = ValidateRequest(request, isSubscription);
        if (requestError != null)
        {
            return requestError;
        }

        string authorizationToken;
        try
        {
            authorizationToken = GenerateJwt();
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            _logger.LogError(
                "Apple verifier credentials could not create an API token: {ErrorType}",
                ex.GetType().Name);
            return VerificationResult.Retryable(
                "APPLE_CONFIGURATION_ERROR",
                "Apple verification credentials are unavailable.");
        }

        try
        {
            var baseUrl = _config.UseSandbox
                ? "https://api.storekit-sandbox.itunes.apple.com"
                : "https://api.storekit.itunes.apple.com";
            var encodedTransactionId = Uri.EscapeDataString(request.TransactionId);

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"{baseUrl}/inApps/v1/transactions/{encodedTransactionId}");
            httpRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", authorizationToken);

            using var response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Apple App Store Server API returned HTTP {StatusCode}",
                    (int)response.StatusCode);
                return ClassifyHttpFailure(response.StatusCode);
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (responseBody.Length > MaxResponseBodyCharacters)
            {
                return VerificationResult.Retryable(
                    "APPLE_RESPONSE_TOO_LARGE",
                    "Apple verification returned an unexpected response.");
            }

            string signedTransaction;
            try
            {
                using var responseDocument = JsonDocument.Parse(responseBody);
                if (!responseDocument.RootElement.TryGetProperty(
                        "signedTransactionInfo",
                        out var signedElement) ||
                    signedElement.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(signedElement.GetString()))
                {
                    return VerificationResult.InvalidReceipt(
                        "Apple response has no signed transaction.");
                }

                signedTransaction = signedElement.GetString()!;
            }
            catch (JsonException)
            {
                return VerificationResult.Retryable(
                    "APPLE_RESPONSE_INVALID",
                    "Apple verification returned an unexpected response.");
            }

            var jwsResult = _jwsVerifier.Verify(signedTransaction);
            if (!jwsResult.IsValid)
            {
                _logger.LogWarning(
                    "Apple signed transaction rejected: {ValidationCode}",
                    jwsResult.ErrorCode);
                return VerificationResult.InvalidReceipt(
                    "Apple transaction signature is invalid.");
            }

            try
            {
                using var transaction = JsonDocument.Parse(jwsResult.Payload!);
                return ValidateSignedTransaction(
                    transaction.RootElement,
                    request,
                    isSubscription,
                    _utcNow());
            }
            catch (Exception ex) when (
                ex is JsonException or FormatException or ArgumentOutOfRangeException)
            {
                _logger.LogWarning(
                    "Apple signed transaction payload rejected: {ErrorType}",
                    ex.GetType().Name);
                return VerificationResult.InvalidReceipt(
                    "Apple signed transaction payload is malformed.");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                "Apple verification network request failed: {ErrorType}",
                ex.GetType().Name);
            return VerificationResult.Retryable(
                "APPLE_STORE_UNAVAILABLE",
                "Apple verification is temporarily unavailable.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "Apple verification failed safely: {ErrorType}",
                ex.GetType().Name);
            return VerificationResult.Retryable(
                "APPLE_STORE_UNAVAILABLE",
                "Apple verification is temporarily unavailable.");
        }
    }

    private VerificationResult ValidateSignedTransaction(
        JsonElement root,
        VerifyRequest request,
        bool isSubscription,
        DateTimeOffset now)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return VerificationResult.InvalidReceipt("Apple transaction payload is not an object.");
        }

        if (!MatchesString(root, "bundleId", _config.BundleId, StringComparison.Ordinal) ||
            !MatchesString(
                root,
                "environment",
                _config.ExpectedEnvironment,
                StringComparison.Ordinal))
        {
            return VerificationResult.InvalidReceipt(
                "Apple transaction application identity mismatch.");
        }

        if (!MatchesOptionalAppAppleId(root, _config.AppAppleId))
        {
            return VerificationResult.InvalidReceipt("Apple application ID mismatch.");
        }

        var productId = GetRequiredString(root, "productId");
        if (!string.Equals(productId, request.ProductId, StringComparison.Ordinal))
        {
            return VerificationResult.ProductMismatch(request.ProductId, productId);
        }

        var transactionId = GetRequiredString(root, "transactionId");
        if (!string.Equals(transactionId, request.TransactionId, StringComparison.Ordinal))
        {
            return VerificationResult.InvalidReceipt("Apple transaction ID mismatch.");
        }

        if (root.TryGetProperty("revocationDate", out var revocationDate) &&
            revocationDate.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            return VerificationResult.Invalid(
                "REVOKED_TRANSACTION",
                "Apple transaction was revoked or refunded.");
        }

        if (!TryGetInt32(root, "quantity", out var quantity) || quantity != 1)
        {
            return VerificationResult.Invalid(
                "INVALID_QUANTITY",
                "Apple transaction quantity must be exactly one.");
        }

        var signedProductType = GetRequiredString(root, "type");
        var expectedSignedType = GetExpectedSignedProductType(request.ExpectedProductType);
        if (expectedSignedType == null ||
            !string.Equals(signedProductType, expectedSignedType, StringComparison.Ordinal) ||
            isSubscription != (request.ExpectedProductType == ProductType.Subscription))
        {
            return VerificationResult.Invalid(
                "PRODUCT_TYPE_MISMATCH",
                "Apple transaction product type does not match the server catalog.");
        }

        var accountBindingError = ValidateAppAccountToken(root, request);
        if (accountBindingError != null)
        {
            return accountBindingError;
        }

        var purchaseDate = GetRequiredUnixMilliseconds(root, "purchaseDate");
        var result = VerificationResult.Valid() with
        {
            ProductId = productId,
            TransactionId = transactionId,
            PurchaseDateUtc = purchaseDate.UtcDateTime,
            IsSandbox = string.Equals(
                GetRequiredString(root, "environment"),
                "Sandbox",
                StringComparison.Ordinal)
        };

        if (!isSubscription)
        {
            return result;
        }

        var originalTransactionId = GetRequiredString(root, "originalTransactionId");
        var expirationDate = GetRequiredUnixMilliseconds(root, "expiresDate");
        if (expirationDate <= now)
        {
            return VerificationResult.ExpiredReceipt();
        }

        return result with
        {
            IsSubscription = true,
            OriginalTransactionId = originalTransactionId,
            ExpirationDateUtc = expirationDate.UtcDateTime,
            SubscriptionStatus = SubscriptionStatus.Active
        };
    }

    private VerificationResult? ValidateAppAccountToken(
        JsonElement root,
        VerifyRequest request)
    {
        if (!_config.RequireAppAccountToken)
        {
            return null;
        }

        if (!Guid.TryParseExact(request.ExpectedAppleAppAccountToken, "D", out var expectedToken) ||
            expectedToken == Guid.Empty)
        {
            return VerificationResult.Retryable(
                "APPLE_ACCOUNT_BINDING_CONFIGURATION_ERROR",
                "Apple account binding could not be derived for this player.");
        }

        if (!root.TryGetProperty("appAccountToken", out var tokenElement) ||
            tokenElement.ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(tokenElement.GetString(), "D", out var signedToken) ||
            signedToken == Guid.Empty)
        {
            return VerificationResult.Invalid(
                "APPLE_ACCOUNT_BINDING_MISSING",
                "Apple transaction is not bound to the authenticated account.");
        }

        return signedToken == expectedToken
            ? null
            : VerificationResult.Invalid(
                "APPLE_ACCOUNT_MISMATCH",
                "Apple transaction belongs to a different authenticated account.");
    }

    private static VerificationResult? ValidateRequest(
        VerifyRequest request,
        bool isSubscription)
    {
        if (string.IsNullOrWhiteSpace(request.ProductId) ||
            string.IsNullOrWhiteSpace(request.TransactionId))
        {
            return VerificationResult.InvalidReceipt(
                "Apple product and transaction IDs are required.");
        }

        if (isSubscription && request.ExpectedProductType != ProductType.Subscription)
        {
            return VerificationResult.Invalid(
                "PRODUCT_TYPE_MISMATCH",
                "Apple subscription verification requires a subscription catalog product.");
        }

        if (!isSubscription &&
            request.ExpectedProductType is not ProductType.Consumable and
            not ProductType.NonConsumable)
        {
            return VerificationResult.Invalid(
                "PRODUCT_TYPE_MISMATCH",
                "Apple one-time verification requires a one-time catalog product.");
        }

        return null;
    }

    private static VerificationResult ClassifyHttpFailure(HttpStatusCode statusCode)
    {
        var numericStatus = (int)statusCode;
        if (statusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
        {
            return VerificationResult.Invalid(
                "APPLE_TRANSACTION_INVALID",
                "Apple rejected the transaction identifier.");
        }

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return VerificationResult.Retryable(
                "APPLE_CONFIGURATION_ERROR",
                "Apple verification credentials were rejected.");
        }

        if (statusCode == HttpStatusCode.RequestTimeout ||
            numericStatus == 429 ||
            numericStatus >= 500)
        {
            return VerificationResult.Retryable(
                "APPLE_STORE_UNAVAILABLE",
                "Apple verification is temporarily unavailable.");
        }

        return numericStatus is >= 400 and < 500
            ? VerificationResult.Invalid(
                "APPLE_REQUEST_REJECTED",
                "Apple rejected the verification request.")
            : VerificationResult.Retryable(
                "APPLE_STORE_UNAVAILABLE",
                "Apple verification is temporarily unavailable.");
    }

    private string GenerateJwt()
    {
        var now = _utcNow();
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

    private static bool MatchesString(
        JsonElement element,
        string propertyName,
        string expected,
        StringComparison comparison) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        string.Equals(property.GetString(), expected, comparison);

    private static bool MatchesOptionalAppAppleId(JsonElement element, long expected)
    {
        if (expected <= 0 || !element.TryGetProperty("appAppleId", out var property))
        {
            return true;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt64(out var actual) && actual == expected,
            JsonValueKind.String => long.TryParse(property.GetString(), out var actual) &&
                                    actual == expected,
            _ => false
        };
    }

    private static string? GetExpectedSignedProductType(ProductType? productType) =>
        productType switch
        {
            ProductType.Consumable => AppleConsumableType,
            ProductType.NonConsumable => AppleNonConsumableType,
            ProductType.Subscription => AppleSubscriptionType,
            _ => null
        };

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

    private static DateTimeOffset GetRequiredUnixMilliseconds(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt64(out var milliseconds))
        {
            throw new JsonException($"Missing required property '{propertyName}'.");
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }

    private static bool TryGetInt32(
        JsonElement element,
        string propertyName,
        out int value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out value);
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
    public long AppAppleId { get; set; }
    public string TrustedRootCertificatesBase64 { get; set; } = string.Empty;
    public string ExpectedEnvironment { get; set; } = "Production";
    public string CertificateRevocationMode { get; set; } = "Online";
    public bool RequireAppAccountToken { get; set; } = true;
    public bool UseSandbox { get; set; }
}
