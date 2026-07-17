using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Configuration;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;
using Serhat.Forge.CloudScript.Framework.Monetization.Verification;
using Serhat.Forge.CloudScript.Infrastructure.Telemetry;
using Xunit;

namespace Serhat.Forge.CloudScript.Tests.Monetization;

public sealed class GooglePlayStoreVerifierTests
{
    private const string ProductId = "premium_monthly";
    private const string PurchaseToken = "sensitive-purchase-token";
    private const string AccountId = "player-binding-7f39";
    private static readonly string PrivateKeyBase64 = CreatePrivateKey();

    [Theory]
    [InlineData("SUBSCRIPTION_STATE_ACTIVE", true, false, SubscriptionStatus.Active)]
    [InlineData("SUBSCRIPTION_STATE_IN_GRACE_PERIOD", true, false, SubscriptionStatus.GracePeriod)]
    [InlineData("SUBSCRIPTION_STATE_CANCELED", true, false, SubscriptionStatus.Cancelled)]
    [InlineData("SUBSCRIPTION_STATE_PENDING", false, true, null)]
    [InlineData("SUBSCRIPTION_STATE_PAUSED", false, false, null)]
    [InlineData("SUBSCRIPTION_STATE_ON_HOLD", false, false, null)]
    [InlineData("SUBSCRIPTION_STATE_EXPIRED", false, false, null)]
    [InlineData("SUBSCRIPTION_STATE_PENDING_PURCHASE_CANCELED", false, false, null)]
    public async Task VerifySubscription_V2State_UsesFailClosedGrantPolicy(
        string state,
        bool expectedValid,
        bool expectedRetryable,
        SubscriptionStatus? expectedStatus)
    {
        using var verifier = CreateVerifier(_ => JsonResponse(CreateSubscriptionJson(state)));

        var result = await verifier.VerifySubscriptionAsync(CreateRequest());

        Assert.Equal(expectedValid, result.IsValid);
        Assert.Equal(expectedRetryable, result.IsRetryable);
        Assert.Equal(expectedStatus, result.SubscriptionStatus);
    }

    [Fact]
    public async Task VerifySubscription_CanceledAfterExpiry_DoesNotGrant()
    {
        var expired = DateTimeOffset.UtcNow.AddMinutes(-1);
        using var verifier = CreateVerifier(
            _ => JsonResponse(CreateSubscriptionJson(
                "SUBSCRIPTION_STATE_CANCELED",
                expiryTime: expired)));

        var result = await verifier.VerifySubscriptionAsync(CreateRequest());

        Assert.False(result.IsValid);
        Assert.False(result.IsRetryable);
        Assert.Equal("SUBSCRIPTION_INACTIVE", result.ErrorCode);
    }

    [Fact]
    public async Task QuerySubscription_ParsesV2SnapshotFields()
    {
        using var verifier = CreateVerifier(
            _ => JsonResponse(CreateSubscriptionJson("SUBSCRIPTION_STATE_ACTIVE")));

        var result = await verifier.QuerySubscriptionAsync(PurchaseToken);

        Assert.True(result.IsSuccess);
        var snapshot = Assert.IsType<GooglePlaySubscriptionSnapshot>(result.Snapshot);
        Assert.Equal(GooglePlaySubscriptionState.Active, snapshot.State);
        Assert.Equal(ProductId, snapshot.ProductId);
        Assert.Equal("GPA.1234-5678-9012-34567", snapshot.LatestSuccessfulOrderId);
        Assert.True(snapshot.AutoRenewEnabled);
        Assert.True(snapshot.IsTestPurchase);
        Assert.Equal("linked-sensitive-token", snapshot.LinkedPurchaseToken);
        Assert.NotNull(snapshot.StartTimeUtc);
        Assert.NotNull(snapshot.ExpiryTimeUtc);
        Assert.Equal(
            AccountId,
            snapshot.ExternalAccountIdentifiers?.ObfuscatedExternalAccountId);
        Assert.Equal(
            "profile-binding",
            snapshot.ExternalAccountIdentifiers?.ObfuscatedExternalProfileId);
        Assert.Equal("legacy-account", snapshot.ExternalAccountIdentifiers?.ExternalAccountId);
    }

