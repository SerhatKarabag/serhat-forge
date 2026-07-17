using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;
using Serhat.Forge.CloudScript.Framework.Monetization.Verification;
using Serhat.Forge.CloudScript.Infrastructure.Telemetry;
using Xunit;

namespace Serhat.Forge.CloudScript.Tests.Monetization;

public sealed class AppleStoreVerifierTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid AccountToken =
        StoreAccountIdentity.CreateAppleAppAccountToken("player-123");

    private static readonly string PrivateKeyBase64 = CreatePrivateKey();

    [Fact]
    public async Task VerifyOneTimePurchase_ActiveBoundTransaction_IsValid()
    {
        var verifier = CreateVerifier(CreateTransaction());

        var result = await verifier.VerifyOneTimePurchaseAsync(CreateRequest());

        Assert.True(result.IsValid);
        Assert.False(result.IsSubscription);
        Assert.Equal("product-1", result.ProductId);
        Assert.Equal("transaction-1", result.TransactionId);
    }

    [Fact]
    public async Task VerifySubscription_UnexpiredBoundTransaction_IsValid()
    {
        var payload = CreateTransaction(
            productType: "Auto-Renewable Subscription",
            expiresDate: Now.AddDays(1).ToUnixTimeMilliseconds(),
            originalTransactionId: "original-1");
        var verifier = CreateVerifier(payload);
        var request = CreateRequest(ProductType.Subscription);

        var result = await verifier.VerifySubscriptionAsync(request);

        Assert.True(result.IsValid);
        Assert.True(result.IsSubscription);
        Assert.Equal("original-1", result.OriginalTransactionId);
        Assert.Equal(SubscriptionStatus.Active, result.SubscriptionStatus);
        Assert.Equal(Now.AddDays(1).UtcDateTime, result.ExpirationDateUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verify_RevokedTransaction_IsNeverValid(bool subscription)
    {
        var payload = CreateTransaction(
            productType: subscription ? "Auto-Renewable Subscription" : "Consumable",
            expiresDate: subscription ? Now.AddDays(1).ToUnixTimeMilliseconds() : null,
            originalTransactionId: subscription ? "original-1" : null,
            revocationDate: Now.AddHours(-1).ToUnixTimeMilliseconds());
        var verifier = CreateVerifier(payload);
        var request = CreateRequest(
            subscription ? ProductType.Subscription : ProductType.Consumable);

        var result = subscription
            ? await verifier.VerifySubscriptionAsync(request)
            : await verifier.VerifyOneTimePurchaseAsync(request);

        Assert.False(result.IsValid);
        Assert.False(result.IsRetryable);
        Assert.Equal("REVOKED_TRANSACTION", result.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task VerifySubscription_ExpiredAtOrBeforeAuthoritativeNow_IsRejected(
        int secondsFromNow)
    {
        var verifier = CreateVerifier(CreateTransaction(
            productType: "Auto-Renewable Subscription",
            expiresDate: Now.AddSeconds(secondsFromNow).ToUnixTimeMilliseconds(),
            originalTransactionId: "original-1"));

        var result = await verifier.VerifySubscriptionAsync(
            CreateRequest(ProductType.Subscription));

        Assert.False(result.IsValid);
        Assert.False(result.IsRetryable);
        Assert.Equal("EXPIRED_RECEIPT", result.ErrorCode);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task VerifySubscription_MissingExpiryOrOriginalTransaction_IsRejected(
        bool includeExpiry,
        bool includeOriginalTransaction)
    {
        var verifier = CreateVerifier(CreateTransaction(
            productType: "Auto-Renewable Subscription",
            expiresDate: includeExpiry ? Now.AddDays(1).ToUnixTimeMilliseconds() : null,
            originalTransactionId: includeOriginalTransaction ? "original-1" : null));

        var result = await verifier.VerifySubscriptionAsync(
            CreateRequest(ProductType.Subscription));

        Assert.False(result.IsValid);
        Assert.False(result.IsRetryable);
        Assert.Equal("INVALID_RECEIPT", result.ErrorCode);
    }

    [Theory]
    [InlineData(ProductType.Consumable, "Non-Consumable")]
    [InlineData(ProductType.NonConsumable, "Consumable")]
    [InlineData(ProductType.Consumable, "Auto-Renewable Subscription")]
    public async Task VerifyOneTimePurchase_SignedTypeMustExactlyMatchCatalog(
        ProductType expectedType,
        string signedType)
    {
        var verifier = CreateVerifier(CreateTransaction(productType: signedType));

        var result = await verifier.VerifyOneTimePurchaseAsync(CreateRequest(expectedType));

        Assert.False(result.IsValid);
        Assert.Equal("PRODUCT_TYPE_MISMATCH", result.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    public async Task Verify_QuantityOtherThanOne_IsRejected(int quantity)
    {
        var verifier = CreateVerifier(CreateTransaction(quantity: quantity));

        var result = await verifier.VerifyOneTimePurchaseAsync(CreateRequest());

        Assert.False(result.IsValid);
        Assert.Equal("INVALID_QUANTITY", result.ErrorCode);
    }

    [Fact]
    public async Task Verify_AccountTokenMissingOrMismatched_FailsClosed()
    {
        var missingVerifier = CreateVerifier(CreateTransaction(includeAppAccountToken: false));
        var mismatchedVerifier = CreateVerifier(CreateTransaction(
            appAccountToken: Guid.Parse("11111111-2222-8333-8444-555555555555")));

        var missing = await missingVerifier.VerifyOneTimePurchaseAsync(CreateRequest());
        var mismatched = await mismatchedVerifier.VerifyOneTimePurchaseAsync(CreateRequest());

        Assert.Equal("APPLE_ACCOUNT_BINDING_MISSING", missing.ErrorCode);
        Assert.Equal("APPLE_ACCOUNT_MISMATCH", mismatched.ErrorCode);
        Assert.False(missing.IsRetryable);
        Assert.False(mismatched.IsRetryable);
    }

    [Fact]
    public async Task Verify_ExpectedAccountTokenMissing_IsRetryableConfigurationFailure()
    {
        var verifier = CreateVerifier(CreateTransaction());
        var request = CreateRequest();
        request.ExpectedAppleAppAccountToken = null;

        var result = await verifier.VerifyOneTimePurchaseAsync(request);

        Assert.False(result.IsValid);
        Assert.True(result.IsRetryable);
        Assert.Equal("APPLE_ACCOUNT_BINDING_CONFIGURATION_ERROR", result.ErrorCode);
    }

    [Fact]
    public async Task Verify_LocalMigrationMode_AllowsMissingAccountToken()
    {
        var config = CreateConfig();
        config.RequireAppAccountToken = false;
        var verifier = CreateVerifier(
            CreateTransaction(includeAppAccountToken: false),
            config: config);
        var request = CreateRequest();
        request.ExpectedAppleAppAccountToken = null;

        var result = await verifier.VerifyOneTimePurchaseAsync(request);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("bundleId")]
    [InlineData("environment")]
    [InlineData("appAppleId")]
    public async Task Verify_ApplicationIdentityMismatch_IsRejected(string field)
    {
        var overrides = new Dictionary<string, object?>
        {
            [field] = field == "appAppleId" ? 999_999L : "different"
        };
        var verifier = CreateVerifier(CreateTransaction(overrides: overrides));

        var result = await verifier.VerifyOneTimePurchaseAsync(CreateRequest());

        Assert.False(result.IsValid);
        Assert.Equal("INVALID_RECEIPT", result.ErrorCode);
    }

    [Fact]
    public async Task Verify_AppAppleIdAbsent_RemainsCompatibleWithTransactionPayloadsWithoutField()
    {
        var verifier = CreateVerifier(CreateTransaction(includeAppAppleId: false));

        var result = await verifier.VerifyOneTimePurchaseAsync(CreateRequest());

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, false, "APPLE_TRANSACTION_INVALID")]
    [InlineData(HttpStatusCode.NotFound, false, "APPLE_TRANSACTION_INVALID")]
    [InlineData(HttpStatusCode.Unauthorized, true, "APPLE_CONFIGURATION_ERROR")]
    [InlineData(HttpStatusCode.Forbidden, true, "APPLE_CONFIGURATION_ERROR")]
    [InlineData(HttpStatusCode.RequestTimeout, true, "APPLE_STORE_UNAVAILABLE")]
    [InlineData((HttpStatusCode)429, true, "APPLE_STORE_UNAVAILABLE")]
    [InlineData(HttpStatusCode.InternalServerError, true, "APPLE_STORE_UNAVAILABLE")]
    public async Task Verify_HttpFailure_IsClassified(
        HttpStatusCode statusCode,
        bool retryable,
        string expectedCode)
    {
        var verifier = CreateVerifier(CreateTransaction(), statusCode);

        var result = await verifier.VerifyOneTimePurchaseAsync(CreateRequest());

        Assert.False(result.IsValid);
        Assert.Equal(retryable, result.IsRetryable);
        Assert.Equal(expectedCode, result.ErrorCode);
    }

    [Fact]
    public async Task Verify_Cancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new StubHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var verifier = CreateVerifier(CreateTransaction(), handler: handler);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            verifier.VerifyOneTimePurchaseAsync(CreateRequest(), cancellation.Token));
    }

    [Fact]
    public async Task Verify_AppleReceiptPayload_IsIgnoredAndNeverAddedToServerApiUri()
    {
        const string secretReceipt = "RAW-APPLE-RECEIPT-MUST-NOT-LEAVE-CLIENT";
        Uri? requestUri = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            requestUri = request.RequestUri;
            return Task.FromResult(CreateHttpResponse(CreateTransaction()));
        });
        using var verifier = CreateVerifier(CreateTransaction(), handler: handler);
        var request = CreateRequest();
        request.ReceiptPayload = secretReceipt;

        var result = await verifier.VerifyOneTimePurchaseAsync(request);

        Assert.True(result.IsValid);
        Assert.NotNull(requestUri);
        Assert.DoesNotContain(secretReceipt, requestUri!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public void ClientAndBackendAppleAccountIdentityContracts_AreExactlyEqual()
    {
        var clientToken = Serhat.Backend.Monetization.Domain.StoreAccountIdentity
            .CreateAppleAppAccountToken("player-123");
        var backendToken = StoreAccountIdentity.CreateAppleAppAccountToken("player-123");

        Assert.Equal(clientToken, backendToken);
        Assert.Equal(8, (clientToken.ToByteArray()[7] >> 4) & 0x0F);
        Assert.True("89ab".Contains(clientToken.ToString("D")[19]));
    }

    [Fact]
    public void TelemetryProcessor_RedactsAppleTransactionId_FromNameAndData()
    {
        const string transactionId = "2000000123456789";
        var sink = new CapturingTelemetryProcessor();
        var processor = new AppleTransactionIdTelemetryProcessor(sink);
        var telemetry = new DependencyTelemetry
        {
            Name = $"GET /inApps/v1/transactions/{transactionId}",
            Data = $"https://api.storekit.itunes.apple.com/inApps/v1/transactions/{transactionId}?x=1"
        };

        processor.Process(telemetry);

        var captured = Assert.IsType<DependencyTelemetry>(sink.Item);
        Assert.DoesNotContain(transactionId, captured.Name, StringComparison.Ordinal);
        Assert.DoesNotContain(transactionId, captured.Data, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", captured.Name, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", captured.Data, StringComparison.Ordinal);
    }

    private static AppleStoreVerifier CreateVerifier(
        string signedPayload,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        AppleVerifierConfig? config = null,
        HttpMessageHandler? handler = null)
    {
        var jwsVerifier = new StubJwsVerifier(signedPayload);
        handler ??= new StubHttpMessageHandler((_, _) =>
            Task.FromResult(
                statusCode == HttpStatusCode.OK
                    ? CreateHttpResponse(signedPayload)
                    : new HttpResponseMessage(statusCode)));

        return new AppleStoreVerifier(
            config ?? CreateConfig(),
            NullLogger<AppleStoreVerifier>.Instance,
            new HttpClient(handler, disposeHandler: true),
            jwsVerifier,
            () => Now);
    }

    private static HttpResponseMessage CreateHttpResponse(string signedPayload) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { signedTransactionInfo = "fake-jws" }),
                Encoding.UTF8,
                "application/json")
        };

    private static AppleVerifierConfig CreateConfig() => new()
    {
        IssuerId = "issuer-id",
        KeyId = "key-id",
        PrivateKeyBase64 = PrivateKeyBase64,
        BundleId = "com.serhat.forge",
        AppAppleId = 123_456,
        ExpectedEnvironment = "Production",
        RequireAppAccountToken = true
    };

    private static VerifyRequest CreateRequest(
        ProductType productType = ProductType.Consumable) =>
        new()
        {
            ProductId = "product-1",
            TransactionId = "transaction-1",
            ReceiptPayload = string.Empty,
            ExpectedAppleAppAccountToken = AccountToken.ToString("D"),
            ExpectedProductType = productType,
            IsSubscription = productType == ProductType.Subscription
        };

    private static string CreateTransaction(
        string productType = "Consumable",
        int quantity = 1,
        long? expiresDate = null,
        string? originalTransactionId = null,
        long? revocationDate = null,
        Guid? appAccountToken = null,
        bool includeAppAccountToken = true,
        bool includeAppAppleId = true,
        IReadOnlyDictionary<string, object?>? overrides = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["bundleId"] = "com.serhat.forge",
            ["environment"] = "Production",
            ["productId"] = "product-1",
            ["transactionId"] = "transaction-1",
            ["purchaseDate"] = Now.AddMinutes(-1).ToUnixTimeMilliseconds(),
            ["type"] = productType,
            ["quantity"] = quantity
        };

        if (includeAppAppleId)
        {
            payload["appAppleId"] = 123_456L;
        }

        if (includeAppAccountToken)
        {
            payload["appAccountToken"] = (appAccountToken ?? AccountToken).ToString("D");
        }

        if (expiresDate.HasValue)
        {
            payload["expiresDate"] = expiresDate.Value;
        }

        if (originalTransactionId != null)
        {
            payload["originalTransactionId"] = originalTransactionId;
        }

        if (revocationDate.HasValue)
        {
            payload["revocationDate"] = revocationDate.Value;
        }

        if (overrides != null)
        {
            foreach (var pair in overrides)
            {
                payload[pair.Key] = pair.Value;
            }
        }

        return JsonSerializer.Serialize(payload);
    }

    private static string CreatePrivateKey()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey());
    }

    private sealed class StubJwsVerifier : IAppleJwsVerifier
    {
        private readonly string _payload;

        public StubJwsVerifier(string payload) => _payload = payload;

        public AppleJwsVerificationResult Verify(string compactJws) =>
            AppleJwsVerificationResult.Success(_payload);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>
            _handler;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
            _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }

    private sealed class CapturingTelemetryProcessor : ITelemetryProcessor
    {
        public ITelemetry? Item { get; private set; }

        public void Process(ITelemetry item) => Item = item;
    }
}
