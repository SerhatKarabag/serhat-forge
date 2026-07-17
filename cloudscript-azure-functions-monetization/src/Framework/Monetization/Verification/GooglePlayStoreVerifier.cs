using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Verification;

/// <summary>
/// Google Play Developer API verifier.
/// Uses service account authentication.
/// </summary>
public sealed class GooglePlayStoreVerifier :
    IStoreVerifier,
    IGooglePlaySubscriptionSnapshotProvider,
    IDisposable
{
    public string Platform => Domain.Platform.Google;

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ILogger<GooglePlayStoreVerifier> _logger;
    private readonly GoogleVerifierConfig _config;

    private string? _cachedAccessToken;
    private DateTime _tokenExpiresAt;

    public GooglePlayStoreVerifier(
        GoogleVerifierConfig config,
        ILogger<GooglePlayStoreVerifier> logger,
        HttpClient? httpClient = null)
    {
        _config = config;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient == null;
    }

    public async Task<VerificationResult> VerifyOneTimePurchaseAsync(
        VerifyRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var accessToken = await GetAccessTokenAsync(ct);

            var escapedPackageName = Uri.EscapeDataString(_config.PackageName);
            var escapedProductId = Uri.EscapeDataString(request.ProductId);
            var escapedPurchaseToken = Uri.EscapeDataString(request.ReceiptPayload);
            var url =
                $"https://androidpublisher.googleapis.com/androidpublisher/v3/applications/{escapedPackageName}/purchases/products/{escapedProductId}/tokens/{escapedPurchaseToken}";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(httpRequest, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Google products API returned {StatusCode}",
                    (int)response.StatusCode);
                return ClassifyVerificationHttpFailure(response.StatusCode);
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            using var purchaseDocument = JsonDocument.Parse(responseBody);
            var purchase = purchaseDocument.RootElement;

            // Check purchase state
            var purchaseState = purchase.GetProperty("purchaseState").GetInt32();
            if (purchaseState == 2) // Pending payment; Google may complete it later.
            {
                return VerificationResult.Retryable(
                    "PURCHASE_PENDING",
                    "The Google Play purchase is still pending");
            }

            if (purchaseState != 0) // 0 = Purchased, 1 = Cancelled
            {
                return VerificationResult.Invalid("PURCHASE_NOT_COMPLETED",
                    $"Purchase state is {purchaseState}, expected 0 (Purchased)");
            }

            var verifiedProductId = ReadOptionalString(purchase, "productId");
            if (!string.IsNullOrWhiteSpace(verifiedProductId) &&
                !string.Equals(verifiedProductId, request.ProductId, StringComparison.Ordinal))
            {
                return VerificationResult.ProductMismatch(request.ProductId, verifiedProductId);
            }

            var verifiedPurchaseToken = ReadOptionalString(purchase, "purchaseToken");
            if (!string.IsNullOrWhiteSpace(verifiedPurchaseToken) &&
                !string.Equals(
                    verifiedPurchaseToken,
                    request.ReceiptPayload,
                    StringComparison.Ordinal))
            {
                return VerificationResult.InvalidReceipt(
                    "Google Play returned a different purchase token");
            }

            var quantity = purchase.TryGetProperty("quantity", out var quantityElement)
                ? quantityElement.GetInt32()
                : 1;
            if (quantity != 1)
            {
                return VerificationResult.Invalid(
                    "UNSUPPORTED_PURCHASE_QUANTITY",
                    "Multi-quantity Google Play purchases require explicit grant scaling");
            }

            var refundableQuantity = purchase.TryGetProperty(
                "refundableQuantity",
                out var refundableQuantityElement)
                ? refundableQuantityElement.GetInt32()
                : quantity;
            if (refundableQuantity != quantity)
            {
                return VerificationResult.Invalid(
                    "PURCHASE_REFUNDED",
                    "Google Play reports a partially or fully refunded purchase");
            }

            var accountBindingFailure = ValidateAccountBinding(
                request,
                ReadOptionalString(purchase, "obfuscatedExternalAccountId"));
            if (accountBindingFailure != null)
            {
                return accountBindingFailure;
            }

            return VerificationResult.Valid() with
            {
                ProductId = request.ProductId,
                TransactionId = purchase.TryGetProperty("orderId", out var oid)
                    ? oid.GetString()
                    : request.TransactionId,
                PurchaseDateUtc = TryReadUnixMilliseconds(
                    purchase,
                    "purchaseTimeMillis",
                    out var purchaseDateUtc)
                    ? purchaseDateUtc
                    : DateTime.UtcNow,
                IsSubscription = false,
                IsSandbox = purchase.TryGetProperty("purchaseType", out var ptype) &&
                           ptype.GetInt32() == 0 // 0 = Test
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Exception messages from HttpClient can contain the request URI, which embeds
            // the Google purchase token. Log only the type and return an opaque message.
            _logger.LogError(
                "Google verification failed for product {ProductId}: {ErrorType}",
                request.ProductId,
                ex.GetType().Name);
            return VerificationResult.StoreError("Google verification is temporarily unavailable");
        }
    }

    public async Task<VerificationResult> VerifySubscriptionAsync(
        VerifyRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = await QuerySubscriptionAsync(request.ReceiptPayload, ct)
            .ConfigureAwait(false);
        if (!query.IsSuccess)
        {
            return query.Failure == GooglePlaySubscriptionQueryFailure.Retryable
                ? VerificationResult.Retryable(
                    query.ErrorCode ?? "STORE_ERROR",
                    query.ErrorMessage ?? "Google verification is temporarily unavailable")
                : VerificationResult.Invalid(
                    query.ErrorCode ?? "INVALID_RECEIPT",
                    query.ErrorMessage ?? "Google Play rejected the purchase token");
        }

        var snapshot = query.Snapshot!;
        if (!string.Equals(snapshot.ProductId, request.ProductId, StringComparison.Ordinal))
        {
            return VerificationResult.ProductMismatch(request.ProductId, snapshot.ProductId);
        }

        if (snapshot.State == GooglePlaySubscriptionState.Pending)
        {
            return VerificationResult.Retryable(
                "PURCHASE_PENDING",
                "The Google Play subscription payment is still pending");
        }

        if (snapshot.State is GooglePlaySubscriptionState.Paused or
            GooglePlaySubscriptionState.OnHold or
            GooglePlaySubscriptionState.Expired or
            GooglePlaySubscriptionState.PendingPurchaseCanceled)
        {
            return VerificationResult.Invalid(
                "SUBSCRIPTION_INACTIVE",
                $"The Google Play subscription is {snapshot.State}");
        }

        if (snapshot.State == GooglePlaySubscriptionState.Unspecified)
        {
            return VerificationResult.Retryable(
                "UNKNOWN_SUBSCRIPTION_STATE",
                "Google Play returned an unsupported subscription state");
        }

        var accountBindingFailure = ValidateAccountBinding(request, snapshot);
        if (accountBindingFailure != null)
        {
            return accountBindingFailure;
        }

        if (snapshot.StartTimeUtc == null || snapshot.ExpiryTimeUtc == null)
        {
            return VerificationResult.Invalid(
                "MALFORMED_STORE_RESPONSE",
                "Google Play omitted required subscription timestamps");
        }

        var status = snapshot.State switch
        {
            GooglePlaySubscriptionState.Active => SubscriptionStatus.Active,
            GooglePlaySubscriptionState.InGracePeriod => SubscriptionStatus.GracePeriod,
            GooglePlaySubscriptionState.Canceled => SubscriptionStatus.Cancelled,
            _ => SubscriptionStatus.None
        };

        if (status == SubscriptionStatus.None)
        {
            return VerificationResult.Invalid(
                "SUBSCRIPTION_INACTIVE",
                $"The Google Play subscription is {snapshot.State}");
        }

        if (snapshot.State == GooglePlaySubscriptionState.Canceled &&
            snapshot.ExpiryTimeUtc.Value <= DateTime.UtcNow)
        {
            return VerificationResult.Invalid(
                "SUBSCRIPTION_INACTIVE",
                "The canceled Google Play subscription has expired");
        }

        return VerificationResult.Valid() with
        {
            ProductId = snapshot.ProductId,
            TransactionId = snapshot.LatestSuccessfulOrderId ?? request.TransactionId,
            PurchaseDateUtc = snapshot.StartTimeUtc,
            ExpirationDateUtc = snapshot.ExpiryTimeUtc,
            IsSubscription = true,
            SubscriptionStatus = status,
            AutoRenew = snapshot.AutoRenewEnabled,
            IsSandbox = snapshot.IsTestPurchase,
            GracePeriodEndUtc = status == SubscriptionStatus.GracePeriod
                ? snapshot.ExpiryTimeUtc
                : null
        };
    }

    public async Task<GooglePlaySubscriptionQueryResult> QuerySubscriptionAsync(
        string purchaseToken,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(purchaseToken))
        {
            return GooglePlaySubscriptionQueryResult.Permanent(
                "INVALID_RECEIPT",
                "A Google Play purchase token is required");
        }

        try
        {
            var accessToken = await GetAccessTokenAsync(ct).ConfigureAwait(false);
            var escapedPackageName = Uri.EscapeDataString(_config.PackageName);
            var escapedPurchaseToken = Uri.EscapeDataString(purchaseToken);
            var url =
                $"https://androidpublisher.googleapis.com/androidpublisher/v3/applications/{escapedPackageName}/purchases/subscriptionsv2/tokens/{escapedPurchaseToken}";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(httpRequest, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Google subscriptionsv2 API returned {StatusCode}",
                    (int)response.StatusCode);
                return ClassifySubscriptionHttpFailure(response.StatusCode);
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct)
                .ConfigureAwait(false);
            return ParseSubscriptionSnapshot(responseBody);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // HttpClient exceptions may include the request URI. Never attach the
            // exception or its message because that URI contains the purchase token.
            _logger.LogError(
                "Google subscriptionsv2 query failed: {ErrorType}",
                ex.GetType().Name);
            return GooglePlaySubscriptionQueryResult.Retryable(
                "STORE_ERROR",
                "Google verification is temporarily unavailable");
        }
    }

    private VerificationResult? ValidateAccountBinding(
        VerifyRequest request,
        GooglePlaySubscriptionSnapshot snapshot)
    {
        return ValidateAccountBinding(
            request,
            snapshot.ExternalAccountIdentifiers?.ObfuscatedExternalAccountId);
    }

    private VerificationResult? ValidateAccountBinding(
        VerifyRequest request,
        string? actual)
    {
        var expected = request.ExpectedObfuscatedAccountId;
        if (string.IsNullOrWhiteSpace(expected))
        {
            return _config.RequireObfuscatedAccountId
                ? VerificationResult.Invalid(
                    "ACCOUNT_BINDING_REQUIRED",
                    "Google Play account binding is required")
                : null;
        }

        if (string.IsNullOrWhiteSpace(actual))
        {
            return VerificationResult.Invalid(
                "ACCOUNT_BINDING_MISSING",
                "The Google Play purchase is not bound to the expected account");
        }

        return FixedTimeEquals(expected, actual)
            ? null
            : VerificationResult.Invalid(
                "ACCOUNT_BINDING_MISMATCH",
                "The Google Play purchase belongs to a different account");
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static GooglePlaySubscriptionQueryResult ClassifySubscriptionHttpFailure(
        HttpStatusCode statusCode)
    {
        var numericStatus = (int)statusCode;
        if (statusCode is HttpStatusCode.BadRequest or
            HttpStatusCode.NotFound or
            HttpStatusCode.Gone)
        {
            return GooglePlaySubscriptionQueryResult.Permanent(
                "INVALID_RECEIPT",
                $"Google Play rejected the purchase token ({numericStatus})");
        }

        if (statusCode is HttpStatusCode.Conflict or
            HttpStatusCode.TooManyRequests ||
            numericStatus >= 500)
        {
            return GooglePlaySubscriptionQueryResult.Retryable(
                "STORE_ERROR",
                $"Google Play is temporarily unavailable ({numericStatus})");
        }

        return GooglePlaySubscriptionQueryResult.Retryable(
            "STORE_ERROR",
            $"Google Play verification failed ({numericStatus})");
    }

    private static VerificationResult ClassifyVerificationHttpFailure(HttpStatusCode statusCode)
    {
        var numericStatus = (int)statusCode;
        if (statusCode is HttpStatusCode.BadRequest or
            HttpStatusCode.NotFound or
            HttpStatusCode.Gone)
        {
            return VerificationResult.InvalidReceipt(
                $"Google Play rejected the purchase token ({numericStatus})");
        }

        return VerificationResult.StoreError(
            $"Google Play verification failed ({numericStatus})");
    }

    private static GooglePlaySubscriptionQueryResult ParseSubscriptionSnapshot(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryParseSubscriptionState(root, out var state) ||
                !root.TryGetProperty("lineItems", out var lineItems) ||
                lineItems.ValueKind != JsonValueKind.Array ||
                lineItems.GetArrayLength() != 1)
            {
                return InvalidSubscriptionShape();
            }

            var lineItem = lineItems[0];
            if (lineItem.ValueKind != JsonValueKind.Object ||
                !TryGetRequiredString(lineItem, "productId", out var productId) ||
                !TryReadOptionalUtcTimestamp(root, "startTime", out var startTime) ||
                !TryReadOptionalUtcTimestamp(lineItem, "expiryTime", out var expiryTime) ||
                !TryReadAutoRenewEnabled(lineItem, out var autoRenewEnabled) ||
                !TryReadExternalAccountIdentifiers(root, out var externalIdentifiers))
            {
                return InvalidSubscriptionShape();
            }

            var latestOrderId = ReadOptionalString(lineItem, "latestSuccessfulOrderId") ??
                                ReadOptionalString(root, "latestOrderId");

            return GooglePlaySubscriptionQueryResult.Success(
                new GooglePlaySubscriptionSnapshot
                {
                    State = state,
                    ProductId = productId,
                    StartTimeUtc = startTime,
                    ExpiryTimeUtc = expiryTime,
                    LatestSuccessfulOrderId = latestOrderId,
                    AutoRenewEnabled = autoRenewEnabled,
                    IsTestPurchase = root.TryGetProperty("testPurchase", out _),
                    LinkedPurchaseToken = ReadOptionalString(root, "linkedPurchaseToken"),
                    ExternalAccountIdentifiers = externalIdentifiers
                });
        }
        catch (JsonException)
        {
            return InvalidSubscriptionShape();
        }
        catch (InvalidOperationException)
        {
            return InvalidSubscriptionShape();
        }
    }

    private static bool TryParseSubscriptionState(
        JsonElement root,
        out GooglePlaySubscriptionState state)
    {
        state = GooglePlaySubscriptionState.Unspecified;
        if (!TryGetRequiredString(root, "subscriptionState", out var rawState))
        {
            return false;
        }

        state = rawState switch
        {
            "SUBSCRIPTION_STATE_PENDING" => GooglePlaySubscriptionState.Pending,
            "SUBSCRIPTION_STATE_ACTIVE" => GooglePlaySubscriptionState.Active,
            "SUBSCRIPTION_STATE_PAUSED" => GooglePlaySubscriptionState.Paused,
            "SUBSCRIPTION_STATE_IN_GRACE_PERIOD" => GooglePlaySubscriptionState.InGracePeriod,
            "SUBSCRIPTION_STATE_ON_HOLD" => GooglePlaySubscriptionState.OnHold,
            "SUBSCRIPTION_STATE_CANCELED" => GooglePlaySubscriptionState.Canceled,
            "SUBSCRIPTION_STATE_EXPIRED" => GooglePlaySubscriptionState.Expired,
            "SUBSCRIPTION_STATE_PENDING_PURCHASE_CANCELED" =>
                GooglePlaySubscriptionState.PendingPurchaseCanceled,
            _ => GooglePlaySubscriptionState.Unspecified
        };
        return true;
    }

    private static bool TryReadOptionalUtcTimestamp(
        JsonElement parent,
        string propertyName,
        out DateTime? value)
    {
        value = null;
        if (!parent.TryGetProperty(propertyName, out var property))
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(
                property.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return false;
        }

        value = parsed.UtcDateTime;
        return true;
    }

    private static bool TryReadAutoRenewEnabled(JsonElement lineItem, out bool enabled)
    {
        enabled = false;
        if (!lineItem.TryGetProperty("autoRenewingPlan", out var plan))
        {
            return true;
        }

        if (plan.ValueKind != JsonValueKind.Object ||
            !plan.TryGetProperty("autoRenewEnabled", out var property) ||
            property.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            return false;
        }

        enabled = property.GetBoolean();
        return true;
    }

    private static bool TryReadExternalAccountIdentifiers(
        JsonElement root,
        out GooglePlayExternalAccountIdentifiers? identifiers)
    {
        identifiers = null;
        if (!root.TryGetProperty("externalAccountIdentifiers", out var value))
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        identifiers = new GooglePlayExternalAccountIdentifiers
        {
            ExternalAccountId = ReadOptionalString(value, "externalAccountId"),
            ObfuscatedExternalAccountId = ReadOptionalString(
                value,
                "obfuscatedExternalAccountId"),
            ObfuscatedExternalProfileId = ReadOptionalString(
                value,
                "obfuscatedExternalProfileId")
        };
        return true;
    }

    private static bool TryGetRequiredString(
        JsonElement parent,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!parent.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string? ReadOptionalString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool TryReadUnixMilliseconds(
        JsonElement parent,
        string propertyName,
        out DateTime purchaseDateUtc)
    {
        purchaseDateUtc = default;
        if (!parent.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        long milliseconds;
        if (property.ValueKind == JsonValueKind.Number)
        {
            if (!property.TryGetInt64(out milliseconds))
            {
                return false;
            }
        }
        else if (property.ValueKind == JsonValueKind.String)
        {
            if (!long.TryParse(
                    property.GetString(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out milliseconds))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        try
        {
            purchaseDateUtc = DateTimeOffset
                .FromUnixTimeMilliseconds(milliseconds)
                .UtcDateTime;
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static GooglePlaySubscriptionQueryResult InvalidSubscriptionShape() =>
        GooglePlaySubscriptionQueryResult.Permanent(
            "UNSUPPORTED_SUBSCRIPTION_SHAPE",
            "Google Play returned an unsupported subscription shape");

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        // Return cached token if still valid
        if (_cachedAccessToken != null && DateTime.UtcNow < _tokenExpiresAt)
        {
            return _cachedAccessToken;
        }

        // Generate JWT for service account
        var jwt = GenerateServiceAccountJwt();

        // Exchange JWT for access token
        var tokenRequest = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
            new KeyValuePair<string, string>("assertion", jwt)
        });

        var response = await _httpClient.PostAsync(
            "https://oauth2.googleapis.com/token",
            tokenRequest,
            ct);

        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        var tokenResponse = JsonDocument.Parse(responseBody).RootElement;

        _cachedAccessToken = tokenResponse.GetProperty("access_token").GetString()!;
        var expiresIn = tokenResponse.GetProperty("expires_in").GetInt32();
        _tokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn - 60); // Buffer of 60 seconds

        return _cachedAccessToken;
    }

    private string GenerateServiceAccountJwt()
    {
        var now = DateTimeOffset.UtcNow;
        var header = new { alg = "RS256", typ = "JWT" };
        var payload = new
        {
            iss = _config.ServiceAccountEmail,
            scope = "https://www.googleapis.com/auth/androidpublisher",
            aud = "https://oauth2.googleapis.com/token",
            iat = now.ToUnixTimeSeconds(),
            exp = now.AddHours(1).ToUnixTimeSeconds()
        };

        var headerJson = JsonSerializer.Serialize(header);
        var payloadJson = JsonSerializer.Serialize(payload);

        var headerBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        var dataToSign = $"{headerBase64}.{payloadBase64}";

        // Sign with RSA private key
        using var rsa = System.Security.Cryptography.RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(_config.PrivateKeyBase64), out _);

        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(dataToSign),
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);

        var signatureBase64 = Base64UrlEncode(signature);

        return $"{dataToSign}.{signatureBase64}";
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}

/// <summary>
/// Configuration for Google Play verifier.
/// </summary>
public sealed class GoogleVerifierConfig
{
    /// <summary>
    /// Service account email.
    /// </summary>
    public string ServiceAccountEmail { get; set; } = string.Empty;

    /// <summary>
    /// Private key from service account JSON (Base64 encoded).
    /// </summary>
    public string PrivateKeyBase64 { get; set; } = string.Empty;

    /// <summary>
    /// Application package name.
    /// </summary>
    public string PackageName { get; set; } = string.Empty;

    /// <summary>
    /// Requires the Google Play purchase to be bound to the authenticated player via
    /// externalAccountIdentifiers.obfuscatedExternalAccountId.
    /// </summary>
    public bool RequireObfuscatedAccountId { get; set; } = true;
}
