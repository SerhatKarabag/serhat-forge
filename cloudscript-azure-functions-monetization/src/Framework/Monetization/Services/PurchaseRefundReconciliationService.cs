using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;
using Serhat.Forge.CloudScript.Infrastructure.Security;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Services;

/// <summary>
/// Provider-neutral, durable refund/revocation reconciler. Callers must translate sensitive
/// provider credentials into a canonical transaction key before invoking this service.
/// </summary>
public sealed class PurchaseRefundReconciliationService
{
    private readonly IPurchaseRepository _repository;
    private readonly IEntitlementGranter _granter;
    private readonly SubscriptionLifecycleService _lifecycleService;
    private readonly ILogger<PurchaseRefundReconciliationService> _logger;
    private readonly TimeProvider _timeProvider;

    public PurchaseRefundReconciliationService(
        IPurchaseRepository repository,
        IEntitlementGranter granter,
        SubscriptionLifecycleService lifecycleService,
        ILogger<PurchaseRefundReconciliationService> logger,
        TimeProvider? timeProvider = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _granter = granter ?? throw new ArgumentNullException(nameof(granter));
        _lifecycleService = lifecycleService ??
                            throw new ArgumentNullException(nameof(lifecycleService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<WebhookProcessingResult> ProcessAsync(
        PurchaseRefundReconciliationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.EventId) ||
            string.IsNullOrWhiteSpace(request.TransactionKey) ||
            string.IsNullOrWhiteSpace(request.Platform))
        {
            return PermanentFailure(
                "Refund reconciliation identity is incomplete",
                "INVALID_REFUND_NOTIFICATION");
        }

        if (!request.IsFullRefund)
        {
            // Quantity-based partial refunds do not contain enough trusted inventory quantity
            // information in store push messages. Retain the event for dead-letter/manual work.
            return RetryableFailure(
                "Partial or unknown refund requires manual reconciliation",
                "PARTIAL_REFUND_REQUIRES_RECONCILIATION");
        }

        var purchase = await ResolvePurchaseAsync(request, ct).ConfigureAwait(false);
        if (purchase == null)
        {
            // A refund can beat the client-authoritative verification. Acknowledging here could
            // allow the later verification to grant a refunded payment.
            return RetryableFailure(
                "Purchase record has not been created yet",
                "PURCHASE_RECORD_PENDING");
        }

        if (!string.Equals(purchase.Platform, request.Platform, StringComparison.Ordinal) ||
            (purchase.ProductType != ProductType.Subscription &&
             !string.IsNullOrWhiteSpace(request.ProductIdHint) &&
             !string.Equals(
                 purchase.ProductId,
                 request.ProductIdHint,
                 StringComparison.Ordinal)))
        {
            return RetryableFailure(
                "Refund purchase identity requires manual reconciliation",
                "PURCHASE_IDENTITY_CONFLICT");
        }

        if (purchase.Status is PurchaseStatus.Pending or PurchaseStatus.Verified)
        {
            // Avoid racing an active verification/grant worker with a non-conditional update.
            return RetryableFailure(
                "Purchase is still being processed",
                "PURCHASE_PROCESSING");
        }

        if (!HasUnitGrantQuantities(purchase))
        {
            return RetryableFailure(
                "Refund quantity requires manual reconciliation",
                "REFUND_QUANTITY_REQUIRES_RECONCILIATION");
        }

        if (purchase.ProductType == ProductType.Subscription)
        {
            return await ProcessSubscriptionAsync(request, purchase, ct).ConfigureAwait(false);
        }

        return await ProcessClaimedAsync(
            request.EventId,
            () => RevokeItemsAndMarkRefundedAsync(request, purchase, ct),
            ct).ConfigureAwait(false);
    }

    private async Task<PurchaseRecord?> ResolvePurchaseAsync(
        PurchaseRefundReconciliationRequest request,
        CancellationToken ct)
    {
        var exact = await _repository.GetPurchaseAsync(request.TransactionKey, ct)
            .ConfigureAwait(false);
        if (exact != null || string.IsNullOrWhiteSpace(request.SubscriptionKey))
        {
            return exact;
        }

        // App Store subscription refunds may reference a renewal transaction that the client
        // never submitted separately. Resolve the original durable purchase through the signed
        // original-transaction subscription key without trusting a player ID from the webhook.
        var subscription = await _repository
            .GetSubscriptionAsync(request.SubscriptionKey, ct)
            .ConfigureAwait(false);
        if (subscription == null ||
            !string.Equals(subscription.Platform, request.Platform, StringComparison.Ordinal))
        {
            return null;
        }

        var purchases = await _repository
            .GetPurchasesByPlayerAsync(subscription.PlayerId, ct)
            .ConfigureAwait(false);
        return purchases
            .Where(purchase =>
                purchase.ProductType == ProductType.Subscription &&
                string.Equals(purchase.Platform, request.Platform, StringComparison.Ordinal) &&
                BelongsToSubscription(purchase, request.SubscriptionKey))
            .OrderByDescending(purchase => purchase.Status == PurchaseStatus.Granted)
            .ThenByDescending(purchase => purchase.UpdatedAtUtc)
            .FirstOrDefault();
    }

    private static bool BelongsToSubscription(
        PurchaseRecord purchase,
        string subscriptionKey)
    {
        if (string.Equals(purchase.TransactionKey, subscriptionKey, StringComparison.Ordinal))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(purchase.OriginalTransactionId) &&
               string.Equals(
                   PurchaseRecord.CreateTransactionKey(
                       purchase.Platform,
                       purchase.OriginalTransactionId),
                   subscriptionKey,
                   StringComparison.Ordinal);
    }

    private async Task<WebhookProcessingResult> ProcessSubscriptionAsync(
        PurchaseRefundReconciliationRequest request,
        PurchaseRecord purchase,
        CancellationToken ct)
    {
        var subscriptionKey = string.IsNullOrWhiteSpace(request.SubscriptionKey)
            ? purchase.TransactionKey
            : request.SubscriptionKey;
        var subscription = await _repository.GetSubscriptionAsync(subscriptionKey, ct)
            .ConfigureAwait(false);
        var targetStatus = request.LifecycleEventType switch
        {
            WebhookEventType.Chargeback => SubscriptionStatus.Chargeback,
            WebhookEventType.Revoked => SubscriptionStatus.Expired,
            _ => SubscriptionStatus.Refunded
        };
        if (purchase.Status == PurchaseStatus.Refunded &&
            (subscription == null ||
             subscription.Status == targetStatus &&
             subscription.ActiveEconomyItemIds.Count == 0))
        {
            return WebhookProcessingResult.Success();
        }

        if (subscription == null ||
            !string.Equals(subscription.PlayerId, purchase.PlayerId, StringComparison.Ordinal))
        {
            return RetryableFailure(
                "Subscription record requires manual reconciliation",
                "SUBSCRIPTION_RECORD_PENDING");
        }

        var purchaseItems = new HashSet<string>(
            purchase.GrantedEconomyItemIds,
            StringComparer.Ordinal);
        var subscriptionItems = new HashSet<string>(
            subscription.ActiveEconomyItemIds,
            StringComparer.Ordinal);
        if (subscription.Status == targetStatus && subscriptionItems.Count > 0)
        {
            return RetryableFailure(
                "Terminal subscription still has active items",
                "SUBSCRIPTION_TERMINAL_GRANT_CONFLICT");
        }

        if (subscription.Status != targetStatus && !purchaseItems.SetEquals(subscriptionItems))
        {
            return RetryableFailure(
                "Subscription grant snapshot requires manual reconciliation",
                "SUBSCRIPTION_GRANT_SNAPSHOT_CONFLICT");
        }

        if (subscription.Status != targetStatus)
        {
            var lifecycleEvent = new WebhookEvent
            {
                // Different provider deliveries (or even refund vs chargeback signals) for
                // one payment must converge on exactly one inventory revoke operation.
                EventId = CreateLifecycleRevokeIdentity(purchase.TransactionKey),
                EventType = NormalizeLifecycleEventType(request.LifecycleEventType),
                Platform = request.Platform,
                SubscriptionKey = subscription.SubscriptionKey,
                ProductId = subscription.ProductId,
                EventTimestampUtc = NormalizeUtc(request.EventTimestampUtc),
                ReceivedAtUtc = NormalizeUtc(request.ReceivedAtUtc),
                AutoRenew = false,
                RawPayloadPreview = "AuthoritativeStoreRefundOrRevocation",
                EntitlementOperationId = CreateLifecycleRevokeIdentity(
                    purchase.TransactionKey)
            };
            var lifecycleResult = await _lifecycleService
                .ProcessWebhookEventAsync(lifecycleEvent, ct)
                .ConfigureAwait(false);
            if (!lifecycleResult.IsSuccess)
            {
                return lifecycleResult;
            }
        }

        return await MarkPurchaseRefundedAsync(purchase, ct).ConfigureAwait(false);
    }

    private async Task<WebhookProcessingResult> RevokeItemsAndMarkRefundedAsync(
        PurchaseRefundReconciliationRequest request,
        PurchaseRecord purchase,
        CancellationToken ct)
    {
        if (purchase.Status == PurchaseStatus.Refunded)
        {
            return WebhookProcessingResult.Success();
        }

        if (purchase.GrantedEconomyItemIds.Count > 0)
        {
            GrantResult? revoke;
            try
            {
                revoke = await _granter.RevokeItemsAsync(
                    new RevokeRequest
                    {
                        PlayerId = purchase.PlayerId,
                        ItemIds = new List<string>(purchase.GrantedEconomyItemIds),
                        IdempotencyKey = CreateIdempotencyKey(purchase.TransactionKey)
                    },
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                _logger.LogWarning(
                    "Refund revoke threw: ErrorType={ErrorType}, PurchaseToken={PurchaseToken}",
                    error.GetType().Name,
                    SensitiveLogValue.Fingerprint(purchase.TransactionKey));
                return RetryableFailure(
                    "Entitlement revoke failed",
                    "ENTITLEMENT_REVOKE_FAILED");
            }

            if (revoke?.IsSuccess != true)
            {
                _logger.LogWarning(
                    "Refund revoke failed: ProviderCode={ProviderCode}, PurchaseToken={PurchaseToken}",
                    revoke?.ErrorCode ?? "NO_RESULT",
                    SensitiveLogValue.Fingerprint(purchase.TransactionKey));
                return RetryableFailure(
                    "Entitlement revoke failed",
                    "ENTITLEMENT_REVOKE_FAILED");
            }
        }

        return await MarkPurchaseRefundedAsync(purchase, ct).ConfigureAwait(false);
    }

    private async Task<WebhookProcessingResult> MarkPurchaseRefundedAsync(
        PurchaseRecord purchase,
        CancellationToken ct)
    {
        purchase.Status = PurchaseStatus.Refunded;
        purchase.IsRetryable = false;
        purchase.NextRetryAtUtc = null;
        purchase.ErrorCode = null;
        purchase.ErrorMessage = null;
        purchase.ProcessingLeaseId = null;
        purchase.ProcessingLeaseExpiresAtUtc = null;
        purchase.UpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        try
        {
            return await _repository.UpdatePurchaseAsync(purchase, ct).ConfigureAwait(false)
                ? WebhookProcessingResult.Success()
                : RetryableFailure(
                    "Purchase refund state update was rejected",
                    "PURCHASE_UPDATE_FAILED");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            _logger.LogWarning(
                "Refund purchase update threw: ErrorType={ErrorType}, PurchaseToken={PurchaseToken}",
                error.GetType().Name,
                SensitiveLogValue.Fingerprint(purchase.TransactionKey));
            return RetryableFailure(
                "Purchase refund state update failed",
                "PURCHASE_UPDATE_FAILED");
        }
    }

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
                    "Failed to release refund webhook claim: {ErrorType}",
                    cleanupError.GetType().Name);
            }

