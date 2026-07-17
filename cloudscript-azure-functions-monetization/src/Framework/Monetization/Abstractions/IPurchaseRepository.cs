using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;

/// <summary>
/// Repository for purchase and subscription records.
/// </summary>
public interface IPurchaseRepository
{
    #region Purchase Records

    /// <summary>
    /// Gets a purchase record by transaction key.
    /// </summary>
    Task<PurchaseRecord?> GetPurchaseAsync(string transactionKey, CancellationToken ct = default);

    /// <summary>
    /// Creates a new purchase record.
    /// </summary>
    Task<bool> CreatePurchaseAsync(PurchaseRecord record, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing purchase record.
    /// </summary>
    Task<bool> UpdatePurchaseAsync(PurchaseRecord record, CancellationToken ct = default);

    /// <summary>
    /// Gets all purchases for a player.
    /// </summary>
    Task<IReadOnlyList<PurchaseRecord>> GetPurchasesByPlayerAsync(
        string playerId,
        CancellationToken ct = default);

    #endregion

    #region Subscription Records

    /// <summary>
    /// Gets a subscription record by key.
    /// </summary>
    Task<SubscriptionRecord?> GetSubscriptionAsync(string subscriptionKey, CancellationToken ct = default);

    /// <summary>
    /// Gets the active subscription for a player.
    /// </summary>
    Task<SubscriptionRecord?> GetActiveSubscriptionAsync(string playerId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new subscription record.
    /// </summary>
    Task<bool> CreateSubscriptionAsync(SubscriptionRecord record, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing subscription record.
    /// </summary>
    Task<bool> UpdateSubscriptionAsync(SubscriptionRecord record, CancellationToken ct = default);

    /// <summary>
    /// Gets all subscriptions for a player (including inactive).
    /// </summary>
    Task<IReadOnlyList<SubscriptionRecord>> GetSubscriptionsByPlayerAsync(
        string playerId,
        CancellationToken ct = default);

    #endregion

    #region Webhook Dedup

    /// <summary>
    /// Atomically claims a webhook for processing. Returns false when another worker has
    /// already claimed or completed the same provider event ID.
    /// </summary>
    Task<bool> TryBeginWebhookProcessingAsync(string eventId, CancellationToken ct = default);

    /// <summary>
    /// Marks a claimed webhook as completed.
    /// </summary>
    Task CompleteWebhookProcessingAsync(string eventId, CancellationToken ct = default);

    /// <summary>
    /// Releases a claim after a retryable failure so the provider retry can process it.
    /// </summary>
    Task AbandonWebhookProcessingAsync(string eventId, CancellationToken ct = default);

    /// <summary>
    /// Checks if a webhook event has been processed.
    /// </summary>
    Task<bool> HasProcessedWebhookAsync(string eventId, CancellationToken ct = default);

    /// <summary>
    /// Marks a webhook event as processed.
    /// </summary>
    Task MarkWebhookProcessedAsync(string eventId, CancellationToken ct = default);

    #endregion
}
