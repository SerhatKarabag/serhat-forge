using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;
using Serhat.Forge.CloudScript.Framework.Monetization.Persistence;
using Serhat.Forge.CloudScript.Framework.Monetization.Services;
using Xunit;

namespace Serhat.Forge.CloudScript.Tests.Monetization;

public class SubscriptionLifecycleServiceTests
{
    private readonly Mock<ILogger<SubscriptionLifecycleService>> _loggerMock;
    private readonly InMemoryPurchaseRepository _repository;
    private readonly Mock<IEntitlementGranter> _granterMock;
    private readonly ProductAllowlistConfig _productConfig;
    private readonly SubscriptionLifecycleService _service;

    public SubscriptionLifecycleServiceTests()
    {
        _loggerMock = new Mock<ILogger<SubscriptionLifecycleService>>();
        _repository = new InMemoryPurchaseRepository();
        _granterMock = new Mock<IEntitlementGranter>();

        _productConfig = new ProductAllowlistConfig
        {
            Products = new Dictionary<string, ProductConfig>
            {
                ["premium_monthly"] = new ProductConfig
                {
                    ProductId = "premium_monthly",
                    Type = ProductType.Subscription,
                    EconomyItemIds = new List<string> { "subscription_premium" },
                    TierKey = "premium",
                    TierPrecedence = 1,
                    Enabled = true
                },
                ["pro_monthly"] = new ProductConfig
                {
                    ProductId = "pro_monthly",
                    Type = ProductType.Subscription,
                    EconomyItemIds = new List<string> { "subscription_pro" },
                    TierKey = "pro",
                    TierPrecedence = 2,
                    Enabled = true
                }
            }
        };

        _service = new SubscriptionLifecycleService(
            _repository,
            _granterMock.Object,
            _productConfig,
            _loggerMock.Object);
    }

    private async Task<SubscriptionRecord> CreateTestSubscription(
        string subscriptionKey = "apple:orig_txn_001",
        SubscriptionStatus status = SubscriptionStatus.Active)
    {
        var subscription = new SubscriptionRecord
        {
            SubscriptionKey = subscriptionKey,
            Platform = Platform.Apple,
            PlayerId = "player123",
            ProductId = "premium_monthly",
            TierKey = "premium",
            TierPrecedence = 1,
            Status = status,
            ActiveEconomyItemId = "subscription_premium",
            AutoRenew = true,
            PeriodStartUtc = DateTime.UtcNow.AddDays(-30),
            PeriodEndUtc = DateTime.UtcNow.AddDays(1),
            OriginalPurchaseDateUtc = DateTime.UtcNow.AddDays(-30),
            LastEventAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-30),
            UpdatedAtUtc = DateTime.UtcNow
        };

