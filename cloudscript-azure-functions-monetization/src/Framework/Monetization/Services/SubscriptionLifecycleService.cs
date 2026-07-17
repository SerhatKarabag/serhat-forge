using System;
using System.Collections.Generic;
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
/// Processes durable, idempotent subscription lifecycle transitions from store webhooks.
/// Provider inventory side effects always succeed before the subscription state is committed.
/// </summary>
public sealed class SubscriptionLifecycleService
{
    private const string EntitlementGrantFailedCode = "ENTITLEMENT_GRANT_FAILED";
    private const string EntitlementRevokeFailedCode = "ENTITLEMENT_REVOKE_FAILED";
    private const string SubscriptionUpdateFailedCode = "SUBSCRIPTION_UPDATE_FAILED";
    private const string SubscriptionConfigurationFailedCode = "SUBSCRIPTION_CONFIGURATION_FAILED";

    private readonly IPurchaseRepository _repository;
    private readonly IEntitlementGranter _granter;
    private readonly ProductAllowlistConfig _productConfig;
    private readonly ILogger<SubscriptionLifecycleService> _logger;

    public SubscriptionLifecycleService(
        IPurchaseRepository repository,
        IEntitlementGranter granter,
        ProductAllowlistConfig productConfig,
        ILogger<SubscriptionLifecycleService> logger)
    {
        _repository = repository;
        _granter = granter;
        _productConfig = productConfig;
        _logger = logger;
    }

    /// <summary>
    /// Processes a webhook event for subscription lifecycle changes.
    /// </summary>
    public async Task<WebhookProcessingResult> ProcessWebhookEventAsync(
        WebhookEvent webhookEvent,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(webhookEvent);
        if (string.IsNullOrWhiteSpace(webhookEvent.EventId))
        {
            return WebhookProcessingResult.Failure(
                "Webhook event ID is required",
                "MISSING_EVENT_ID");
        }

        var eventToken = SensitiveLogValue.Fingerprint(webhookEvent.EventId);
        var subscriptionToken = SensitiveLogValue.Fingerprint(webhookEvent.SubscriptionKey);
        _logger.LogInformation(
            "Processing webhook: Type={EventType}, EventToken={EventToken}, SubscriptionToken={SubscriptionToken}",
            webhookEvent.EventType,
            eventToken,
            subscriptionToken);

        if (!await _repository.TryBeginWebhookProcessingAsync(webhookEvent.EventId, ct))
        {
            _logger.LogInformation("Webhook replay ignored: EventToken={EventToken}", eventToken);
            return WebhookProcessingResult.Duplicate();
        }

        try
        {
            WebhookProcessingResult result;
            var subscriptionKey = webhookEvent.SubscriptionKey;
            if (string.IsNullOrWhiteSpace(subscriptionKey))
            {
                _logger.LogWarning("Webhook has no subscription identity: EventToken={EventToken}", eventToken);
                result = WebhookProcessingResult.Success();
            }
            else
            {
                var durableSubscription = await _repository.GetSubscriptionAsync(subscriptionKey, ct);
                if (durableSubscription == null)
                {
                    _logger.LogWarning(
                        "Webhook subscription was not found: SubscriptionToken={SubscriptionToken}",
                        subscriptionToken);
                    result = WebhookProcessingResult.Success();
                }
                else if (IsStaleEvent(durableSubscription, webhookEvent))
                {
                    _logger.LogInformation(
                        "Stale subscription event ignored: Type={EventType}, EventToken={EventToken}, SubscriptionToken={SubscriptionToken}",
                        webhookEvent.EventType,
                        eventToken,
                        subscriptionToken);
                    result = WebhookProcessingResult.Success();
                }
                else
                {
                    // Never mutate a repository-owned object. Provider failures must leave the
                    // durable state unchanged so the same claimed event can be retried safely.
                    var candidate = durableSubscription.Copy();
                    result = webhookEvent.EventType switch
                    {
                        WebhookEventType.Renewed => await HandleRenewalAsync(candidate, webhookEvent, ct),
                        WebhookEventType.Cancelled => await HandleCancellationAsync(candidate, webhookEvent, ct),
                        WebhookEventType.Expired => await HandleExpirationAsync(candidate, webhookEvent, ct),
                        WebhookEventType.Refunded => await HandleRefundAsync(candidate, webhookEvent, ct),
                        WebhookEventType.Chargeback => await HandleChargebackAsync(candidate, webhookEvent, ct),
                        WebhookEventType.GracePeriodStarted => await HandleGracePeriodAsync(candidate, webhookEvent, ct),
                        WebhookEventType.GracePeriodEnded => await HandleGracePeriodEndedAsync(candidate, webhookEvent, ct),
                        WebhookEventType.Paused => await HandlePausedAsync(candidate, webhookEvent, ct),
                        WebhookEventType.Resumed => await HandleResumedAsync(candidate, webhookEvent, ct),
                        WebhookEventType.UpgradeDowngrade => await HandleTierChangeAsync(candidate, webhookEvent, ct),
                        WebhookEventType.Revoked => await HandleRevokedAsync(candidate, webhookEvent, ct),
                        _ => WebhookProcessingResult.Success()
                    };
                }
            }

            if (result.IsRetryable)
            {
                await _repository.AbandonWebhookProcessingAsync(webhookEvent.EventId, ct);
            }
            else
            {
                await _repository.CompleteWebhookProcessingAsync(webhookEvent.EventId, ct);
            }

            return result;
        }
        catch
        {
            try
            {
                await _repository.AbandonWebhookProcessingAsync(
                    webhookEvent.EventId,
                    CancellationToken.None);
            }
            catch (Exception cleanupError)
            {
                _logger.LogError(
                    "Failed to release webhook claim: {ErrorType}",
                    cleanupError.GetType().Name);
            }

            throw;
        }
    }

