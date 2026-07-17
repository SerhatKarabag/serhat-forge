using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;
using Serhat.Forge.CloudScript.Framework.Monetization.Persistence;
using Serhat.Forge.CloudScript.Framework.Monetization.Services;
using Serhat.Forge.CloudScript.Framework.Monetization.Verification;
using Serhat.Forge.CloudScript.Framework.Monetization.Webhooks;
using Xunit;

namespace Serhat.Forge.CloudScript.Tests.Monetization;

public sealed class GoogleRtdnReconciliationServiceTests
{
    private static readonly DateTime NowUtc =
        new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryPurchaseRepository _repository = new();
    private readonly Mock<IGooglePlaySubscriptionSnapshotProvider> _snapshotProvider = new();
    private readonly Mock<IEntitlementGranter> _granter = new();
    private readonly GoogleRtdnReconciliationService _service;

    public GoogleRtdnReconciliationServiceTests()
    {
        _granter
            .Setup(value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((GrantRequest request, CancellationToken _) =>
                GrantResult.Success(new List<string>(request.ItemIds)));
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
                    EconomyItemIds = new List<string> { "premium_access", "subscriber_badge" },
                    TierKey = "premium",
                    TierPrecedence = 1,
                    Enabled = true
                }
            }
        };
        var lifecycle = new SubscriptionLifecycleService(
            _repository,
            _granter.Object,
            products,
            Mock.Of<ILogger<SubscriptionLifecycleService>>());
        var refundService = new PurchaseRefundReconciliationService(
            _repository,
            _granter.Object,
            lifecycle,
            Mock.Of<ILogger<PurchaseRefundReconciliationService>>(),
            new FixedTimeProvider(NowUtc));
        _service = new GoogleRtdnReconciliationService(
            _snapshotProvider.Object,
            _repository,
            lifecycle,
            refundService,
            requireObfuscatedAccountId: true,
            Mock.Of<ILogger<GoogleRtdnReconciliationService>>(),
            new FixedTimeProvider(NowUtc));
    }

    [Fact]
    public async Task ActiveSnapshot_RenewsFromAuthoritativeState()
    {
        const string token = "active-token";
        await SeedSubscriptionAsync(token);
        SetupSnapshot(
            token,
            CreateSnapshot(GooglePlaySubscriptionState.Active) with
            {
                IsTestPurchase = true
            });

        var result = await _service.ProcessAsync(CreateSubscriptionHint(token, 9));

        Assert.True(result.IsSuccess);
        var stored = await _repository.GetSubscriptionAsync(
            SubscriptionRecord.CreateGoogleKey(token));
        Assert.NotNull(stored);
        Assert.Equal(SubscriptionStatus.Active, stored.Status);
        Assert.Equal(NowUtc.AddDays(30), stored.PeriodEndUtc);
        Assert.True(stored.AutoRenew);
        Assert.Equal("GPA.safe-order-id", stored.LatestStoreOrderId);
        Assert.True(stored.IsSandbox);
        _granter.Verify(
            value => value.RevokeItemsAsync(
                It.IsAny<RevokeRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task NotificationTypeFive_OnHoldSnapshot_RevokesInsteadOfStartingGrace()
    {
        const string token = "on-hold-token";
        await SeedSubscriptionAsync(token);
        SetupSnapshot(token, CreateSnapshot(GooglePlaySubscriptionState.OnHold));

        var result = await _service.ProcessAsync(CreateSubscriptionHint(token, 5));

        Assert.True(result.IsSuccess);
        var stored = await _repository.GetSubscriptionAsync(
            SubscriptionRecord.CreateGoogleKey(token));
        Assert.NotNull(stored);
        Assert.Equal(SubscriptionStatus.Paused, stored.Status);
        Assert.Empty(stored.ActiveEconomyItemIds);
        _granter.Verify(
            value => value.RevokeItemsAsync(
                It.Is<RevokeRequest>(request => request.ItemIds.Count == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task NotificationTypeNine_ActiveSnapshot_DoesNotExpire()
    {
        const string token = "deferred-token";
        await SeedSubscriptionAsync(token);
        SetupSnapshot(token, CreateSnapshot(GooglePlaySubscriptionState.Active));

        var result = await _service.ProcessAsync(CreateSubscriptionHint(token, 9));

        Assert.True(result.IsSuccess);
        var stored = await _repository.GetSubscriptionAsync(
            SubscriptionRecord.CreateGoogleKey(token));
        Assert.NotNull(stored);
        Assert.Equal(SubscriptionStatus.Active, stored.Status);
        Assert.NotEmpty(stored.ActiveEconomyItemIds);
    }

    [Fact]
    public async Task CanceledSnapshot_RetainsItemsUntilAuthoritativeExpiry()
    {
        const string token = "canceled-token";
        await SeedSubscriptionAsync(token);
        SetupSnapshot(
            token,
            CreateSnapshot(GooglePlaySubscriptionState.Canceled) with
            {
                AutoRenewEnabled = false
            });

        var result = await _service.ProcessAsync(CreateSubscriptionHint(token, 12));

        Assert.True(result.IsSuccess);
        var stored = await _repository.GetSubscriptionAsync(
            SubscriptionRecord.CreateGoogleKey(token));
        Assert.NotNull(stored);
        Assert.Equal(SubscriptionStatus.Cancelled, stored.Status);
        Assert.False(stored.AutoRenew);
        Assert.Equal(2, stored.ActiveEconomyItemIds.Count);
        _granter.Verify(
            value => value.RevokeItemsAsync(
                It.IsAny<RevokeRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExpiredSnapshot_RevokesAllItems()
    {
        const string token = "expired-token";
        await SeedSubscriptionAsync(token);
        SetupSnapshot(
            token,
            CreateSnapshot(GooglePlaySubscriptionState.Expired) with
            {
                ExpiryTimeUtc = NowUtc.AddMinutes(-1),
                AutoRenewEnabled = false
            });

        var result = await _service.ProcessAsync(CreateSubscriptionHint(token, 2));

        Assert.True(result.IsSuccess);
        var stored = await _repository.GetSubscriptionAsync(
            SubscriptionRecord.CreateGoogleKey(token));
        Assert.NotNull(stored);
        Assert.Equal(SubscriptionStatus.Expired, stored.Status);
        Assert.Empty(stored.ActiveEconomyItemIds);
    }

    [Fact]
    public async Task RetryableGoogleQuery_DoesNotMutateOrClaimEvent()
    {
        const string token = "query-retry-token";
        var seeded = await SeedSubscriptionAsync(token);
        _snapshotProvider
            .Setup(value => value.QuerySubscriptionAsync(
                token,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(GooglePlaySubscriptionQueryResult.Retryable(
                "STORE_ERROR",
                "temporarily unavailable"));
        var notification = CreateSubscriptionHint(token, 5);

        var result = await _service.ProcessAsync(notification);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsRetryable);
        Assert.False(await _repository.HasProcessedWebhookAsync(notification.EventId));
        var stored = await _repository.GetSubscriptionAsync(seeded.SubscriptionKey);
        Assert.NotNull(stored);
        Assert.Equal(SubscriptionStatus.Active, stored.Status);
    }

    [Fact]
    public async Task PermanentGoogleQueryFailure_IsAcknowledgableWithoutMutation()
    {
        const string token = "query-permanent-token";
        var seeded = await SeedSubscriptionAsync(token);
        _snapshotProvider
            .Setup(value => value.QuerySubscriptionAsync(
                token,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(GooglePlaySubscriptionQueryResult.Permanent(
                "INVALID_RECEIPT",
                "not found"));

        var result = await _service.ProcessAsync(CreateSubscriptionHint(token, 4));

        Assert.False(result.IsSuccess);
        Assert.False(result.IsRetryable);
        var stored = await _repository.GetSubscriptionAsync(seeded.SubscriptionKey);
        Assert.NotNull(stored);
        Assert.Equal(SubscriptionStatus.Active, stored.Status);
    }

    [Fact]
    public async Task LinkedToken_SameOwner_SupersedesOldActiveRecord()
    {
        const string oldToken = "linked-old-token";
        const string newToken = "linked-new-token";
        var oldSubscription = await SeedSubscriptionAsync(oldToken);
        var newSubscription = await SeedSubscriptionAsync(newToken);
        SetupSnapshot(
            newToken,
            CreateSnapshot(GooglePlaySubscriptionState.Active) with
            {
                LinkedPurchaseToken = oldToken
            });

        var result = await _service.ProcessAsync(CreateSubscriptionHint(newToken, 4));

        Assert.True(result.IsSuccess);
        var oldStored = await _repository.GetSubscriptionAsync(oldSubscription.SubscriptionKey);
        var newStored = await _repository.GetSubscriptionAsync(newSubscription.SubscriptionKey);
        Assert.NotNull(oldStored);
        Assert.NotNull(newStored);
        Assert.Equal(SubscriptionStatus.Expired, oldStored.Status);
        Assert.Empty(oldStored.ActiveEconomyItemIds);
        Assert.Equal(SubscriptionStatus.Active, newStored.Status);
    }

    [Fact]
    public async Task LinkedToken_DifferentOwner_FailsClosed()
    {
        const string oldToken = "cross-owner-old-token";
        const string newToken = "cross-owner-new-token";
        await SeedSubscriptionAsync(oldToken, playerId: "other-player");
        await SeedSubscriptionAsync(newToken);
        SetupSnapshot(
            newToken,
            CreateSnapshot(GooglePlaySubscriptionState.Active) with
            {
                LinkedPurchaseToken = oldToken
            });

        var result = await _service.ProcessAsync(CreateSubscriptionHint(newToken, 4));

        Assert.False(result.IsSuccess);
        Assert.True(result.IsRetryable);
        Assert.Equal("LINKED_SUBSCRIPTION_OWNER_CONFLICT", result.ErrorCode);
        _granter.Verify(
            value => value.RevokeItemsAsync(
                It.IsAny<RevokeRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task OneTimeHint_NeverQueriesOrGrants(int notificationType)
    {
        var notification = new GoogleRtdnNotification
        {
            EventId = $"one-time-{notificationType}",
            Kind = GoogleRtdnNotificationKind.OneTimeProductChanged,
            PurchaseToken = "one-time-token",
            ProductIdHint = "coins",
            NotificationType = notificationType,
            EventTimestampUtc = NowUtc,
            ReceivedAtUtc = NowUtc.AddMinutes(1)
        };

        var result = await _service.ProcessAsync(notification);

        Assert.True(result.IsSuccess);
        Assert.True(await _repository.HasProcessedWebhookAsync(notification.EventId));
        _snapshotProvider.VerifyNoOtherCalls();
        _granter.Verify(
            value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FullVoid_RevokesEveryRecordedItem_AndIsIdempotent()
    {
        const string token = "void-full-token";
        var purchase = await SeedPurchaseAsync(token);
        var notification = CreateVoidHint(token, refundType: 1);

        var first = await _service.ProcessAsync(notification);
        var replay = await _service.ProcessAsync(notification);

        Assert.True(first.IsSuccess);
        Assert.True(replay.IsDuplicate);
        var stored = await _repository.GetPurchaseAsync(purchase.TransactionKey);
        Assert.NotNull(stored);
        Assert.Equal(PurchaseStatus.Refunded, stored.Status);
        _granter.Verify(
            value => value.RevokeItemsAsync(
                It.Is<RevokeRequest>(request =>
                    request.ItemIds.Count == 2 &&
                    request.ItemIds.Contains("skin") &&
                    request.ItemIds.Contains("badge")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FullVoid_RevokeFailure_AbandonsClaimAndRetriesIdempotently()
    {
        const string token = "void-retry-token";
        var purchase = await SeedPurchaseAsync(token);
        var idempotencyKeys = new List<string>();
        var attempt = 0;
        _granter
            .Setup(value => value.RevokeItemsAsync(
                It.IsAny<RevokeRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((RevokeRequest request, CancellationToken _) =>
            {
                idempotencyKeys.Add(request.IdempotencyKey);
                attempt++;
                return attempt == 1
                    ? GrantResult.Failure("TEMPORARY", "unavailable")
                    : GrantResult.Success(new List<string>(request.ItemIds));
            });
        var notification = CreateVoidHint(token, refundType: 1);

        var first = await _service.ProcessAsync(notification);
        var afterFailure = await _repository.GetPurchaseAsync(purchase.TransactionKey);
        var processedAfterFailure = await _repository.HasProcessedWebhookAsync(
            notification.EventId);
        var retry = await _service.ProcessAsync(notification);

        Assert.False(first.IsSuccess);
        Assert.True(first.IsRetryable);
        Assert.NotNull(afterFailure);
        Assert.Equal(PurchaseStatus.Granted, afterFailure.Status);
        Assert.False(processedAfterFailure);
        Assert.True(retry.IsSuccess);
        Assert.Equal(2, idempotencyKeys.Count);
        Assert.Equal(idempotencyKeys[0], idempotencyKeys[1]);
    }

    [Fact]
    public async Task ConcurrentDifferentVoidEvents_UseOneTransactionScopedRevokeIdentity()
    {
        const string token = "void-concurrent-token";
        await SeedPurchaseAsync(token);
        var idempotencyKeys = new ConcurrentBag<string>();
        var bothEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredCount = 0;
        _granter
            .Setup(value => value.RevokeItemsAsync(
                It.IsAny<RevokeRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (RevokeRequest request, CancellationToken _) =>
            {
                idempotencyKeys.Add(request.IdempotencyKey);
                if (Interlocked.Increment(ref enteredCount) == 2)
                {
                    bothEntered.TrySetResult(true);
                }

                await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                return GrantResult.Success(new List<string>(request.ItemIds));
            });

        var results = await Task.WhenAll(
            _service.ProcessAsync(CreateVoidHint(token, 1, "void-concurrent-a")),
            _service.ProcessAsync(CreateVoidHint(token, 1, "void-concurrent-b")));

        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(2, idempotencyKeys.Count);
        Assert.Single(idempotencyKeys.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ConcurrentSubscriptionRefundEvents_RevokeBenefitsExactlyOnce()
    {
        const string token = "subscription-void-concurrent-token";
        var subscription = await SeedSubscriptionAsync(token);
        var purchase = await SeedSubscriptionPurchaseAsync(token);
        var revokeStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRevoke = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _granter
            .Setup(value => value.RevokeItemsAsync(
                It.IsAny<RevokeRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (RevokeRequest request, CancellationToken _) =>
            {
                revokeStarted.TrySetResult(true);
                await releaseRevoke.Task.WaitAsync(TimeSpan.FromSeconds(5));
                return GrantResult.Success(new List<string>(request.ItemIds));
            });

        var firstTask = _service.ProcessAsync(CreateVoidHint(
            token,
            1,
            "subscription-void-a",
            "premium_monthly"));
        await revokeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = await _service.ProcessAsync(CreateVoidHint(
            token,
            1,
            "subscription-void-b",
            "premium_monthly"));
        releaseRevoke.TrySetResult(true);
        var first = await firstTask;

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        _granter.Verify(
            value => value.RevokeItemsAsync(
                It.IsAny<RevokeRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        var storedSubscription = await _repository.GetSubscriptionAsync(
            subscription.SubscriptionKey);
        var storedPurchase = await _repository.GetPurchaseAsync(purchase.TransactionKey);
        Assert.NotNull(storedSubscription);
        Assert.NotNull(storedPurchase);
        Assert.Equal(SubscriptionStatus.Refunded, storedSubscription.Status);
        Assert.Empty(storedSubscription.ActiveEconomyItemIds);
        Assert.Equal(PurchaseStatus.Refunded, storedPurchase.Status);
    }

    [Fact]
    public async Task PartialVoid_FailsClosedWithoutRevoking()
    {
        const string token = "void-partial-token";
        var purchase = await SeedPurchaseAsync(token);

        var result = await _service.ProcessAsync(CreateVoidHint(token, refundType: 2));

        Assert.False(result.IsSuccess);
        Assert.True(result.IsRetryable);
        Assert.Equal("PARTIAL_REFUND_REQUIRES_RECONCILIATION", result.ErrorCode);
        var stored = await _repository.GetPurchaseAsync(purchase.TransactionKey);
        Assert.NotNull(stored);
        Assert.Equal(PurchaseStatus.Granted, stored.Status);
        _granter.Verify(
            value => value.RevokeItemsAsync(
                It.IsAny<RevokeRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task NonUnitFullVoid_FailsClosedWithoutUnderRevoking()
    {
        const string token = "void-quantity-token";
        var purchase = await SeedPurchaseAsync(token);
        purchase.GrantQuantities = new List<int> { 100, 1 };
        purchase.QuantityGranted = 100;
        Assert.True(await _repository.UpdatePurchaseAsync(purchase));

        var result = await _service.ProcessAsync(CreateVoidHint(token, refundType: 1));

        Assert.False(result.IsSuccess);
        Assert.True(result.IsRetryable);
        Assert.Equal("REFUND_QUANTITY_REQUIRES_RECONCILIATION", result.ErrorCode);
        _granter.Verify(
            value => value.RevokeItemsAsync(
                It.IsAny<RevokeRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task VoidWithoutRecord_RetriesToAvoidLaterGrantRace()
    {
        var result = await _service.ProcessAsync(
            CreateVoidHint("record-race-token", refundType: 1));

        Assert.False(result.IsSuccess);
        Assert.True(result.IsRetryable);
        Assert.Equal("PURCHASE_RECORD_PENDING", result.ErrorCode);
    }

    private void SetupSnapshot(string token, GooglePlaySubscriptionSnapshot snapshot)
    {
        _snapshotProvider
            .Setup(value => value.QuerySubscriptionAsync(
                token,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(GooglePlaySubscriptionQueryResult.Success(snapshot));
    }

    private async Task<SubscriptionRecord> SeedSubscriptionAsync(
        string token,
        string playerId = "player-1")
    {
        var subscription = new SubscriptionRecord
        {
            SubscriptionKey = SubscriptionRecord.CreateGoogleKey(token),
            Platform = Platform.Google,
            PlayerId = playerId,
            ProductId = "premium_monthly",
            TierKey = "premium",
            TierPrecedence = 1,
            Status = SubscriptionStatus.Active,
            AutoRenew = true,
            PeriodStartUtc = NowUtc.AddDays(-30),
            PeriodEndUtc = NowUtc.AddDays(1),
            OriginalPurchaseDateUtc = NowUtc.AddDays(-60),
            LastEventAtUtc = NowUtc.AddMinutes(-1),
            CreatedAtUtc = NowUtc.AddDays(-60),
            UpdatedAtUtc = NowUtc.AddMinutes(-1)
        };
        subscription.SetActiveEconomyItemIds(new[] { "premium_access", "subscriber_badge" });
        Assert.True(await _repository.CreateSubscriptionAsync(subscription));
        return subscription;
    }

    private async Task<PurchaseRecord> SeedPurchaseAsync(string token)
    {
        var purchase = new PurchaseRecord
        {
            TransactionKey = PurchaseRecord.CreateGoogleTransactionKey(token),
            Platform = Platform.Google,
            ProductId = "durable_product",
            ProductType = ProductType.NonConsumable,
            PlayerId = "player-1",
            Status = PurchaseStatus.Granted,
            GrantedEconomyItemIds = new List<string> { "skin", "badge" },
            GrantEconomyItemIds = new List<string> { "skin", "badge" },
            GrantQuantities = new List<int> { 1, 1 },
            QuantityGranted = 1,
            CreatedAtUtc = NowUtc.AddDays(-1),
            UpdatedAtUtc = NowUtc.AddDays(-1)
        };
        Assert.True(await _repository.CreatePurchaseAsync(purchase));
        return purchase;
    }

    private async Task<PurchaseRecord> SeedSubscriptionPurchaseAsync(string token)
    {
        var purchase = new PurchaseRecord
        {
            TransactionKey = PurchaseRecord.CreateGoogleTransactionKey(token),
            Platform = Platform.Google,
            ProductId = "premium_monthly",
            ProductType = ProductType.Subscription,
            PlayerId = "player-1",
            Status = PurchaseStatus.Granted,
            GrantedEconomyItemIds = new List<string> { "premium_access", "subscriber_badge" },
            GrantEconomyItemIds = new List<string> { "premium_access", "subscriber_badge" },
            GrantQuantities = new List<int> { 1, 1 },
            QuantityGranted = 1,
            CreatedAtUtc = NowUtc.AddDays(-1),
            UpdatedAtUtc = NowUtc.AddDays(-1)
        };
        Assert.True(await _repository.CreatePurchaseAsync(purchase));
        return purchase;
    }

    private static GooglePlaySubscriptionSnapshot CreateSnapshot(
        GooglePlaySubscriptionState state) => new()
        {
            State = state,
            ProductId = "premium_monthly",
            StartTimeUtc = NowUtc.AddDays(-30),
            ExpiryTimeUtc = NowUtc.AddDays(30),
            LatestSuccessfulOrderId = "GPA.safe-order-id",
            AutoRenewEnabled = true,
            IsTestPurchase = false,
            ExternalAccountIdentifiers = new GooglePlayExternalAccountIdentifiers
            {
                ObfuscatedExternalAccountId = CreateBinding("player-1")
            }
        };

    private static GoogleRtdnNotification CreateSubscriptionHint(
        string token,
        int notificationType) => new()
        {
            EventId = $"subscription-event-{notificationType}-{token}",
            Kind = GoogleRtdnNotificationKind.SubscriptionStateChanged,
            PurchaseToken = token,
            NotificationType = notificationType,
            EventTimestampUtc = NowUtc,
            ReceivedAtUtc = NowUtc.AddMinutes(1)
        };

    private static GoogleRtdnNotification CreateVoidHint(
        string token,
        int refundType,
        string? eventId = null,
        string productId = "durable_product") => new()
        {
            EventId = eventId ?? $"void-event-{token}",
            Kind = GoogleRtdnNotificationKind.VoidedPurchase,
            PurchaseToken = token,
            ProductIdHint = productId,
            ProductType = 2,
            RefundType = refundType,
            EventTimestampUtc = NowUtc,
            ReceivedAtUtc = NowUtc.AddMinutes(1)
        };

    private static string CreateBinding(string playerId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"serhat-forge/google-account/v1:{playerId}")));

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTime utcNow)
        {
            _utcNow = new DateTimeOffset(utcNow);
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