        await _repository.CreateSubscriptionAsync(subscription);
        return subscription;
    }

    [Fact]
    public async Task ProcessWebhookEventAsync_Renewal_UpdatesSubscription()
    {
        // Arrange
        await CreateTestSubscription();

        var webhookEvent = new WebhookEvent
        {
            EventId = "event_001",
            EventType = WebhookEventType.Renewed,
            Platform = Platform.Apple,
            SubscriptionKey = "apple:orig_txn_001",
            ProductId = "premium_monthly",
            PeriodStartUtc = DateTime.UtcNow,
            PeriodEndUtc = DateTime.UtcNow.AddDays(30),
            ReceivedAtUtc = DateTime.UtcNow
        };

        // Act
        var result = await _service.ProcessWebhookEventAsync(webhookEvent);

        // Assert
        Assert.True(result.IsSuccess);

        var subscription = await _repository.GetSubscriptionAsync("apple:orig_txn_001");
        Assert.NotNull(subscription);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.True(subscription.AutoRenew);
    }

    [Fact]
    public async Task ProcessWebhookEventAsync_Cancellation_SetsStatusToCancelled()
    {
        // Arrange
        await CreateTestSubscription();

        var webhookEvent = new WebhookEvent
        {
            EventId = "event_002",
            EventType = WebhookEventType.Cancelled,
            Platform = Platform.Apple,
            SubscriptionKey = "apple:orig_txn_001",
            ProductId = "premium_monthly",
            ReceivedAtUtc = DateTime.UtcNow
        };

        // Act
        var result = await _service.ProcessWebhookEventAsync(webhookEvent);

        // Assert
        Assert.True(result.IsSuccess);

        var subscription = await _repository.GetSubscriptionAsync("apple:orig_txn_001");
        Assert.NotNull(subscription);
        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
        Assert.False(subscription.AutoRenew);

        // Verify entitlements were NOT revoked (still in period)
        _granterMock.Verify(
            g => g.RevokeItemsAsync(It.IsAny<RevokeRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessWebhookEventAsync_Expiration_RevokesEntitlements()
    {
        // Arrange
        await CreateTestSubscription();

        _granterMock
            .Setup(g => g.RevokeItemsAsync(It.IsAny<RevokeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrantResult.Success(new List<string> { "subscription_premium" }));

        var webhookEvent = new WebhookEvent
        {
            EventId = "event_003",
            EventType = WebhookEventType.Expired,
            Platform = Platform.Apple,
            SubscriptionKey = "apple:orig_txn_001",
            ProductId = "premium_monthly",
            ReceivedAtUtc = DateTime.UtcNow
        };

        // Act
        var result = await _service.ProcessWebhookEventAsync(webhookEvent);

        // Assert
        Assert.True(result.IsSuccess);

        var subscription = await _repository.GetSubscriptionAsync("apple:orig_txn_001");
        Assert.NotNull(subscription);
        Assert.Equal(SubscriptionStatus.Expired, subscription.Status);

        // Verify entitlements were revoked
        _granterMock.Verify(
            g => g.RevokeItemsAsync(
                It.Is<RevokeRequest>(r => r.ItemIds.Contains("subscription_premium")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessWebhookEventAsync_Refund_RevokesEntitlementsImmediately()
    {
        // Arrange
        await CreateTestSubscription();

        _granterMock
            .Setup(g => g.RevokeItemsAsync(It.IsAny<RevokeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrantResult.Success(new List<string> { "subscription_premium" }));

        var webhookEvent = new WebhookEvent
        {
            EventId = "event_004",
            EventType = WebhookEventType.Refunded,
            Platform = Platform.Apple,
            SubscriptionKey = "apple:orig_txn_001",
            ProductId = "premium_monthly",
            ReceivedAtUtc = DateTime.UtcNow
        };

        // Act
        var result = await _service.ProcessWebhookEventAsync(webhookEvent);

        // Assert
        Assert.True(result.IsSuccess);

        var subscription = await _repository.GetSubscriptionAsync("apple:orig_txn_001");
        Assert.NotNull(subscription);
        Assert.Equal(SubscriptionStatus.Refunded, subscription.Status);

        // Verify entitlements were revoked
        _granterMock.Verify(
            g => g.RevokeItemsAsync(It.IsAny<RevokeRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessWebhookEventAsync_GracePeriodStarted_KeepsEntitlements()
    {
        // Arrange
        await CreateTestSubscription();

        var webhookEvent = new WebhookEvent
        {
            EventId = "event_005",
            EventType = WebhookEventType.GracePeriodStarted,
            Platform = Platform.Apple,
            SubscriptionKey = "apple:orig_txn_001",
            ProductId = "premium_monthly",
            GracePeriodEndUtc = DateTime.UtcNow.AddDays(16),
            ReceivedAtUtc = DateTime.UtcNow
        };

        // Act
        var result = await _service.ProcessWebhookEventAsync(webhookEvent);

        // Assert
        Assert.True(result.IsSuccess);

        var subscription = await _repository.GetSubscriptionAsync("apple:orig_txn_001");
        Assert.NotNull(subscription);
        Assert.Equal(SubscriptionStatus.GracePeriod, subscription.Status);
        Assert.True(subscription.IsActive); // Still active during grace period

        // Verify entitlements were NOT revoked
        _granterMock.Verify(
            g => g.RevokeItemsAsync(It.IsAny<RevokeRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessWebhookEventAsync_DuplicateEvent_ReturnsDuplicate()
    {
        // Arrange
        await CreateTestSubscription();

        var webhookEvent = new WebhookEvent
        {
            EventId = "event_dup_001",
            EventType = WebhookEventType.Renewed,
            Platform = Platform.Apple,
            SubscriptionKey = "apple:orig_txn_001",
            ProductId = "premium_monthly",
            ReceivedAtUtc = DateTime.UtcNow
        };

        // First processing
        await _service.ProcessWebhookEventAsync(webhookEvent);

        // Act - Second processing with same event ID
        var result = await _service.ProcessWebhookEventAsync(webhookEvent);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.IsDuplicate);
    }

    [Fact]
    public async Task ProcessWebhookEventAsync_UnknownSubscription_DoesNotFail()
    {
        // Arrange
        var webhookEvent = new WebhookEvent
        {
            EventId = "event_unknown",
            EventType = WebhookEventType.Renewed,
            Platform = Platform.Apple,
            SubscriptionKey = "apple:unknown_subscription",
            ProductId = "premium_monthly",
            ReceivedAtUtc = DateTime.UtcNow
        };

        // Act
        var result = await _service.ProcessWebhookEventAsync(webhookEvent);

        // Assert - Should succeed (idempotent) even for unknown subscriptions
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ProcessWebhookEventAsync_TierUpgrade_AppliesImmediately()
    {
        // Arrange
        await CreateTestSubscription();

        _granterMock
            .Setup(g => g.RevokeItemsAsync(It.IsAny<RevokeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrantResult.Success(new List<string> { "subscription_premium" }));

        _granterMock
            .Setup(g => g.GrantItemsAsync(It.IsAny<GrantRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrantResult.Success(new List<string> { "subscription_pro" }));

        var webhookEvent = new WebhookEvent
        {
            EventId = "event_upgrade",
            EventType = WebhookEventType.UpgradeDowngrade,
            Platform = Platform.Apple,
            SubscriptionKey = "apple:orig_txn_001",
            ProductId = "premium_monthly",
            NewProductId = "pro_monthly",
            NewTierKey = "pro",
            ReceivedAtUtc = DateTime.UtcNow
        };

        // Act
        var result = await _service.ProcessWebhookEventAsync(webhookEvent);

        // Assert
        Assert.True(result.IsSuccess);

        var subscription = await _repository.GetSubscriptionAsync("apple:orig_txn_001");
        Assert.NotNull(subscription);
        Assert.Equal("pro", subscription.TierKey);
        Assert.Equal(2, subscription.TierPrecedence);

        // Verify old entitlement revoked and new granted
        _granterMock.Verify(g => g.RevokeItemsAsync(It.IsAny<RevokeRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _granterMock.Verify(g => g.GrantItemsAsync(It.IsAny<GrantRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessWebhookEventAsync_TierDowngrade_SchedulesForNextRenewal()
    {
        // Arrange - Start with Pro subscription
        var subscription = new SubscriptionRecord
        {
            SubscriptionKey = "apple:pro_sub_001",
            Platform = Platform.Apple,
            PlayerId = "player123",
            ProductId = "pro_monthly",
            TierKey = "pro",
            TierPrecedence = 2,
            Status = SubscriptionStatus.Active,
            ActiveEconomyItemId = "subscription_pro",
            AutoRenew = true,
            PeriodStartUtc = DateTime.UtcNow.AddDays(-30),
            PeriodEndUtc = DateTime.UtcNow.AddDays(1),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-30),
            UpdatedAtUtc = DateTime.UtcNow
        };
        await _repository.CreateSubscriptionAsync(subscription);

        var webhookEvent = new WebhookEvent
        {
            EventId = "event_downgrade",
            EventType = WebhookEventType.UpgradeDowngrade,
            Platform = Platform.Apple,
            SubscriptionKey = "apple:pro_sub_001",
            ProductId = "pro_monthly",
            NewProductId = "premium_monthly",
            NewTierKey = "premium",
            ReceivedAtUtc = DateTime.UtcNow
        };

        // Act
        var result = await _service.ProcessWebhookEventAsync(webhookEvent);

        // Assert
        Assert.True(result.IsSuccess);

        var updatedSub = await _repository.GetSubscriptionAsync("apple:pro_sub_001");
        Assert.NotNull(updatedSub);
        Assert.Equal("pro", updatedSub.TierKey); // Still Pro
        Assert.Equal("premium", updatedSub.PendingTierKey); // Pending downgrade
        Assert.Equal("premium_monthly", updatedSub.PendingProductId);

        // Verify no entitlement changes (downgrade is deferred)
        _granterMock.Verify(g => g.RevokeItemsAsync(It.IsAny<RevokeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _granterMock.Verify(g => g.GrantItemsAsync(It.IsAny<GrantRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
