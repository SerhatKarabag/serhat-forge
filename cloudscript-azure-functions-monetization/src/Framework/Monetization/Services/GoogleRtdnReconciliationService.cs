using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;
using Serhat.Forge.CloudScript.Framework.Monetization.Verification;
using Serhat.Forge.CloudScript.Framework.Monetization.Webhooks;
using Serhat.Forge.CloudScript.Infrastructure.Security;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Services;

/// <summary>
/// Reconciles authenticated Google RTDN change hints against authoritative Google Play
/// state and durable purchase/subscription ownership before changing entitlements.
/// </summary>
public sealed class GoogleRtdnReconciliationService
{
    private const int FullRefundType = 1;
    private const string AccountBindingPrefix = "serhat-forge/google-account/v1:";

    private readonly IGooglePlaySubscriptionSnapshotProvider _snapshotProvider;
    private readonly IPurchaseRepository _repository;
    private readonly SubscriptionLifecycleService _lifecycleService;
    private readonly PurchaseRefundReconciliationService _refundService;
    private readonly bool _requireObfuscatedAccountId;
    private readonly ILogger<GoogleRtdnReconciliationService> _logger;
    private readonly TimeProvider _timeProvider;

    public GoogleRtdnReconciliationService(
        IGooglePlaySubscriptionSnapshotProvider snapshotProvider,
        IPurchaseRepository repository,
        SubscriptionLifecycleService lifecycleService,
        PurchaseRefundReconciliationService refundService,
        bool requireObfuscatedAccountId,
        ILogger<GoogleRtdnReconciliationService> logger,
        TimeProvider? timeProvider = null)
    {
        _snapshotProvider = snapshotProvider ??
                            throw new ArgumentNullException(nameof(snapshotProvider));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _lifecycleService = lifecycleService ??
                            throw new ArgumentNullException(nameof(lifecycleService));
        _refundService = refundService ?? throw new ArgumentNullException(nameof(refundService));
        _requireObfuscatedAccountId = requireObfuscatedAccountId;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<WebhookProcessingResult> ProcessAsync(
        GoogleRtdnNotification notification,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (string.IsNullOrWhiteSpace(notification.EventId) ||
            string.IsNullOrWhiteSpace(notification.PurchaseToken))
        {
            return Task.FromResult(PermanentFailure(
                "Authenticated RTDN identity is incomplete",
                "INVALID_NOTIFICATION"));
        }

        return notification.Kind switch
        {
            GoogleRtdnNotificationKind.SubscriptionStateChanged =>
                ProcessSubscriptionAsync(notification, ct),
            GoogleRtdnNotificationKind.OneTimeProductChanged =>
                ProcessOneTimeHintAsync(notification, ct),
            GoogleRtdnNotificationKind.VoidedPurchase =>
                ProcessVoidedPurchaseAsync(notification, ct),
            _ => Task.FromResult(PermanentFailure(
                "Unsupported Google RTDN notification",
                "UNSUPPORTED_NOTIFICATION"))
        };
    }

    private async Task<WebhookProcessingResult> ProcessSubscriptionAsync(
        GoogleRtdnNotification notification,
        CancellationToken ct)
    {
        var query = await _snapshotProvider
            .QuerySubscriptionAsync(notification.PurchaseToken, ct)
            .ConfigureAwait(false);
        if (!query.IsSuccess)
        {
            var code = query.ErrorCode ?? "GOOGLE_SUBSCRIPTION_QUERY_FAILED";
            return query.Failure == GooglePlaySubscriptionQueryFailure.Retryable
                ? RetryableFailure("Google subscription state is temporarily unavailable", code)
                : PermanentFailure("Google rejected the subscription token", code);
        }

        var snapshot = query.Snapshot!;
        if (snapshot.State == GooglePlaySubscriptionState.Unspecified)
        {
            return RetryableFailure(
                "Google returned an unsupported subscription state",
                "UNKNOWN_SUBSCRIPTION_STATE");
        }

        var subscriptionKey = SubscriptionRecord.CreateGoogleKey(notification.PurchaseToken);
        var subscription = await _repository
            .GetSubscriptionAsync(subscriptionKey, ct)
            .ConfigureAwait(false);
        if (subscription == null)
        {
            // An inactive/pending token with no durable record has no entitlement to revoke.
            // Benefit-bearing states are retried because RTDN can race the client-authoritative
            // VerifyPurchase call which creates the player-owned record.
            return SnapshotProvidesBenefits(snapshot, UtcNow)
                ? RetryableFailure(
                    "Subscription record has not been created yet",
                    "SUBSCRIPTION_RECORD_PENDING")
                : await CompleteNoMutationAsync(notification.EventId, ct).ConfigureAwait(false);
        }

        if (!string.Equals(subscription.Platform, Platform.Google, StringComparison.Ordinal) ||
            !string.Equals(subscription.ProductId, snapshot.ProductId, StringComparison.Ordinal))
        {
            return RetryableFailure(
                "Subscription identity requires manual reconciliation",
                "SUBSCRIPTION_IDENTITY_CONFLICT");
        }

        var accountBindingFailure = ValidateAccountBinding(subscription, snapshot);
        if (accountBindingFailure != null)
        {
            return accountBindingFailure;
        }

        if (IsTerminalFraudState(subscription.Status))
        {
            return SnapshotProvidesBenefits(snapshot, UtcNow)
                ? RetryableFailure(
                    "A terminal subscription cannot be automatically reactivated",
                    "TERMINAL_SUBSCRIPTION_REACTIVATION")
                : await CompleteNoMutationAsync(notification.EventId, ct).ConfigureAwait(false);
        }

        if (SnapshotProvidesBenefits(snapshot, UtcNow))
        {
            var ownershipResult = await ReconcileLinkedOwnershipAsync(
                notification,
                snapshot,
                subscription,
                ct).ConfigureAwait(false);
            if (ownershipResult != null)
            {
                return ownershipResult;
            }
        }

        if (snapshot.State == GooglePlaySubscriptionState.Pending &&
            subscription.ActiveEconomyItemIds.Count == 0)
        {
            return await CompleteNoMutationAsync(notification.EventId, ct).ConfigureAwait(false);
        }

        var result = await ApplyAuthoritativeSubscriptionStateAsync(
            notification,
            snapshot,
            subscription,
            ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return result;
        }

        // Lifecycle transitions own state and inventory ordering. This small follow-up keeps
        // Google-provided period/renewal fields exact. If a newer event already won, leave it.
        return await PersistAuthoritativeFieldsAsync(
            notification,
            snapshot,
            subscriptionKey,
            result,
            ct).ConfigureAwait(false);
    }

    private async Task<WebhookProcessingResult?> ReconcileLinkedOwnershipAsync(
        GoogleRtdnNotification notification,
        GooglePlaySubscriptionSnapshot snapshot,
        SubscriptionRecord current,
        CancellationToken ct)
    {
        string? linkedKey = null;
        SubscriptionRecord? linked = null;
        if (!string.IsNullOrWhiteSpace(snapshot.LinkedPurchaseToken))
        {
            linkedKey = SubscriptionRecord.CreateGoogleKey(snapshot.LinkedPurchaseToken);
            if (!string.Equals(linkedKey, current.SubscriptionKey, StringComparison.Ordinal))
            {
                linked = await _repository.GetSubscriptionAsync(linkedKey, ct)
                    .ConfigureAwait(false);
                if (linked == null)
                {
                    return RetryableFailure(
                        "Linked subscription ownership is unknown",
                        "LINKED_SUBSCRIPTION_NOT_FOUND");
                }

                if (!string.Equals(linked.PlayerId, current.PlayerId, StringComparison.Ordinal))
                {
                    return RetryableFailure(
                        "Linked subscription belongs to a different player",
                        "LINKED_SUBSCRIPTION_OWNER_CONFLICT");
                }
            }
        }

        var playerSubscriptions = await _repository
            .GetSubscriptionsByPlayerAsync(current.PlayerId, ct)
            .ConfigureAwait(false);
        var unexpectedActive = playerSubscriptions.FirstOrDefault(candidate =>
            !string.Equals(candidate.SubscriptionKey, current.SubscriptionKey, StringComparison.Ordinal) &&
            !string.Equals(candidate.SubscriptionKey, linkedKey, StringComparison.Ordinal) &&
            RecordProvidesBenefits(candidate, UtcNow));
        if (unexpectedActive != null)
        {
            return RetryableFailure(
                "Another active subscription requires manual reconciliation",
                "MULTIPLE_ACTIVE_SUBSCRIPTIONS");
        }

        if (linked == null || !RecordProvidesBenefits(linked, UtcNow))
        {
            return null;
        }

        var linkedEvent = CreateLifecycleEvent(
            notification,
            snapshot,
            WebhookEventType.Revoked,
            linked.SubscriptionKey,
            eventId: CreateDerivedEventId(notification.EventId, "linked", linked.SubscriptionKey));
        linkedEvent.ProductId = linked.ProductId;
        linkedEvent.RawPayloadPreview = "AuthoritativeLinkedSubscriptionSuperseded";

        var result = await _lifecycleService
            .ProcessWebhookEventAsync(linkedEvent, ct)
            .ConfigureAwait(false);
        return result.IsSuccess ? null : result;
    }

    private async Task<WebhookProcessingResult> ApplyAuthoritativeSubscriptionStateAsync(
        GoogleRtdnNotification notification,
        GooglePlaySubscriptionSnapshot snapshot,
        SubscriptionRecord subscription,
        CancellationToken ct)
    {
        if (snapshot.State is GooglePlaySubscriptionState.Active or
            GooglePlaySubscriptionState.InGracePeriod or
            GooglePlaySubscriptionState.Canceled)
        {
            if (snapshot.StartTimeUtc == null || snapshot.ExpiryTimeUtc == null)
            {
                return RetryableFailure(
                    "Google omitted required subscription timestamps",
                    "INCOMPLETE_SUBSCRIPTION_SNAPSHOT");
            }
        }

        if (snapshot.State is GooglePlaySubscriptionState.InGracePeriod or
            GooglePlaySubscriptionState.Canceled &&
            subscription.ActiveEconomyItemIds.Count == 0)
        {
            var restore = CreateLifecycleEvent(
                notification,
                snapshot,
                WebhookEventType.Resumed,
                subscription.SubscriptionKey,
                eventId: CreateDerivedEventId(
                    notification.EventId,
                    "restore",
                    subscription.SubscriptionKey));
            var restored = await _lifecycleService
                .ProcessWebhookEventAsync(restore, ct)
                .ConfigureAwait(false);
            if (!restored.IsSuccess)
            {
                return restored;
            }
        }

        var eventType = snapshot.State switch
        {
            GooglePlaySubscriptionState.Active =>
                subscription.ActiveEconomyItemIds.Count == 0 ||
                subscription.Status is SubscriptionStatus.Paused or SubscriptionStatus.Expired
                    ? WebhookEventType.Resumed
                    : WebhookEventType.Renewed,
            GooglePlaySubscriptionState.InGracePeriod => WebhookEventType.GracePeriodStarted,
            GooglePlaySubscriptionState.Canceled when snapshot.ExpiryTimeUtc <= UtcNow =>
                WebhookEventType.Expired,
            GooglePlaySubscriptionState.Canceled => WebhookEventType.Cancelled,
            GooglePlaySubscriptionState.Paused => WebhookEventType.Paused,
            GooglePlaySubscriptionState.OnHold => WebhookEventType.Paused,
            GooglePlaySubscriptionState.Expired => WebhookEventType.Expired,
            GooglePlaySubscriptionState.PendingPurchaseCanceled => WebhookEventType.Revoked,
            GooglePlaySubscriptionState.Pending => WebhookEventType.Revoked,
            _ => WebhookEventType.Other
        };

        if (eventType == WebhookEventType.Other)
        {
            return RetryableFailure(
                "Google returned an unsupported subscription state",
                "UNKNOWN_SUBSCRIPTION_STATE");
        }

        var lifecycleEvent = CreateLifecycleEvent(
            notification,
            snapshot,
            eventType,
            subscription.SubscriptionKey,
            notification.EventId);
        return await _lifecycleService
            .ProcessWebhookEventAsync(lifecycleEvent, ct)
            .ConfigureAwait(false);
    }

    private async Task<WebhookProcessingResult> PersistAuthoritativeFieldsAsync(
        GoogleRtdnNotification notification,
        GooglePlaySubscriptionSnapshot snapshot,
        string subscriptionKey,
        WebhookProcessingResult lifecycleResult,
        CancellationToken ct)
    {
        var durable = await _repository.GetSubscriptionAsync(subscriptionKey, ct)
            .ConfigureAwait(false);
        if (durable == null)
        {
            return RetryableFailure(
                "Subscription state disappeared during reconciliation",
                "SUBSCRIPTION_UPDATE_FAILED");
        }

        var eventAtUtc = ResolveEventTimestamp(notification);
        if (durable.LastEventAtUtc > eventAtUtc)
        {
            return lifecycleResult;
        }

        if (snapshot.StartTimeUtc.HasValue)
        {
            durable.PeriodStartUtc = snapshot.StartTimeUtc.Value;
            if (durable.OriginalPurchaseDateUtc == default ||
                snapshot.StartTimeUtc.Value < durable.OriginalPurchaseDateUtc)
            {
                durable.OriginalPurchaseDateUtc = snapshot.StartTimeUtc.Value;
            }
        }

        if (snapshot.ExpiryTimeUtc.HasValue)
        {
            durable.PeriodEndUtc = snapshot.ExpiryTimeUtc.Value;
        }

        durable.AutoRenew = snapshot.State == GooglePlaySubscriptionState.Canceled
            ? false
            : snapshot.AutoRenewEnabled;
        if (!string.IsNullOrWhiteSpace(snapshot.LatestSuccessfulOrderId))
        {
            durable.LatestStoreOrderId = snapshot.LatestSuccessfulOrderId;
        }
        durable.IsSandbox = snapshot.IsTestPurchase;
        durable.UpdatedAtUtc = UtcNow;

        try
        {
            return await _repository.TryUpdateSubscriptionIfNotNewerAsync(durable, ct)
                .ConfigureAwait(false)
                ? lifecycleResult
                : RetryableFailure(
                    "Subscription state update was rejected",
                    "SUBSCRIPTION_UPDATE_FAILED");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            _logger.LogWarning(
                "Google subscription field persistence failed: ErrorType={ErrorType}, SubscriptionToken={SubscriptionToken}",
                error.GetType().Name,
                SensitiveLogValue.Fingerprint(subscriptionKey));
            return RetryableFailure(
                "Subscription state update failed",
                "SUBSCRIPTION_UPDATE_FAILED");
        }
    }

    private Task<WebhookProcessingResult> ProcessOneTimeHintAsync(
        GoogleRtdnNotification notification,
        CancellationToken ct)
    {
        // PURCHASED/CANCELED RTDN is deliberately not a grant authority. The authenticated
        // client VerifyPurchase flow performs the Developer API query and durable grant.
        return CompleteNoMutationAsync(notification.EventId, ct);
    }

    private Task<WebhookProcessingResult> ProcessVoidedPurchaseAsync(
        GoogleRtdnNotification notification,
        CancellationToken ct)
    {
        var canonicalKey = PurchaseRecord.CreateGoogleTransactionKey(
            notification.PurchaseToken);
        return _refundService.ProcessAsync(
            new PurchaseRefundReconciliationRequest
            {
                EventId = notification.EventId,
                TransactionKey = canonicalKey,
                Platform = Platform.Google,
                ProductIdHint = notification.ProductIdHint,
                SubscriptionKey = canonicalKey,
                IsFullRefund = notification.RefundType == FullRefundType,
                LifecycleEventType = WebhookEventType.Refunded,
                EventTimestampUtc = notification.EventTimestampUtc,
                ReceivedAtUtc = notification.ReceivedAtUtc
            },
            ct);
    }

    private WebhookProcessingResult? ValidateAccountBinding(
        SubscriptionRecord subscription,
        GooglePlaySubscriptionSnapshot snapshot)
    {
        var actual = snapshot.ExternalAccountIdentifiers?.ObfuscatedExternalAccountId;
        if (string.IsNullOrWhiteSpace(actual))
        {
            return _requireObfuscatedAccountId
                ? RetryableFailure(
                    "Google subscription account binding is missing",
                    "ACCOUNT_BINDING_MISSING")
                : null;
        }

        var expected = CreateGoogleAccountBinding(subscription.PlayerId);
        return FixedTimeEquals(expected, actual)
            ? null
            : RetryableFailure(
                "Google subscription account binding does not match",
                "ACCOUNT_BINDING_MISMATCH");
    }

    private static WebhookEvent CreateLifecycleEvent(
        GoogleRtdnNotification notification,
        GooglePlaySubscriptionSnapshot snapshot,
        WebhookEventType eventType,
        string subscriptionKey,
        string eventId) => new()
    {
        EventId = eventId,
        EventType = eventType,
        Platform = Platform.Google,
        SubscriptionKey = subscriptionKey,
        ProductId = snapshot.ProductId,
        TransactionId = snapshot.LatestSuccessfulOrderId,
        OriginalTransactionId = snapshot.LatestSuccessfulOrderId,
        EventTimestampUtc = ResolveEventTimestamp(notification),
        PeriodStartUtc = snapshot.StartTimeUtc,
        PeriodEndUtc = snapshot.ExpiryTimeUtc,
        ExpirationDateUtc = snapshot.ExpiryTimeUtc,
        NewStatus = MapStatus(snapshot.State),
        AutoRenew = snapshot.State == GooglePlaySubscriptionState.Canceled
            ? false
            : snapshot.AutoRenewEnabled,
        GracePeriodEndUtc = snapshot.State == GooglePlaySubscriptionState.InGracePeriod
            ? snapshot.ExpiryTimeUtc
            : null,
        ReceivedAtUtc = ResolveReceivedTimestamp(notification),
        IsSandbox = snapshot.IsTestPurchase,
        RawPayloadPreview = $"AuthoritativeState:{snapshot.State}",
        EntitlementOperationId = CreateEntitlementOperationId(
            eventType,
            subscriptionKey,
            snapshot)
    };

    private async Task<WebhookProcessingResult> CompleteNoMutationAsync(
        string eventId,
        CancellationToken ct) =>
        await ProcessClaimedAsync(
            eventId,
            static () => Task.FromResult(WebhookProcessingResult.Success()),
            ct).ConfigureAwait(false);

    private async Task<WebhookProcessingResult> ProcessClaimedAsync(
        string eventId,
        Func<Task<WebhookProcessingResult>> operation,
        CancellationToken ct)
    {
        if (!await _repository.TryBeginWebhookProcessingAsync(eventId, ct).ConfigureAwait(false))
        {
            return WebhookProcessingResult.Duplicate();
        }

        try
        {
            var result = await operation().ConfigureAwait(false);
            if (result.IsRetryable)
            {
                await _repository.AbandonWebhookProcessingAsync(eventId, ct)
                    .ConfigureAwait(false);
            }
            else
            {
                await _repository.CompleteWebhookProcessingAsync(eventId, ct)
                    .ConfigureAwait(false);
            }

            return result;
        }
        catch
        {
            try
            {
                await _repository.AbandonWebhookProcessingAsync(
                    eventId,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception cleanupError)
            {
                _logger.LogError(
                    "Failed to release Google RTDN claim: {ErrorType}",
                    cleanupError.GetType().Name);
            }

            throw;
        }
    }

    private static bool SnapshotProvidesBenefits(
        GooglePlaySubscriptionSnapshot snapshot,
        DateTime nowUtc) => snapshot.State switch
    {
        GooglePlaySubscriptionState.Active => true,
        GooglePlaySubscriptionState.InGracePeriod => true,
        GooglePlaySubscriptionState.Canceled =>
            snapshot.ExpiryTimeUtc.HasValue && snapshot.ExpiryTimeUtc.Value > nowUtc,
        _ => false
    };

    private static bool RecordProvidesBenefits(SubscriptionRecord record, DateTime nowUtc) =>
        record.Status is SubscriptionStatus.Active or SubscriptionStatus.GracePeriod ||
        record.Status == SubscriptionStatus.Cancelled && record.PeriodEndUtc > nowUtc;

    private static bool IsTerminalFraudState(SubscriptionStatus status) =>
        status is SubscriptionStatus.Refunded or SubscriptionStatus.Chargeback;

    private static SubscriptionStatus? MapStatus(GooglePlaySubscriptionState state) => state switch
    {
        GooglePlaySubscriptionState.Active => SubscriptionStatus.Active,
        GooglePlaySubscriptionState.InGracePeriod => SubscriptionStatus.GracePeriod,
        GooglePlaySubscriptionState.Canceled => SubscriptionStatus.Cancelled,
        GooglePlaySubscriptionState.Paused => SubscriptionStatus.Paused,
        GooglePlaySubscriptionState.OnHold => SubscriptionStatus.Paused,
        GooglePlaySubscriptionState.Expired => SubscriptionStatus.Expired,
        GooglePlaySubscriptionState.PendingPurchaseCanceled => SubscriptionStatus.Expired,
        GooglePlaySubscriptionState.Pending => SubscriptionStatus.None,
        _ => null
    };

    private static string CreateGoogleAccountBinding(string playerId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            AccountBindingPrefix + playerId)));

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static string CreateDerivedEventId(
        string eventId,
        string operation,
        string durableKey)
    {
        var material = string.Concat(eventId, "\n", operation, "\n", durableKey);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return $"google-{operation}:{Convert.ToHexString(digest)}";
    }