    [Fact]
    public async Task VerifySubscription_ProductMismatch_IsRejected()
    {
        using var verifier = CreateVerifier(
            _ => JsonResponse(CreateSubscriptionJson(
                "SUBSCRIPTION_STATE_ACTIVE",
                productId: "different_product")));

        var result = await verifier.VerifySubscriptionAsync(CreateRequest());

        Assert.False(result.IsValid);
        Assert.False(result.IsRetryable);
        Assert.Equal("PRODUCT_MISMATCH", result.ErrorCode);
    }

    [Fact]
    public async Task VerifySubscription_MultipleLineItems_IsRejected()
    {
        var lineItem = CreateLineItem(ProductId, DateTimeOffset.UtcNow.AddDays(30));
        var json = JsonSerializer.Serialize(new
        {
            subscriptionState = "SUBSCRIPTION_STATE_ACTIVE",
            startTime = DateTimeOffset.UtcNow.AddDays(-1).ToString("O"),
            lineItems = new[] { lineItem, lineItem }
        });
        using var verifier = CreateVerifier(_ => JsonResponse(json));

        var result = await verifier.VerifySubscriptionAsync(CreateRequest());

        Assert.False(result.IsValid);
        Assert.False(result.IsRetryable);
        Assert.Equal("UNSUPPORTED_SUBSCRIPTION_SHAPE", result.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task QuerySubscription_PermanentHttpStatus_IsNotRetryable(
        HttpStatusCode statusCode)
    {
        using var verifier = CreateVerifier(_ => new HttpResponseMessage(statusCode));

        var result = await verifier.QuerySubscriptionAsync(PurchaseToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(GooglePlaySubscriptionQueryFailure.Permanent, result.Failure);
        Assert.Equal("INVALID_RECEIPT", result.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task QuerySubscription_TransientHttpStatus_IsRetryable(
        HttpStatusCode statusCode)
    {
        using var verifier = CreateVerifier(_ => new HttpResponseMessage(statusCode));

        var result = await verifier.QuerySubscriptionAsync(PurchaseToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(GooglePlaySubscriptionQueryFailure.Retryable, result.Failure);
        Assert.Equal("STORE_ERROR", result.ErrorCode);
    }

    [Fact]
    public async Task QuerySubscription_CallerCancellation_Propagates()
    {
        using var verifier = CreateVerifier(
            async (_, ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                throw new InvalidOperationException("Unreachable");
            });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => verifier.QuerySubscriptionAsync(PurchaseToken, cts.Token));
    }

    [Fact]
    public async Task VerifySubscription_RequiredAccountBindingMissingFromRequest_FailsClosed()
    {
        using var verifier = CreateVerifier(_ => JsonResponse(
            CreateSubscriptionJson("SUBSCRIPTION_STATE_ACTIVE")));
        var request = CreateRequest();
        request.ExpectedObfuscatedAccountId = null;

        var result = await verifier.VerifySubscriptionAsync(request);

        Assert.False(result.IsValid);
        Assert.Equal("ACCOUNT_BINDING_REQUIRED", result.ErrorCode);
    }

    [Fact]
    public void ProductionConfiguration_DisabledGoogleAccountBinding_FailsFast()
    {
        var config = new MonetizationConfig
        {
            EnvironmentName = "Production",
            Google = new GoogleStoreConfig { RequireObfuscatedAccountId = false }
        };

        var exception = Assert.Throws<InvalidOperationException>(config.ValidateForStartup);

        Assert.Contains("GOOGLE_REQUIRE_OBFUSCATED_ACCOUNT_ID", exception.Message);
    }

    [Fact]
    public async Task VerifySubscription_AccountBindingMismatch_FailsClosed()
    {
        using var verifier = CreateVerifier(_ => JsonResponse(
            CreateSubscriptionJson("SUBSCRIPTION_STATE_ACTIVE")));
        var request = CreateRequest();
        request.ExpectedObfuscatedAccountId = "different-player";

        var result = await verifier.VerifySubscriptionAsync(request);

        Assert.False(result.IsValid);
        Assert.Equal("ACCOUNT_BINDING_MISMATCH", result.ErrorCode);
    }

    [Fact]
    public async Task VerifyOneTimePurchase_MatchingAccountBinding_IsValid()
    {
        using var verifier = CreateVerifier(
            _ => JsonResponse(CreateOneTimePurchaseJson(AccountId)));
        var request = CreateRequest();
        request.IsSubscription = false;

        var result = await verifier.VerifyOneTimePurchaseAsync(request);

        Assert.True(result.IsValid);
        Assert.Equal("GPA.1111-2222-3333-44444", result.TransactionId);
        Assert.False(result.IsSubscription);
    }

    [Fact]
    public async Task VerifyOneTimePurchase_MissingStoreAccountBinding_FailsClosed()
    {
        using var verifier = CreateVerifier(
            _ => JsonResponse(CreateOneTimePurchaseJson(null)));
        var request = CreateRequest();
        request.IsSubscription = false;

        var result = await verifier.VerifyOneTimePurchaseAsync(request);

        Assert.False(result.IsValid);
        Assert.Equal("ACCOUNT_BINDING_MISSING", result.ErrorCode);
    }

    [Fact]
    public async Task VerifyOneTimePurchase_AccountBindingMismatch_FailsClosed()
    {
        using var verifier = CreateVerifier(
            _ => JsonResponse(CreateOneTimePurchaseJson("different-player")));
        var request = CreateRequest();
        request.IsSubscription = false;

        var result = await verifier.VerifyOneTimePurchaseAsync(request);

        Assert.False(result.IsValid);
        Assert.Equal("ACCOUNT_BINDING_MISMATCH", result.ErrorCode);
    }

    [Fact]
    public async Task VerifyOneTimePurchase_ProductMismatch_FailsClosed()
    {
        using var verifier = CreateVerifier(_ => JsonResponse(
            CreateOneTimePurchaseJson(AccountId, productId: "different-product")));
        var request = CreateRequest();
        request.IsSubscription = false;

        var result = await verifier.VerifyOneTimePurchaseAsync(request);

        Assert.False(result.IsValid);
        Assert.Equal("PRODUCT_MISMATCH", result.ErrorCode);
    }

    [Fact]
    public async Task VerifyOneTimePurchase_MultiQuantity_FailsClosed()
    {
        using var verifier = CreateVerifier(_ => JsonResponse(
            CreateOneTimePurchaseJson(AccountId, quantity: 2, refundableQuantity: 2)));
        var request = CreateRequest();
        request.IsSubscription = false;

        var result = await verifier.VerifyOneTimePurchaseAsync(request);

        Assert.False(result.IsValid);
        Assert.Equal("UNSUPPORTED_PURCHASE_QUANTITY", result.ErrorCode);
    }

    [Fact]
    public async Task VerifyOneTimePurchase_RefundedQuantity_FailsClosed()
    {
        using var verifier = CreateVerifier(_ => JsonResponse(
            CreateOneTimePurchaseJson(AccountId, quantity: 1, refundableQuantity: 0)));
        var request = CreateRequest();
        request.IsSubscription = false;

        var result = await verifier.VerifyOneTimePurchaseAsync(request);

        Assert.False(result.IsValid);
        Assert.Equal("PURCHASE_REFUNDED", result.ErrorCode);
    }

    [Fact]
    public async Task VerifyOneTimePurchase_ResponseTokenMismatch_FailsClosed()
    {
        using var verifier = CreateVerifier(_ => JsonResponse(
            CreateOneTimePurchaseJson(AccountId, purchaseToken: "different-token")));
        var request = CreateRequest();
        request.IsSubscription = false;

        var result = await verifier.VerifyOneTimePurchaseAsync(request);

        Assert.False(result.IsValid);
        Assert.Equal("INVALID_RECEIPT", result.ErrorCode);
    }

    [Fact]
    public async Task VerifyOneTimePurchase_EscapesEveryUserControlledUrlSegment()
    {
        const string unsafeToken = "token/with?unsafe#characters";
        const string unsafeProduct = "premium/monthly";
        Uri? capturedUri = null;
        using var verifier = CreateVerifier(request =>
        {
            capturedUri = request.RequestUri;
            return JsonResponse(CreateOneTimePurchaseJson(
                AccountId,
                productId: unsafeProduct,
                purchaseToken: unsafeToken));
        });
        var request = CreateRequest();
        request.ProductId = unsafeProduct;
        request.ReceiptPayload = unsafeToken;
        request.IsSubscription = false;

        var result = await verifier.VerifyOneTimePurchaseAsync(request);

        Assert.True(result.IsValid);
        Assert.NotNull(capturedUri);
        Assert.Contains(Uri.EscapeDataString(unsafeProduct), capturedUri.AbsoluteUri);
        Assert.Contains(Uri.EscapeDataString(unsafeToken), capturedUri.AbsoluteUri);
        Assert.DoesNotContain(unsafeToken, capturedUri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyOneTimePurchase_CallerCancellation_Propagates()
    {
        using var verifier = CreateVerifier(
            async (_, ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                throw new InvalidOperationException("Unreachable");
            });
        var request = CreateRequest();
        request.IsSubscription = false;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => verifier.VerifyOneTimePurchaseAsync(request, cts.Token));
    }

    [Fact]
    public async Task QueryFailure_DoesNotLogRawPurchaseTokenOrRequestUri()
    {
        var logger = new CollectingLogger<GooglePlayStoreVerifier>();
        using var verifier = CreateVerifier(
            (_, _) => throw new HttpRequestException(
                $"GET https://androidpublisher.googleapis.com/tokens/{PurchaseToken} failed"),
            logger);

        var result = await verifier.QuerySubscriptionAsync(PurchaseToken);

        Assert.Equal(GooglePlaySubscriptionQueryFailure.Retryable, result.Failure);
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains(PurchaseToken, StringComparison.Ordinal));
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains("/tokens/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OneTimeQueryFailure_DoesNotLogRawPurchaseTokenOrRequestUri()
    {
        var logger = new CollectingLogger<GooglePlayStoreVerifier>();
        using var verifier = CreateVerifier(
            (_, _) => throw new HttpRequestException(
                $"GET https://androidpublisher.googleapis.com/tokens/{PurchaseToken} failed"),
            logger);
        var request = CreateRequest();
        request.IsSubscription = false;

        var result = await verifier.VerifyOneTimePurchaseAsync(request);

        Assert.True(result.IsRetryable);
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains(PurchaseToken, StringComparison.Ordinal));
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains("/tokens/", StringComparison.Ordinal));
    }

    [Fact]
    public void TelemetryProcessor_RedactsAndroidPublisherToken_FromNameAndData()
    {
        var sink = new CapturingTelemetryProcessor();
        var processor = new GooglePlayPurchaseTokenTelemetryProcessor(sink);
        var telemetry = new DependencyTelemetry
        {
            Name = $"GET /androidpublisher/v3/applications/app/purchases/subscriptionsv2/tokens/{PurchaseToken}",
            Data = $"https://androidpublisher.googleapis.com/androidpublisher/v3/applications/app/purchases/products/sku/tokens/{PurchaseToken}?alt=json"
        };

        processor.Process(telemetry);

        Assert.Same(telemetry, sink.Item);
        Assert.DoesNotContain(PurchaseToken, telemetry.Name, StringComparison.Ordinal);
        Assert.DoesNotContain(PurchaseToken, telemetry.Data, StringComparison.Ordinal);
        Assert.Contains("/tokens/[REDACTED]", telemetry.Name, StringComparison.Ordinal);
        Assert.Contains("/tokens/[REDACTED]?alt=json", telemetry.Data, StringComparison.Ordinal);
    }

    private static VerifyRequest CreateRequest() => new()
    {
        ProductId = ProductId,
        TransactionId = "client-transaction-id",
        ReceiptPayload = PurchaseToken,
        IsSubscription = true,
        ExpectedObfuscatedAccountId = AccountId
    };

    private static GooglePlayStoreVerifier CreateVerifier(
        Func<HttpRequestMessage, HttpResponseMessage> googleResponse,
        ILogger<GooglePlayStoreVerifier>? logger = null) =>
        CreateVerifier((request, _) => Task.FromResult(googleResponse(request)), logger);

    private static GooglePlayStoreVerifier CreateVerifier(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> googleResponse,
        ILogger<GooglePlayStoreVerifier>? logger = null)
    {
        var handler = new ScriptedHandler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.Host == "oauth2.googleapis.com")
            {
                return JsonResponse("{\"access_token\":\"access-token\",\"expires_in\":3600}");
            }

            return await googleResponse(request, ct);
        });

        return new GooglePlayStoreVerifier(
            new GoogleVerifierConfig
            {
                PackageName = "com.serhat.forge.tests",
                ServiceAccountEmail = "service-account@example.test",
                PrivateKeyBase64 = PrivateKeyBase64,
                RequireObfuscatedAccountId = true
            },
            logger ?? new CollectingLogger<GooglePlayStoreVerifier>(),
            new HttpClient(handler));
    }

    private static string CreateSubscriptionJson(
        string state,
        string productId = ProductId,
        DateTimeOffset? expiryTime = null)
    {
        var expiry = expiryTime ?? DateTimeOffset.UtcNow.AddDays(30);
        return JsonSerializer.Serialize(new
        {
            subscriptionState = state,
            startTime = DateTimeOffset.UtcNow.AddDays(-1).ToString("O"),
            linkedPurchaseToken = "linked-sensitive-token",
            testPurchase = new { },
            externalAccountIdentifiers = new
            {
                externalAccountId = "legacy-account",
                obfuscatedExternalAccountId = AccountId,
                obfuscatedExternalProfileId = "profile-binding"
            },
            lineItems = new[] { CreateLineItem(productId, expiry) }
        });
    }

    private static string CreateOneTimePurchaseJson(
        string? obfuscatedAccountId,
        int quantity = 1,
        int? refundableQuantity = null,
        string productId = ProductId,
        string purchaseToken = PurchaseToken) =>
        JsonSerializer.Serialize(new
        {
            purchaseState = 0,
            orderId = "GPA.1111-2222-3333-44444",
            purchaseTimeMillis = DateTimeOffset.UtcNow.AddMinutes(-1)
                .ToUnixTimeMilliseconds()
                .ToString(),
            purchaseType = 0,
            productId,
            purchaseToken,
            quantity,
            refundableQuantity = refundableQuantity ?? quantity,
            obfuscatedExternalAccountId = obfuscatedAccountId
        });

    private static object CreateLineItem(string productId, DateTimeOffset expiryTime) => new
    {
        productId,
        expiryTime = expiryTime.ToString("O"),
        latestSuccessfulOrderId = "GPA.1234-5678-9012-34567",
        autoRenewingPlan = new { autoRenewEnabled = true }
    };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static string CreatePrivateKey()
    {
        using var rsa = RSA.Create(2048);
        return Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public ScriptedHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private sealed class CapturingTelemetryProcessor : ITelemetryProcessor
    {
        public ITelemetry? Item { get; private set; }

        public void Process(ITelemetry item)
        {
            Item = item;
        }
    }
}
