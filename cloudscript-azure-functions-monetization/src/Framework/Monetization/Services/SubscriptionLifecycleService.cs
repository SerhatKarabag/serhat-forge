using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;
using Serhat.Forge.CloudScript.Infrastructure.Security;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Services;

/// <summary>
/// Service for handling subscription lifecycle events from webhooks.
/// </summary>
public sealed class SubscriptionLifecycleService
{
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
            return WebhookProcessingResult.Failure("Webhook event ID is required", "MISSING_EVENT_ID");
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
            if (string.IsNullOrEmpty(webhookEvent.SubscriptionKey))
            {
                _logger.LogWarning("Webhook has no subscription identity: EventToken={EventToken}", eventToken);
                await _repository.CompleteWebhookProcessingAsync(webhookEvent.EventId, ct);
                return WebhookProcessingResult.Success();
            }

            var subscription = await _repository.GetSubscriptionAsync(webhookEvent.SubscriptionKey, ct);
            if (subscription == null)
            {
                _logger.LogWarning(
                    "Webhook subscription was not found: SubscriptionToken={SubscriptionToken}",
                    subscriptionToken);
                await _repository.CompleteWebhookProcessingAsync(webhookEvent.EventId, ct);
                return WebhookProcessingResult.Success();
            }

            var result = webhookEvent.EventType switch
            {
                WebhookEventType.Renewed => await HandleRenewalAsync(subscription, webhookEvent, ct),
                WebhookEventType.Cancelled => await HandleCancellationAsync(subscription, webhookEvent, ct),
                WebhookEventType.Expired => await HandleExpirationAsync(subscription, webhookEvent, ct),
                WebhookEventType.Refunded => await HandleRefundAsync(subscription, webhookEvent, ct),
                WebhookEventType.Chargeback => await HandleChargebackAsync(subscription, webhookEvent, ct),
                WebhookEventType.GracePeriodStarted => await HandleGracePeriodAsync(subscription, webhookEvent, ct),
                WebhookEventType.GracePeriodEnded => await HandleGracePeriodEndedAsync(subscription, webhookEvent, ct),
                WebhookEventType.Paused => await HandlePausedAsync(subscription, webhookEvent, ct),
                WebhookEventType.Resumed => await HandleResumedAsync(subscription, webhookEvent, ct),
                WebhookEventType.UpgradeDowngrade => await HandleTierChangeAsync(subscription, webhookEvent, ct),
                WebhookEventType.Revoked => await HandleRevokedAsync(subscription, webhookEvent, ct),
                _ => WebhookProcessingResult.Success()
            };

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
        _logger.LogInformation("Processing renewal for {SubscriptionToken}", SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));

        subscription.Status = SubscriptionStatus.Active;
        subscription.AutoRenew = true;
        subscription.PeriodStartUtc = evt.PeriodStartUtc ?? DateTime.UtcNow;
        subscription.PeriodEndUtc = evt.PeriodEndUtc ?? DateTime.UtcNow.AddMonths(1);
        subscription.GracePeriodEndUtc = null;
        subscription.LastEventAtUtc = DateTime.UtcNow;
        subscription.UpdatedAtUtc = DateTime.UtcNow;

        // Apply pending tier change if any
        if (!string.IsNullOrEmpty(subscription.PendingTierKey))
        {
            await ApplyTierChangeAsync(subscription, subscription.PendingTierKey, ct);
            subscription.PendingTierKey = null;
            subscription.PendingProductId = null;
        }

        await _repository.UpdateSubscriptionAsync(subscription, ct);

        return WebhookProcessingResult.Success();
    }

    private async Task<WebhookProcessingResult> HandleCancellationAsync(
        SubscriptionRecord subscription,
        WebhookEvent evt,
        CancellationToken ct)
    {
        _logger.LogInformation("Processing cancellation for {SubscriptionToken}", SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));

        subscription.Status = SubscriptionStatus.Cancelled;
        subscription.AutoRenew = false;
        subscription.LastEventAtUtc = DateTime.UtcNow;
        subscription.UpdatedAtUtc = DateTime.UtcNow;

        await _repository.UpdateSubscriptionAsync(subscription, ct);

        // Note: Don't revoke entitlements until period ends
        // The IsActive property will handle this

        return WebhookProcessingResult.Success();
    }

    private async Task<WebhookProcessingResult> HandleExpirationAsync(
        SubscriptionRecord subscription,
        WebhookEvent evt,
        CancellationToken ct)
    {
        _logger.LogInformation("Processing expiration for {SubscriptionToken}", SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));

        subscription.Status = SubscriptionStatus.Expired;
        subscription.AutoRenew = false;
        subscription.LastEventAtUtc = DateTime.UtcNow;
        subscription.UpdatedAtUtc = DateTime.UtcNow;

        await _repository.UpdateSubscriptionAsync(subscription, ct);

        // Revoke entitlements
        if (!string.IsNullOrEmpty(subscription.ActiveEconomyItemId))
        {
            var revokeRequest = new RevokeRequest
            {
                PlayerId = subscription.PlayerId,
                ItemIds = new System.Collections.Generic.List<string> { subscription.ActiveEconomyItemId },
                IdempotencyKey = $"revoke:{subscription.SubscriptionKey}:{DateTime.UtcNow:yyyyMMdd}"
            };

            await _granter.RevokeItemsAsync(revokeRequest, ct);
        }

        return WebhookProcessingResult.Success();
    }

    private async Task<WebhookProcessingResult> HandleRefundAsync(
        SubscriptionRecord subscription,
        WebhookEvent evt,
        CancellationToken ct)
    {
        _logger.LogInformation("Processing refund for {SubscriptionToken}", SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));

        subscription.Status = SubscriptionStatus.Refunded;
        subscription.AutoRenew = false;
        subscription.LastEventAtUtc = DateTime.UtcNow;
        subscription.UpdatedAtUtc = DateTime.UtcNow;

        await _repository.UpdateSubscriptionAsync(subscription, ct);

        // Revoke entitlements immediately on refund
        if (!string.IsNullOrEmpty(subscription.ActiveEconomyItemId))
        {
            var revokeRequest = new RevokeRequest
            {
                PlayerId = subscription.PlayerId,
                ItemIds = new System.Collections.Generic.List<string> { subscription.ActiveEconomyItemId },
                IdempotencyKey = $"refund:{subscription.SubscriptionKey}:{evt.EventId}"
            };

            await _granter.RevokeItemsAsync(revokeRequest, ct);
        }

        return WebhookProcessingResult.Success();
    }

    private async Task<WebhookProcessingResult> HandleChargebackAsync(
        SubscriptionRecord subscription,
        WebhookEvent evt,
        CancellationToken ct)
    {
        _logger.LogWarning("Processing chargeback for {SubscriptionToken}", SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));

        subscription.Status = SubscriptionStatus.Chargeback;
        subscription.AutoRenew = false;
        subscription.LastEventAtUtc = DateTime.UtcNow;
        subscription.UpdatedAtUtc = DateTime.UtcNow;

        await _repository.UpdateSubscriptionAsync(subscription, ct);

        // Revoke entitlements immediately on chargeback
        if (!string.IsNullOrEmpty(subscription.ActiveEconomyItemId))
        {
            var revokeRequest = new RevokeRequest
            {
                PlayerId = subscription.PlayerId,
                ItemIds = new System.Collections.Generic.List<string> { subscription.ActiveEconomyItemId },
                IdempotencyKey = $"chargeback:{subscription.SubscriptionKey}:{evt.EventId}"
            };

            await _granter.RevokeItemsAsync(revokeRequest, ct);
        }

        return WebhookProcessingResult.Success();
    }

    private async Task<WebhookProcessingResult> HandleGracePeriodAsync(
        SubscriptionRecord subscription,
        WebhookEvent evt,
        CancellationToken ct)
    {
        _logger.LogInformation("Processing grace period start for {SubscriptionToken}", SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));

        subscription.Status = SubscriptionStatus.GracePeriod;
        subscription.GracePeriodEndUtc = evt.GracePeriodEndUtc;
        subscription.LastEventAtUtc = DateTime.UtcNow;
        subscription.UpdatedAtUtc = DateTime.UtcNow;

        await _repository.UpdateSubscriptionAsync(subscription, ct);

        // Keep entitlements active during grace period

        return WebhookProcessingResult.Success();
    }

    private async Task<WebhookProcessingResult> HandleGracePeriodEndedAsync(
        SubscriptionRecord subscription,
        WebhookEvent evt,
        CancellationToken ct)
    {
        _logger.LogInformation("Processing grace period end for {SubscriptionToken}", SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));

        // If still in grace period status, payment failed - expire it
        if (subscription.Status == SubscriptionStatus.GracePeriod)
        {
            subscription.Status = SubscriptionStatus.Expired;
            subscription.GracePeriodEndUtc = null;
            subscription.LastEventAtUtc = DateTime.UtcNow;
            subscription.UpdatedAtUtc = DateTime.UtcNow;

            await _repository.UpdateSubscriptionAsync(subscription, ct);

            // Revoke entitlements
            if (!string.IsNullOrEmpty(subscription.ActiveEconomyItemId))
            {
                var revokeRequest = new RevokeRequest
                {
                    PlayerId = subscription.PlayerId,
                    ItemIds = new System.Collections.Generic.List<string> { subscription.ActiveEconomyItemId },
                    IdempotencyKey = $"grace-end:{subscription.SubscriptionKey}:{evt.EventId}"
                };

                await _granter.RevokeItemsAsync(revokeRequest, ct);
            }
        }

        return WebhookProcessingResult.Success();
    }

    private async Task<WebhookProcessingResult> HandlePausedAsync(
        SubscriptionRecord subscription,
        WebhookEvent evt,
        CancellationToken ct)
    {
        _logger.LogInformation("Processing pause for {SubscriptionToken}", SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));

        subscription.Status = SubscriptionStatus.Paused;
        subscription.LastEventAtUtc = DateTime.UtcNow;
        subscription.UpdatedAtUtc = DateTime.UtcNow;

        await _repository.UpdateSubscriptionAsync(subscription, ct);

        // Revoke entitlements while paused
        if (!string.IsNullOrEmpty(subscription.ActiveEconomyItemId))
        {
            var revokeRequest = new RevokeRequest
            {
                PlayerId = subscription.PlayerId,
                ItemIds = new System.Collections.Generic.List<string> { subscription.ActiveEconomyItemId },
                IdempotencyKey = $"pause:{subscription.SubscriptionKey}:{evt.EventId}"
            };

            await _granter.RevokeItemsAsync(revokeRequest, ct);
        }

        return WebhookProcessingResult.Success();
    }

    private async Task<WebhookProcessingResult> HandleResumedAsync(
        SubscriptionRecord subscription,
        WebhookEvent evt,
        CancellationToken ct)
    {
        _logger.LogInformation("Processing resume for {SubscriptionToken}", SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));

        subscription.Status = SubscriptionStatus.Active;
        subscription.PeriodStartUtc = evt.PeriodStartUtc ?? DateTime.UtcNow;
        subscription.PeriodEndUtc = evt.PeriodEndUtc ?? DateTime.UtcNow.AddMonths(1);
        subscription.LastEventAtUtc = DateTime.UtcNow;
        subscription.UpdatedAtUtc = DateTime.UtcNow;

        await _repository.UpdateSubscriptionAsync(subscription, ct);

        // Re-grant entitlements
        var productConfig = _productConfig.GetProduct(subscription.ProductId);
        if (productConfig != null && productConfig.EconomyItemIds.Count > 0)
        {
            var grantRequest = new GrantRequest
            {
                PlayerId = subscription.PlayerId,
                ItemIds = productConfig.EconomyItemIds,
                IdempotencyKey = $"resume:{subscription.SubscriptionKey}:{evt.EventId}"
            };

            await _granter.GrantItemsAsync(grantRequest, ct);
        }

        return WebhookProcessingResult.Success();
    }

    private async Task<WebhookProcessingResult> HandleTierChangeAsync(
        SubscriptionRecord subscription,
        WebhookEvent evt,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing tier change for {SubscriptionToken}: {OldTier} -> {NewTier}",
            SensitiveLogValue.Fingerprint(subscription.SubscriptionKey), subscription.TierKey, evt.NewTierKey);

        if (string.IsNullOrEmpty(evt.NewTierKey))
        {
            return WebhookProcessingResult.Success();
        }

        var newProductConfig = _productConfig.GetProduct(evt.NewProductId ?? subscription.ProductId);
        if (newProductConfig == null)
        {
            _logger.LogWarning("New product not found: {ProductId}", evt.NewProductId);
            return WebhookProcessingResult.Success();
        }

        var isUpgrade = newProductConfig.TierPrecedence > subscription.TierPrecedence;

        if (isUpgrade)
        {
            // Upgrades take effect immediately
            await ApplyTierChangeAsync(subscription, evt.NewTierKey, ct);
            subscription.TierKey = evt.NewTierKey;
            subscription.TierPrecedence = newProductConfig.TierPrecedence;
            if (!string.IsNullOrEmpty(evt.NewProductId))
            {
                subscription.ProductId = evt.NewProductId;
            }
        }
        else
        {
            // Downgrades take effect at next renewal
            subscription.PendingTierKey = evt.NewTierKey;
            subscription.PendingProductId = evt.NewProductId;
        }

        subscription.LastEventAtUtc = DateTime.UtcNow;
        subscription.UpdatedAtUtc = DateTime.UtcNow;

        await _repository.UpdateSubscriptionAsync(subscription, ct);

        return WebhookProcessingResult.Success();
    }

    private async Task<WebhookProcessingResult> HandleRevokedAsync(
        SubscriptionRecord subscription,
        WebhookEvent evt,
        CancellationToken ct)
    {
        _logger.LogWarning("Processing revocation for {SubscriptionToken}", SensitiveLogValue.Fingerprint(subscription.SubscriptionKey));

        subscription.Status = SubscriptionStatus.Expired;
        subscription.AutoRenew = false;
        subscription.LastEventAtUtc = DateTime.UtcNow;
        subscription.UpdatedAtUtc = DateTime.UtcNow;

        await _repository.UpdateSubscriptionAsync(subscription, ct);

        // Revoke entitlements
        if (!string.IsNullOrEmpty(subscription.ActiveEconomyItemId))
        {
            var revokeRequest = new RevokeRequest
            {
                PlayerId = subscription.PlayerId,
                ItemIds = new System.Collections.Generic.List<string> { subscription.ActiveEconomyItemId },
                IdempotencyKey = $"revoked:{subscription.SubscriptionKey}:{evt.EventId}"
            };

            await _granter.RevokeItemsAsync(revokeRequest, ct);
        }

        return WebhookProcessingResult.Success();
    }

    private async Task ApplyTierChangeAsync(
        SubscriptionRecord subscription,
        string newTierKey,
        CancellationToken ct)
    {
        // Find the product for the new tier
        ProductConfig? newProductConfig = null;
        foreach (var kvp in _productConfig.Products)
        {
            if (kvp.Value.TierKey == newTierKey && kvp.Value.IsSubscription)
            {
                newProductConfig = kvp.Value;
                break;
            }
        }

        if (newProductConfig == null)
        {
            _logger.LogWarning("Product config not found for tier: {TierKey}", newTierKey);
            return;
        }

        // Revoke old entitlement
        if (!string.IsNullOrEmpty(subscription.ActiveEconomyItemId))
        {
            var revokeRequest = new RevokeRequest
            {
                PlayerId = subscription.PlayerId,
                ItemIds = new System.Collections.Generic.List<string> { subscription.ActiveEconomyItemId },
                IdempotencyKey = $"tier-change-revoke:{subscription.SubscriptionKey}:{newTierKey}"
            };

            await _granter.RevokeItemsAsync(revokeRequest, ct);
        }

        // Grant new entitlement
        if (newProductConfig.EconomyItemIds.Count > 0)
        {
            var grantRequest = new GrantRequest
            {
                PlayerId = subscription.PlayerId,
                ItemIds = newProductConfig.EconomyItemIds,
                IdempotencyKey = $"tier-change-grant:{subscription.SubscriptionKey}:{newTierKey}"
            };

            await _granter.GrantItemsAsync(grantRequest, ct);

            subscription.ActiveEconomyItemId = newProductConfig.EconomyItemIds[0];
        }
    }
}