    private static string? CreateEntitlementOperationId(
        WebhookEventType eventType,
        string subscriptionKey,
        GooglePlaySubscriptionSnapshot snapshot)
    {
        var direction = eventType switch
        {
            WebhookEventType.Resumed => "activate",
            WebhookEventType.Paused or
                WebhookEventType.Expired or
                WebhookEventType.Revoked or
                WebhookEventType.Refunded or
                WebhookEventType.Chargeback or
                WebhookEventType.GracePeriodEnded => "deactivate",
            _ => null
        };
        if (direction == null)
        {
            return null;
        }

        // subscriptionsv2 does not expose a transition sequence number. Product plus the
        // authoritative entitlement window is the stable identity shared by duplicate RTDN
        // deliveries, while a later billing window naturally receives a new identity.
        var material = FormattableString.Invariant(
            $"{direction}\n{subscriptionKey}\n{snapshot.ProductId}\n{snapshot.StartTimeUtc?.Ticks ?? 0}\n{snapshot.ExpiryTimeUtc?.Ticks ?? 0}");
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return $"google-entitlement:{Convert.ToHexString(digest)}";
    }

    private static DateTime ResolveEventTimestamp(GoogleRtdnNotification notification) =>
        notification.ReceivedAtUtc == default
            ? NormalizeUtc(notification.EventTimestampUtc)
            : NormalizeUtc(notification.ReceivedAtUtc);

    private static DateTime ResolveReceivedTimestamp(GoogleRtdnNotification notification) =>
        notification.ReceivedAtUtc == default
            ? ResolveEventTimestamp(notification)
            : NormalizeUtc(notification.ReceivedAtUtc);

    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    private static WebhookProcessingResult RetryableFailure(string message, string code) =>
        WebhookProcessingResult.Failure(message, code, retryable: true);

    private static WebhookProcessingResult PermanentFailure(string message, string code) =>
        WebhookProcessingResult.Failure(message, code, retryable: false);
}

/// <summary>
/// Development-only provider used when the fake purchase verifier is enabled. It keeps the
/// Function host resolvable while ensuring RTDN can never become a fake grant authority.
/// </summary>
public sealed class DisabledGooglePlaySubscriptionSnapshotProvider :
    IGooglePlaySubscriptionSnapshotProvider
{
    public Task<GooglePlaySubscriptionQueryResult> QuerySubscriptionAsync(
        string purchaseToken,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(GooglePlaySubscriptionQueryResult.Permanent(
            "RTDN_DISABLED",
            "Google RTDN reconciliation is disabled while the fake verifier is enabled"));
    }
}