            throw;
        }
    }

    private static bool HasUnitGrantQuantities(PurchaseRecord purchase)
    {
        if (purchase.GrantQuantities != null)
        {
            return purchase.GrantQuantities.Count == purchase.GrantedEconomyItemIds.Count &&
                   purchase.GrantQuantities.All(quantity => quantity == 1);
        }

        return purchase.QuantityGranted is 0 or 1;
    }

    private static WebhookEventType NormalizeLifecycleEventType(WebhookEventType value) =>
        value is WebhookEventType.Refunded or
            WebhookEventType.Chargeback or
            WebhookEventType.Revoked
            ? value
            : WebhookEventType.Refunded;

    private static string CreateIdempotencyKey(string transactionKey)
    {
        // Provider message IDs are delivery identities, not payment identities. Multiple
        // refund notifications for the same payment must converge on one inventory operation.
        var material = string.Concat("refund-revoke\n", transactionKey);
        return $"purchase-refund:{Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(material)))}";
    }

    private static string CreateLifecycleRevokeIdentity(string transactionKey)
    {
        var material = string.Concat("subscription-refund-revoke\n", transactionKey);
        return $"subscription-refund:{Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(material)))}";
    }

    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static WebhookProcessingResult RetryableFailure(string message, string code) =>
        WebhookProcessingResult.Failure(message, code, retryable: true);

    private static WebhookProcessingResult PermanentFailure(string message, string code) =>
        WebhookProcessingResult.Failure(message, code, retryable: false);
}

public sealed record PurchaseRefundReconciliationRequest
{
    public required string EventId { get; init; }
    public required string TransactionKey { get; init; }
    public required string Platform { get; init; }
    public string? ProductIdHint { get; init; }
    public string? SubscriptionKey { get; init; }
    public bool IsFullRefund { get; init; }
    public WebhookEventType LifecycleEventType { get; init; } = WebhookEventType.Refunded;
    public DateTime EventTimestampUtc { get; init; }
    public DateTime ReceivedAtUtc { get; init; }
}
