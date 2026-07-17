using Microsoft.Extensions.Logging;
using Moq;
using Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;
using Serhat.Forge.CloudScript.Framework.Monetization.Persistence;
using Serhat.Forge.CloudScript.Framework.Monetization.Services;
using Xunit;

namespace Serhat.Forge.CloudScript.Tests.Monetization;

public sealed class AppleRefundReconciliationTests
{
    private static readonly DateTime NowUtc =
        new(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryPurchaseRepository _repository = new();
    private readonly Mock<IEntitlementGranter> _granter = new();
    private readonly PurchaseRefundReconciliationService _service;

    public AppleRefundReconciliationTests()
    {
        _granter
            .Setup(value => value.RevokeItemsAsync(
                It.IsAny<RevokeRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((RevokeRequest request, CancellationToken _) =>
                GrantResult.Success(new List<string>(request.ItemIds)));

        var products = new ProductAllowlistConfig
        {
            Products = new Dictionary<string, ProductConfig>
            {
                ["premium_monthly"] = new()
                {
                    ProductId = "premium_monthly",
                    Type = ProductType.Subscription,
                    EconomyItemIds = ["premium_access"],
                    TierKey = "premium",
                    Quantity = 1,
                    Enabled = true
                }
            }
        };
        var lifecycle = new SubscriptionLifecycleService(
            _repository,
            _granter.Object,
            products,
            Mock.Of<ILogger<SubscriptionLifecycleService>>());
        _service = new PurchaseRefundReconciliationService(
            _repository,
            _granter.Object,
            lifecycle,
            Mock.Of<ILogger<PurchaseRefundReconciliationService>>());
    }

    [Fact]
    public async Task FullOneTimeRefund_RevokesRecordedGrantAndMarksPurchaseRefunded()
    {
        var purchase = CreatePurchase(
            "apple:one-time-transaction",
            ProductType.NonConsumable,
            "remove_ads",
            "remove_ads_entitlement");
        Assert.True(await _repository.CreatePurchaseAsync(purchase));

        var result = await _service.ProcessAsync(new PurchaseRefundReconciliationRequest
        {
            EventId = "apple-refund-one-time",
            TransactionKey = purchase.TransactionKey,
            Platform = Platform.Apple,
            ProductIdHint = purchase.ProductId,
            IsFullRefund = true,
            EventTimestampUtc = NowUtc,
            ReceivedAtUtc = NowUtc
        });

        Assert.True(result.IsSuccess);
        var stored = await _repository.GetPurchaseAsync(purchase.TransactionKey);
        Assert.NotNull(stored);
        Assert.Equal(PurchaseStatus.Refunded, stored.Status);
        _granter.Verify(value => value.RevokeItemsAsync(
            It.Is<RevokeRequest>(request =>
                request.PlayerId == "player-1" &&
                request.ItemIds.SequenceEqual(new[] { "remove_ads_entitlement" })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProratedOneTimeRefund_FailsClosedForManualReconciliation()
    {
        var purchase = CreatePurchase(
            "apple:partial-transaction",
            ProductType.Consumable,
            "coins_100",
            "currency_coins");
        Assert.True(await _repository.CreatePurchaseAsync(purchase));

        var result = await _service.ProcessAsync(new PurchaseRefundReconciliationRequest
        {
            EventId = "apple-refund-partial",
            TransactionKey = purchase.TransactionKey,
            Platform = Platform.Apple,
            ProductIdHint = purchase.ProductId,
            IsFullRefund = false,
            EventTimestampUtc = NowUtc,
            ReceivedAtUtc = NowUtc
        });

        Assert.False(result.IsSuccess);
        Assert.True(result.IsRetryable);
        Assert.Equal("PARTIAL_REFUND_REQUIRES_RECONCILIATION", result.ErrorCode);
        _granter.Verify(value => value.RevokeItemsAsync(
            It.IsAny<RevokeRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubscriptionRenewalRefund_ResolvesOriginalPurchaseAndRevokesExactlyOnce()
    {
        const string originalTransactionId = "original-transaction";
        var subscriptionKey = SubscriptionRecord.CreateAppleKey(originalTransactionId);
        var purchase = CreatePurchase(
            PurchaseRecord.CreateTransactionKey(Platform.Apple, originalTransactionId),
            ProductType.Subscription,
            "premium_monthly",
            "premium_access");
        purchase.OriginalTransactionId = originalTransactionId;
        Assert.True(await _repository.CreatePurchaseAsync(purchase));

        var subscription = new SubscriptionRecord
        {
            SubscriptionKey = subscriptionKey,
            Platform = Platform.Apple,
            PlayerId = purchase.PlayerId,
            ProductId = purchase.ProductId,
            TierKey = "premium",
            Status = SubscriptionStatus.Active,
            AutoRenew = true,
            PeriodStartUtc = NowUtc.AddDays(-15),
            PeriodEndUtc = NowUtc.AddDays(15),
            LastEventAtUtc = NowUtc.AddDays(-15),
            CreatedAtUtc = NowUtc.AddDays(-15),
            UpdatedAtUtc = NowUtc.AddDays(-15)
        };
        subscription.SetActiveEconomyItemIds(["premium_access"]);
        Assert.True(await _repository.CreateSubscriptionAsync(subscription));

        var request = new PurchaseRefundReconciliationRequest
        {
            EventId = "apple-refund-renewal",
            TransactionKey = PurchaseRecord.CreateTransactionKey(
                Platform.Apple,
                "renewal-not-submitted-by-client"),
            Platform = Platform.Apple,
            ProductIdHint = "premium_monthly",
            SubscriptionKey = subscriptionKey,
            IsFullRefund = true,
            LifecycleEventType = WebhookEventType.Refunded,
            EventTimestampUtc = NowUtc,
            ReceivedAtUtc = NowUtc
        };

        var first = await _service.ProcessAsync(request);
        var replay = await _service.ProcessAsync(request with { EventId = "apple-refund-renewal-retry" });

        Assert.True(first.IsSuccess);
        Assert.True(replay.IsSuccess);
        var storedPurchase = await _repository.GetPurchaseAsync(purchase.TransactionKey);
        var storedSubscription = await _repository.GetSubscriptionAsync(subscriptionKey);
        Assert.NotNull(storedPurchase);
        Assert.NotNull(storedSubscription);
        Assert.Equal(PurchaseStatus.Refunded, storedPurchase.Status);
        Assert.Equal(SubscriptionStatus.Refunded, storedSubscription.Status);
        Assert.Empty(storedSubscription.ActiveEconomyItemIds);
        _granter.Verify(value => value.RevokeItemsAsync(
            It.IsAny<RevokeRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static PurchaseRecord CreatePurchase(
        string transactionKey,
        ProductType productType,
        string productId,
        string economyItemId) =>
        new()
        {
            TransactionKey = transactionKey,
            Platform = Platform.Apple,
            ProductId = productId,
            ProductType = productType,
            PlayerId = "player-1",
            Status = PurchaseStatus.Granted,
            StoreTransactionId = transactionKey["apple:".Length..],
            GrantedEconomyItemIds = [economyItemId],
            QuantityGranted = 1,
            HasGrantPayloadSnapshot = true,
            GrantEconomyItemIds = [economyItemId],
            GrantQuantities = [1],
            CreatedAtUtc = NowUtc.AddDays(-1),
            UpdatedAtUtc = NowUtc.AddDays(-1)
        };
}
