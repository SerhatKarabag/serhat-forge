using System;
using System.Net.Http;
using System.Net.Http.Headers;
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
public sealed class GooglePlayStoreVerifier : IStoreVerifier, IDisposable
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

            var packageName = request.PackageName ?? _config.PackageName;
            var url = $"https://androidpublisher.googleapis.com/androidpublisher/v3/applications/{packageName}/purchases/products/{request.ProductId}/tokens/{request.ReceiptPayload}";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(httpRequest, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Google API error: {Status}",
                    response.StatusCode);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return VerificationResult.InvalidReceipt("Purchase not found");
                }

                return VerificationResult.StoreError($"Google API error: {response.StatusCode}");
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            var purchase = JsonDocument.Parse(responseBody).RootElement;

            // Check purchase state
            var purchaseState = purchase.GetProperty("purchaseState").GetInt32();
            if (purchaseState != 0) // 0 = Purchased
            {
                return VerificationResult.Invalid("PURCHASE_NOT_COMPLETED",
                    $"Purchase state is {purchaseState}, expected 0 (Purchased)");
            }

            // Check consumption state for consumables
            var consumptionState = purchase.TryGetProperty("consumptionState", out var cs)
                ? cs.GetInt32()
                : 0;

            return VerificationResult.Valid() with
            {
                ProductId = request.ProductId,
                TransactionId = purchase.TryGetProperty("orderId", out var oid)
                    ? oid.GetString()
                    : request.TransactionId,
                PurchaseDateUtc = purchase.TryGetProperty("purchaseTimeMillis", out var pt)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(pt.GetInt64()).UtcDateTime
                    : DateTime.UtcNow,
                IsSubscription = false,
                IsSandbox = purchase.TryGetProperty("purchaseType", out var ptype) &&
                           ptype.GetInt32() == 0 // 0 = Test
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google verification failed for product {ProductId}", request.ProductId);
            return VerificationResult.StoreError(ex.Message);
        }
    }

    public async Task<VerificationResult> VerifySubscriptionAsync(
        VerifyRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var accessToken = await GetAccessTokenAsync(ct);

            var packageName = request.PackageName ?? _config.PackageName;
            var url = $"https://androidpublisher.googleapis.com/androidpublisher/v3/applications/{packageName}/purchases/subscriptions/{request.ProductId}/tokens/{request.ReceiptPayload}";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(httpRequest, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Google subscription API error: {Status}",
                    response.StatusCode);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return VerificationResult.InvalidReceipt("Subscription not found");
                }

                return VerificationResult.StoreError($"Google API error: {response.StatusCode}");
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            var subscription = JsonDocument.Parse(responseBody).RootElement;

            // Extract expiry time
            var expiryTimeMillis = subscription.GetProperty("expiryTimeMillis").GetInt64();
            var expiryDate = DateTimeOffset.FromUnixTimeMilliseconds(expiryTimeMillis).UtcDateTime;

            // Determine status
            var status = DetermineSubscriptionStatus(subscription, expiryDate);

            return VerificationResult.Valid() with
            {
                ProductId = request.ProductId,
                TransactionId = subscription.TryGetProperty("orderId", out var oid)
                    ? oid.GetString()
                    : request.TransactionId,
                PurchaseDateUtc = subscription.TryGetProperty("startTimeMillis", out var st)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(st.GetInt64()).UtcDateTime
                    : DateTime.UtcNow,
                ExpirationDateUtc = expiryDate,
                IsSubscription = true,
                SubscriptionStatus = status,
                AutoRenew = subscription.TryGetProperty("autoRenewing", out var ar) && ar.GetBoolean(),
                IsSandbox = subscription.TryGetProperty("purchaseType", out var ptype) &&
                           ptype.GetInt32() == 0,
                GracePeriodEndUtc = subscription.TryGetProperty("obfuscatedExternalAccountId", out _)
                    ? null  // Would need additional logic for grace period
                    : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google subscription verification failed for product {ProductId}",
                request.ProductId);
            return VerificationResult.StoreError(ex.Message);
        }
    }

    private SubscriptionStatus DetermineSubscriptionStatus(JsonElement subscription, DateTime expiryDate)
    {
        // Check for cancellation
        if (subscription.TryGetProperty("cancelReason", out var cancelReason))
        {
            var reason = cancelReason.GetInt32();
            if (reason == 1) // User cancelled
            {
                return expiryDate > DateTime.UtcNow
                    ? SubscriptionStatus.Cancelled
                    : SubscriptionStatus.Expired;
            }
            if (reason == 2) // System cancelled (billing issue)
            {
                return SubscriptionStatus.Expired;
            }
            if (reason == 3) // Developer cancelled
            {
                return SubscriptionStatus.Refunded;
            }
        }

        // Check for pause
        if (subscription.TryGetProperty("autoResumeTimeMillis", out _))
        {
            return SubscriptionStatus.Paused;
        }

        // Check if expired
        if (expiryDate < DateTime.UtcNow)
        {
            // Check for grace period
            if (subscription.TryGetProperty("paymentState", out var ps) && ps.GetInt32() == 0)
            {
                return SubscriptionStatus.GracePeriod;
            }
            return SubscriptionStatus.Expired;
        }

        return SubscriptionStatus.Active;
    }

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
}