    private async Task<WebhookProcessingResult> HandleRenewalAsync(
        SubscriptionRecord subscription,
        WebhookEvent evt,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing renewal for {SubscriptionToken}",
            SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));

        if (!string.IsNullOrWhiteSpace(subscription.PendingTierKey))
        {
            var pendingProduct = ResolveSubscriptionProduct(
                subscription.PendingProductId,
                subscription.PendingTierKey);
            if (pendingProduct == null)
            {
                return ConfigurationFailure(subscription, subscription.PendingProductId);
            }

            var sideEffectFailure = await ApplyTierSideEffectsAsync(
                subscription,
                pendingProduct,
                evt,
                ct);
            if (sideEffectFailure != null)
            {
                return sideEffectFailure;
            }

            subscription.ProductId = pendingProduct.ProductId;
            subscription.TierKey = pendingProduct.TierKey ?? subscription.PendingTierKey;
            subscription.TierPrecedence = pendingProduct.TierPrecedence;
            subscription.PendingTierKey = null;
            subscription.PendingProductId = null;
        }

        var eventAtUtc = ResolveEventTimestampUtc(evt);
        subscription.Status = SubscriptionStatus.Active;
        subscription.AutoRenew = evt.AutoRenew ?? true;
        subscription.PeriodStartUtc = evt.PeriodStartUtc ?? eventAtUtc;
        subscription.PeriodEndUtc = evt.PeriodEndUtc ?? eventAtUtc.AddMonths(1);
        subscription.GracePeriodEndUtc = null;
        MarkEventApplied(subscription, eventAtUtc);

        return await CommitAsync(subscription, ct);
    }

    private async Task<WebhookProcessingResult> HandleCancellationAsync(
        SubscriptionRecord subscription,
        WebhookEvent evt,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing cancellation for {SubscriptionToken}",
            SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));

        subscription.Status = SubscriptionStatus.Cancelled;
        subscription.AutoRenew = false;
        MarkEventApplied(subscription, ResolveEventTimestampUtc(evt));

        // Cancellation retains benefits until the already-paid period expires.
        return await CommitAsync(subscription, ct);
    }

    private Task<WebhookProcessingResult> HandleExpirationAsync(
        SubscriptionRecord subscription,
        WebhookEvent evt,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing expiration for {SubscriptionToken}",
            SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));
        return RevokeAllAndCommitAsync(
            subscription,
            evt,
            SubscriptionStatus.Expired,
            autoRenew: false,
            clearGracePeriod: true,
            operation: "expire",
            ct);
    }

    private Task<WebhookProcessingResult> HandleRefundAsync(
        SubscriptionRecord subscription,
        WebhookEvent evt,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing refund for {SubscriptionToken}",
            SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));
        return RevokeAllAndCommitAsync(
            subscription,
            evt,
            SubscriptionStatus.Refunded,
            autoRenew: false,
            clearGracePeriod: true,
            operation: "refund",
            ct);
    }

    private Task<WebhookProcessingResult> HandleChargebackAsync(
        SubscriptionRecord subscription,
        WebhookEvent evt,
        CancellationToken ct)
    {
        _logger.LogWarning(
            "Processing chargeback for {SubscriptionToken}",
            SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));
        return RevokeAllAndCommitAsync(
            subscription,
            evt,
            SubscriptionStatus.Chargeback,
            autoRenew: false,
            clearGracePeriod: true,
            operation: "chargeback",
            ct);
    }

    private async Task<WebhookProcessingResult> HandleGracePeriodAsync(
        SubscriptionRecord subscription,
        WebhookEvent evt,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing grace period start for {SubscriptionToken}",
            SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));

        subscription.Status = SubscriptionStatus.GracePeriod;
        subscription.GracePeriodEndUtc = evt.GracePeriodEndUtc;
        MarkEventApplied(subscription, ResolveEventTimestampUtc(evt));

        // Benefits remain active throughout the provider-declared grace period.
        return await CommitAsync(subscription, ct);
    }

    private Task<WebhookProcessingResult> HandleGracePeriodEndedAsync(
        SubscriptionRecord subscription,
        WebhookEvent evt,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing grace period end for {SubscriptionToken}",
            SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));

        if (subscription.Status != SubscriptionStatus.GracePeriod)
        {
            return Task.FromResult(WebhookProcessingResult.Success());
        }

        return RevokeAllAndCommitAsync(
            subscription,
            evt,
            SubscriptionStatus.Expired,
            autoRenew: false,
            clearGracePeriod: true,
            operation: "grace-end",
            ct);
    }

    private Task<WebhookProcessingResult> HandlePausedAsync(
        SubscriptionRecord subscription,
        WebhookEvent evt,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing pause for {SubscriptionToken}",
            SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));
        return RevokeAllAndCommitAsync(
            subscription,
            evt,
            SubscriptionStatus.Paused,
            autoRenew: null,
            clearGracePeriod: false,
            operation: "pause",
            ct);
    }

    private async Task<WebhookProcessingResult> HandleResumedAsync(
        SubscriptionRecord subscription,
        WebhookEvent evt,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing resume for {SubscriptionToken}",
            SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));

        var product = _productConfig.GetProduct(subscription.ProductId);
        if (product == null || !product.IsSubscription)
        {
            return ConfigurationFailure(subscription, subscription.ProductId);
        }

        var grantFailure = await GrantItemsAsync(
            subscription,
            product.EconomyItemIds,
            "resume-grant",
            evt,
            ct);
        if (grantFailure != null)
        {
            return grantFailure;
        }

        var eventAtUtc = ResolveEventTimestampUtc(evt);
        subscription.SetActiveEconomyItemIds(product.EconomyItemIds);
        subscription.Status = SubscriptionStatus.Active;
        subscription.AutoRenew = evt.AutoRenew ?? true;
        subscription.PeriodStartUtc = evt.PeriodStartUtc ?? eventAtUtc;
        subscription.PeriodEndUtc = evt.PeriodEndUtc ?? eventAtUtc.AddMonths(1);
        subscription.GracePeriodEndUtc = null;
        MarkEventApplied(subscription, eventAtUtc);

        return await CommitAsync(subscription, ct);
    }

    private async Task<WebhookProcessingResult> HandleTierChangeAsync(
        SubscriptionRecord subscription,
        WebhookEvent evt,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing tier change for {SubscriptionToken}: {OldTier} -> {NewTier}",
            SensitiveLogValue.Fingerprint(subscription.SubscriptionKey),
            subscription.TierKey,
            evt.NewTierKey);

        if (string.IsNullOrWhiteSpace(evt.NewTierKey))
        {
            return WebhookProcessingResult.Success();
        }

        var newProduct = ResolveSubscriptionProduct(evt.NewProductId, evt.NewTierKey);
        if (newProduct == null)
        {
            return ConfigurationFailure(subscription, evt.NewProductId);
        }

        if (newProduct.TierPrecedence > subscription.TierPrecedence)
        {
            var sideEffectFailure = await ApplyTierSideEffectsAsync(
                subscription,
                newProduct,
                evt,
                ct);
            if (sideEffectFailure != null)
            {
                return sideEffectFailure;
            }

            subscription.ProductId = newProduct.ProductId;
            subscription.TierKey = newProduct.TierKey ?? evt.NewTierKey;
            subscription.TierPrecedence = newProduct.TierPrecedence;
            subscription.PendingTierKey = null;
            subscription.PendingProductId = null;
        }
        else
        {
            // Downgrades retain the paid higher tier until the next renewal.
            subscription.PendingTierKey = newProduct.TierKey ?? evt.NewTierKey;
            subscription.PendingProductId = newProduct.ProductId;
        }

        MarkEventApplied(subscription, ResolveEventTimestampUtc(evt));
        return await CommitAsync(subscription, ct);
    }

    private Task<WebhookProcessingResult> HandleRevokedAsync(
        SubscriptionRecord subscription,
        WebhookEvent evt,
        CancellationToken ct)
    {
        _logger.LogWarning(
            "Processing revocation for {SubscriptionToken}",
            SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));
        return RevokeAllAndCommitAsync(
            subscription,
            evt,
            SubscriptionStatus.Expired,
            autoRenew: false,
            clearGracePeriod: true,
            operation: "revoke",
            ct);
    }

    private async Task<WebhookProcessingResult> RevokeAllAndCommitAsync(
        SubscriptionRecord subscription,
        WebhookEvent evt,
        SubscriptionStatus newStatus,
        bool? autoRenew,
        bool clearGracePeriod,
        string operation,
        CancellationToken ct)
    {
        var revokeFailure = await RevokeItemsAsync(
            subscription,
            subscription.ActiveEconomyItemIds,
            operation,
            evt,
            ct);
        if (revokeFailure != null)
        {
            return revokeFailure;
        }

        subscription.SetActiveEconomyItemIds(Array.Empty<string>());
        subscription.Status = newStatus;
        if (autoRenew.HasValue)
        {
            subscription.AutoRenew = autoRenew.Value;
        }

        if (clearGracePeriod)
        {
            subscription.GracePeriodEndUtc = null;
        }

        MarkEventApplied(subscription, ResolveEventTimestampUtc(evt));
        return await CommitAsync(subscription, ct);
    }

    private async Task<WebhookProcessingResult?> ApplyTierSideEffectsAsync(
        SubscriptionRecord subscription,
        ProductConfig newProduct,
        WebhookEvent evt,
        CancellationToken ct)
    {
        var revokeFailure = await RevokeItemsAsync(
            subscription,
            subscription.ActiveEconomyItemIds,
            "tier-revoke",
            evt,
            ct);
        if (revokeFailure != null)
        {
            return revokeFailure;
        }

        var grantFailure = await GrantItemsAsync(
            subscription,
            newProduct.EconomyItemIds,
            "tier-grant",
            evt,
            ct);
        if (grantFailure != null)
        {
            return grantFailure;
        }

        subscription.SetActiveEconomyItemIds(newProduct.EconomyItemIds);
        return null;
    }

    private async Task<WebhookProcessingResult?> GrantItemsAsync(
        SubscriptionRecord subscription,
        IReadOnlyList<string> itemIds,
        string operation,
        WebhookEvent evt,
        CancellationToken ct)
    {
        if (itemIds.Count == 0)
        {
            return null;
        }

        GrantResult? result;
        try
        {
            result = await _granter.GrantItemsAsync(
                new GrantRequest
                {
                    PlayerId = subscription.PlayerId,
                    ItemIds = new List<string>(itemIds),
                    IdempotencyKey = CreateIdempotencyKey(
                        "grant",
                        operation,
                        subscription,
                        evt)
                },
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            _logger.LogWarning(
                "Subscription grant provider threw: Operation={Operation}, ErrorType={ErrorType}, SubscriptionToken={SubscriptionToken}",
                operation,
                error.GetType().Name,
                SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));
            return RetryableFailure("Subscription entitlement grant failed", EntitlementGrantFailedCode);
        }

        if (result?.IsSuccess == true)
        {
            return null;
        }

        _logger.LogWarning(
            "Subscription grant provider failed: Operation={Operation}, ProviderCode={ProviderCode}, SubscriptionToken={SubscriptionToken}",
            operation,
            result?.ErrorCode ?? "NO_RESULT",
            SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));
        return RetryableFailure("Subscription entitlement grant failed", EntitlementGrantFailedCode);
    }

    private async Task<WebhookProcessingResult?> RevokeItemsAsync(
        SubscriptionRecord subscription,
        IReadOnlyList<string> itemIds,
        string operation,
        WebhookEvent evt,
        CancellationToken ct)
    {
        if (itemIds.Count == 0)
        {
            return null;
        }

        GrantResult? result;
        try
        {
            result = await _granter.RevokeItemsAsync(
                new RevokeRequest
                {
                    PlayerId = subscription.PlayerId,
                    ItemIds = new List<string>(itemIds),
                    IdempotencyKey = CreateIdempotencyKey(
                        "revoke",
                        operation,
                        subscription,
                        evt)
                },
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            _logger.LogWarning(
                "Subscription revoke provider threw: Operation={Operation}, ErrorType={ErrorType}, SubscriptionToken={SubscriptionToken}",
                operation,
                error.GetType().Name,
                SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));
            return RetryableFailure("Subscription entitlement revoke failed", EntitlementRevokeFailedCode);
        }

        if (result?.IsSuccess == true)
        {
            return null;
        }

        _logger.LogWarning(
            "Subscription revoke provider failed: Operation={Operation}, ProviderCode={ProviderCode}, SubscriptionToken={SubscriptionToken}",
            operation,
            result?.ErrorCode ?? "NO_RESULT",
            SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));
        return RetryableFailure("Subscription entitlement revoke failed", EntitlementRevokeFailedCode);
    }

    private async Task<WebhookProcessingResult> CommitAsync(
        SubscriptionRecord subscription,
        CancellationToken ct)
    {
        try
        {
            if (await _repository.TryUpdateSubscriptionIfNotNewerAsync(subscription, ct))
            {
                return WebhookProcessingResult.Success();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            _logger.LogWarning(
                "Subscription state update threw: ErrorType={ErrorType}, SubscriptionToken={SubscriptionToken}",
                error.GetType().Name,
                SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));
            return RetryableFailure("Subscription state update failed", SubscriptionUpdateFailedCode);
        }

        _logger.LogWarning(
            "Subscription state update was rejected: SubscriptionToken={SubscriptionToken}",
            SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));
        return RetryableFailure("Subscription state update failed", SubscriptionUpdateFailedCode);
    }

    private ProductConfig? ResolveSubscriptionProduct(string? productId, string? tierKey)
    {
        if (!string.IsNullOrWhiteSpace(productId))
        {
            var exactProduct = _productConfig.GetProduct(productId);
            if (exactProduct?.IsSubscription == true)
            {
                return exactProduct;
            }
        }

        if (string.IsNullOrWhiteSpace(tierKey))
        {
            return null;
        }

        foreach (var product in _productConfig.Products.Values)
        {
            if (product.Enabled &&
                product.IsSubscription &&
                string.Equals(product.TierKey, tierKey, StringComparison.Ordinal))
            {
                return product;
            }
        }

        return null;
    }

    private WebhookProcessingResult ConfigurationFailure(
        SubscriptionRecord subscription,
        string? productId)
    {
        _logger.LogWarning(
            "Subscription product configuration was not found: ProductId={ProductId}, SubscriptionToken={SubscriptionToken}",
            productId,
            SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));
        return RetryableFailure(
            "Subscription product configuration is unavailable",
            SubscriptionConfigurationFailedCode);
    }

    private static WebhookProcessingResult RetryableFailure(string message, string errorCode) =>
        WebhookProcessingResult.Failure(message, errorCode, retryable: true);

    private static bool IsStaleEvent(SubscriptionRecord subscription, WebhookEvent evt)
    {
        if (evt.EventTimestampUtc == default || subscription.LastEventAtUtc == default)
        {
            return false;
        }

        return NormalizeUtc(evt.EventTimestampUtc) < NormalizeUtc(subscription.LastEventAtUtc);
    }

    private static DateTime ResolveEventTimestampUtc(WebhookEvent evt)
    {
        if (evt.EventTimestampUtc != default)
        {
            return NormalizeUtc(evt.EventTimestampUtc);
        }

        if (evt.ReceivedAtUtc.HasValue && evt.ReceivedAtUtc.Value != default)
        {
            return NormalizeUtc(evt.ReceivedAtUtc.Value);
        }

        return DateTime.UtcNow;
    }

    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static void MarkEventApplied(SubscriptionRecord subscription, DateTime eventAtUtc)
    {
        subscription.LastEventAtUtc = eventAtUtc;
        subscription.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string CreateIdempotencyKey(
        string sideEffect,
        string operation,
        SubscriptionRecord subscription,
        WebhookEvent evt)
    {
        var hasCanonicalOperation = !string.IsNullOrWhiteSpace(evt.EntitlementOperationId);
        var idempotencyOperation = hasCanonicalOperation ? sideEffect : operation;
        var material = string.Concat(
            idempotencyOperation,
            "\n",
            subscription.SubscriptionKey,
            "\n",
            hasCanonicalOperation ? evt.EntitlementOperationId : evt.EventId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return $"subscription-{idempotencyOperation}:{Convert.ToHexString(hash)}";
    }
}
