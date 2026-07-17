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
                    EconomyItemIds = new List<string>
                    {
                        "subscription_premium",
                        "subscriber_badge"
                    },
                    TierKey = "premium",
                    TierPrecedence = 1,
                    Enabled = true
                },
                ["pro_monthly"] = new ProductConfig
                {
                    ProductId = "pro_monthly",
                    Type = ProductType.Subscription,
                    EconomyItemIds = new List<string>
                    {
                        "subscription_pro",
                        "pro_badge"
                    },
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
        var existingSubscription = await CreateTestSubscription();
        existingSubscription.SetActiveEconomyItemIds(
            new[] { "subscription_premium", "subscriber_badge" });
        Assert.True(await _repository.UpdateSubscriptionAsync(existingSubscription));

        _granterMock
            .Setup(g => g.RevokeItemsAsync(It.IsAny<RevokeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrantResult.Success(new List<string> { "subscription_premium" }));

        _granterMock
            .Setup(g => g.GrantItemsAsync(It.IsAny<GrantRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrantResult.Success(
                new List<string> { "subscription_pro", "pro_badge" }));

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
        _granterMock.Verify(
            g => g.RevokeItemsAsync(
                It.Is<RevokeRequest>(request =>
                    request.ItemIds.Count == 2 &&
                    request.ItemIds.Contains("subscription_premium") &&
                    request.ItemIds.Contains("subscriber_badge")),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _granterMock.Verify(
            g => g.GrantItemsAsync(
                It.Is<GrantRequest>(request =>
                    request.ItemIds.Count == 2 &&
                    request.ItemIds.Contains("subscription_pro") &&
                    request.ItemIds.Contains("pro_badge")),
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(
            new[] { "subscription_pro", "pro_badge" },
            subscription.ActiveEconomyItemIds);
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

    [Fact]
    public async Task ProcessWebhookEventAsync_Expiration_RevokesEverySnapshottedItem()
    {
        var subscription = new SubscriptionRecord
        {
            SubscriptionKey = "apple:bundle_subscription",
            Platform = Platform.Apple,
            PlayerId = "player123",
            ProductId = "premium_monthly",
            TierKey = "premium",
            TierPrecedence = 1,
            Status = SubscriptionStatus.Active,
            AutoRenew = true,
            PeriodStartUtc = DateTime.UtcNow.AddDays(-30),
            PeriodEndUtc = DateTime.UtcNow.AddDays(1),
            LastEventAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-30),
            UpdatedAtUtc = DateTime.UtcNow
        };
        subscription.SetActiveEconomyItemIds(
            new[] { "subscription_premium", "subscriber_badge" });
        await _repository.CreateSubscriptionAsync(subscription);

        RevokeRequest? capturedRequest = null;
        _granterMock
            .Setup(g => g.RevokeItemsAsync(It.IsAny<RevokeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RevokeRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(GrantResult.Success(
                new List<string> { "subscription_premium", "subscriber_badge" }));

        var result = await _service.ProcessWebhookEventAsync(new WebhookEvent
        {
            EventId = "event_bundle_expired",
            EventType = WebhookEventType.Expired,
            Platform = Platform.Apple,
            SubscriptionKey = subscription.SubscriptionKey,
            EventTimestampUtc = subscription.LastEventAtUtc.AddMinutes(1),
            ReceivedAtUtc = DateTime.UtcNow
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedRequest);
        Assert.Equal(
            new[] { "subscription_premium", "subscriber_badge" },
            capturedRequest.ItemIds);

        var stored = await _repository.GetSubscriptionAsync(subscription.SubscriptionKey);
        Assert.NotNull(stored);
        Assert.Equal(SubscriptionStatus.Expired, stored.Status);
        Assert.Empty(stored.ActiveEconomyItemIds);
        Assert.Null(stored.ActiveEconomyItemId);
    }

    [Fact]
    public async Task ProcessWebhookEventAsync_RevokeFailure_DoesNotCommitAndCanRetry()
    {
        var subscription = await CreateTestSubscription();
        var idempotencyKeys = new List<string>();
        var attempt = 0;
        _granterMock
            .Setup(g => g.RevokeItemsAsync(It.IsAny<RevokeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RevokeRequest request, CancellationToken _) =>
            {
                idempotencyKeys.Add(request.IdempotencyKey);
                attempt++;
                return attempt == 1
                    ? GrantResult.Failure("TEMPORARY", "Provider unavailable")
                    : GrantResult.Success(new List<string>(request.ItemIds));
            });

        var webhookEvent = new WebhookEvent
        {
            EventId = "event_retryable_expiration",
            EventType = WebhookEventType.Expired,
            Platform = Platform.Apple,
            SubscriptionKey = subscription.SubscriptionKey,
            EventTimestampUtc = subscription.LastEventAtUtc.AddMinutes(1),
            ReceivedAtUtc = DateTime.UtcNow
        };

        var firstResult = await _service.ProcessWebhookEventAsync(webhookEvent);

        Assert.False(firstResult.IsSuccess);
        Assert.True(firstResult.IsRetryable);
        Assert.Equal("ENTITLEMENT_REVOKE_FAILED", firstResult.ErrorCode);
        Assert.False(await _repository.HasProcessedWebhookAsync(webhookEvent.EventId));
        var afterFailure = await _repository.GetSubscriptionAsync(subscription.SubscriptionKey);
        Assert.NotNull(afterFailure);
        Assert.Equal(SubscriptionStatus.Active, afterFailure.Status);
        Assert.Single(afterFailure.ActiveEconomyItemIds);

        var retryResult = await _service.ProcessWebhookEventAsync(webhookEvent);

        Assert.True(retryResult.IsSuccess);
        Assert.True(await _repository.HasProcessedWebhookAsync(webhookEvent.EventId));
        var afterRetry = await _repository.GetSubscriptionAsync(subscription.SubscriptionKey);
        Assert.NotNull(afterRetry);
        Assert.Equal(SubscriptionStatus.Expired, afterRetry.Status);
        Assert.Empty(afterRetry.ActiveEconomyItemIds);
        Assert.Equal(2, idempotencyKeys.Count);
        Assert.Equal(idempotencyKeys[0], idempotencyKeys[1]);
        Assert.False(idempotencyKeys[0].Contains(subscription.SubscriptionKey, StringComparison.Ordinal));
        Assert.False(idempotencyKeys[0].Contains(webhookEvent.EventId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessWebhookEventAsync_OlderEvent_DoesNotOverwriteNewerTerminalState()
    {
        var terminalEventAtUtc = new DateTime(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);
        var subscription = new SubscriptionRecord
        {
            SubscriptionKey = "apple:terminal_subscription",
            Platform = Platform.Apple,
            PlayerId = "player123",
            ProductId = "premium_monthly",
            TierKey = "premium",
            TierPrecedence = 1,
            Status = SubscriptionStatus.Chargeback,
            AutoRenew = false,
            PeriodStartUtc = terminalEventAtUtc.AddMonths(-1),
            PeriodEndUtc = terminalEventAtUtc,
            OriginalPurchaseDateUtc = terminalEventAtUtc.AddMonths(-6),
            LastEventAtUtc = terminalEventAtUtc,
            CreatedAtUtc = terminalEventAtUtc.AddMonths(-6),
            UpdatedAtUtc = terminalEventAtUtc
        };
        await _repository.CreateSubscriptionAsync(subscription);

        var result = await _service.ProcessWebhookEventAsync(new WebhookEvent
        {
            EventId = "event_stale_renewal",
            EventType = WebhookEventType.Renewed,
            Platform = Platform.Apple,
            SubscriptionKey = subscription.SubscriptionKey,
            EventTimestampUtc = terminalEventAtUtc.AddMinutes(-1),
            PeriodStartUtc = terminalEventAtUtc.AddDays(-30),
            PeriodEndUtc = terminalEventAtUtc.AddDays(30),
            ReceivedAtUtc = terminalEventAtUtc.AddMinutes(1)
        });

        Assert.True(result.IsSuccess);
        var stored = await _repository.GetSubscriptionAsync(subscription.SubscriptionKey);
        Assert.NotNull(stored);
        Assert.Equal(SubscriptionStatus.Chargeback, stored.Status);
        Assert.False(stored.AutoRenew);
        Assert.Equal(terminalEventAtUtc, stored.LastEventAtUtc);
        _granterMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessWebhookEventAsync_ResumeGrantFailure_PreservesPausedStateAndRetries()
    {
        var subscription = await CreateTestSubscription(status: SubscriptionStatus.Paused);
        subscription.SetActiveEconomyItemIds(Array.Empty<string>());
        Assert.True(await _repository.UpdateSubscriptionAsync(subscription));

        var idempotencyKeys = new List<string>();
        var attempt = 0;
        _granterMock
            .Setup(g => g.GrantItemsAsync(It.IsAny<GrantRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GrantRequest request, CancellationToken _) =>
            {
                idempotencyKeys.Add(request.IdempotencyKey);
                attempt++;
                return attempt == 1
                    ? GrantResult.Failure("TEMPORARY", "Provider unavailable")
                    : GrantResult.Success(new List<string>(request.ItemIds));
            });

        var webhookEvent = new WebhookEvent
        {
            EventId = "event_retryable_resume",
            EventType = WebhookEventType.Resumed,
            Platform = Platform.Apple,
            SubscriptionKey = subscription.SubscriptionKey,
            EventTimestampUtc = subscription.LastEventAtUtc.AddMinutes(1),
            PeriodStartUtc = subscription.PeriodEndUtc,
            PeriodEndUtc = subscription.PeriodEndUtc.AddMonths(1),
            ReceivedAtUtc = DateTime.UtcNow
        };

        var firstResult = await _service.ProcessWebhookEventAsync(webhookEvent);

        Assert.False(firstResult.IsSuccess);
        Assert.True(firstResult.IsRetryable);
        var afterFailure = await _repository.GetSubscriptionAsync(subscription.SubscriptionKey);
        Assert.NotNull(afterFailure);
        Assert.Equal(SubscriptionStatus.Paused, afterFailure.Status);
        Assert.Empty(afterFailure.ActiveEconomyItemIds);

        var retryResult = await _service.ProcessWebhookEventAsync(webhookEvent);

        Assert.True(retryResult.IsSuccess);
        var afterRetry = await _repository.GetSubscriptionAsync(subscription.SubscriptionKey);
        Assert.NotNull(afterRetry);
        Assert.Equal(SubscriptionStatus.Active, afterRetry.Status);
        Assert.Equal(
            new[] { "subscription_premium", "subscriber_badge" },
            afterRetry.ActiveEconomyItemIds);
        Assert.Equal(2, idempotencyKeys.Count);
        Assert.Equal(idempotencyKeys[0], idempotencyKeys[1]);
    }

    [Fact]
    public async Task ProcessWebhookEventAsync_RepositoryRejectsUpdate_ReturnsRetryableAndAbandonsClaim()
    {
        var repositoryMock = new Mock<IPurchaseRepository>();
        var subscription = new SubscriptionRecord
        {
            SubscriptionKey = "apple:repository_failure",
            Platform = Platform.Apple,
            PlayerId = "player123",
            ProductId = "premium_monthly",
            TierKey = "premium",
            TierPrecedence = 1,
            Status = SubscriptionStatus.Active,
            AutoRenew = true,
            PeriodEndUtc = DateTime.UtcNow.AddDays(1),
            LastEventAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-30),
            UpdatedAtUtc = DateTime.UtcNow
        };
        repositoryMock
            .Setup(r => r.TryBeginWebhookProcessingAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repositoryMock
            .Setup(r => r.GetSubscriptionAsync(
                subscription.SubscriptionKey,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription.Copy());
        repositoryMock
            .Setup(r => r.TryUpdateSubscriptionIfNotNewerAsync(
                It.IsAny<SubscriptionRecord>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repositoryMock
            .Setup(r => r.AbandonWebhookProcessingAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new SubscriptionLifecycleService(
            repositoryMock.Object,
            _granterMock.Object,
            _productConfig,
            _loggerMock.Object);
        var result = await service.ProcessWebhookEventAsync(new WebhookEvent
        {
            EventId = "event_repository_failure",
            EventType = WebhookEventType.Cancelled,
            Platform = Platform.Apple,
            SubscriptionKey = subscription.SubscriptionKey,
            EventTimestampUtc = subscription.LastEventAtUtc.AddMinutes(1),
            ReceivedAtUtc = DateTime.UtcNow
        });

        Assert.False(result.IsSuccess);
        Assert.True(result.IsRetryable);
        Assert.Equal("SUBSCRIPTION_UPDATE_FAILED", result.ErrorCode);
        repositoryMock.Verify(
            r => r.AbandonWebhookProcessingAsync(
                "event_repository_failure",
                It.IsAny<CancellationToken>()),
            Times.Once);
        repositoryMock.Verify(
            r => r.CompleteWebhookProcessingAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void SubscriptionEntity_RoundTrip_PreservesImmutableMultiItemSnapshot()
    {
        var mutableInput = new List<string>
        {
            "subscription_premium",
            "subscriber_badge"
        };
        var subscription = new SubscriptionRecord
        {
            SubscriptionKey = "apple:entity_round_trip",
            Platform = Platform.Apple,
            PlayerId = "player123",
            ProductId = "premium_monthly",
            TierKey = "premium",
            Status = SubscriptionStatus.Active,
            LatestStoreOrderId = "GPA.audit-order-id",
            IsSandbox = true
        };
        subscription.SetActiveEconomyItemIds(mutableInput);
        mutableInput.Clear();

        var entity = SubscriptionEntity.FromRecord(subscription, "title");
        var roundTripped = entity.ToRecord();

        Assert.Equal(
            new[] { "subscription_premium", "subscriber_badge" },
            subscription.ActiveEconomyItemIds);
        Assert.Equal(
            new[] { "subscription_premium", "subscriber_badge" },
            roundTripped.ActiveEconomyItemIds);
        Assert.Equal("GPA.audit-order-id", roundTripped.LatestStoreOrderId);
        Assert.True(roundTripped.IsSandbox);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<string>)roundTripped.ActiveEconomyItemIds).Add("unexpected"));

        var legacyEntity = new SubscriptionEntity
        {
            Status = SubscriptionStatus.Active.ToString(),
            ActiveEconomyItemId = "legacy_subscription_item",
            ActiveEconomyItemIdsJson = null
        };
        var legacyRecord = legacyEntity.ToRecord();
        Assert.Equal(
            new[] { "legacy_subscription_item" },
            legacyRecord.ActiveEconomyItemIds);
    }
}
