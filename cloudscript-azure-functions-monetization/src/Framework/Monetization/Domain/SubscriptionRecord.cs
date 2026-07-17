using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Domain;

/// <summary>
/// Subscription status states.
/// </summary>
public enum SubscriptionStatus
{
    None,
    Active,
    GracePeriod,
    Paused,
    Cancelled,
    Expired,
    Refunded,
    Chargeback
}

/// <summary>
/// Durable record of a subscription.
/// Source of truth for subscription state.
/// </summary>
public sealed class SubscriptionRecord
{
    private static readonly ReadOnlyCollection<string> EmptyEconomyItemIds =
        Array.AsReadOnly(Array.Empty<string>());

    private ReadOnlyCollection<string> _activeEconomyItemIds = EmptyEconomyItemIds;

    /// <summary>
    /// Unique key for subscription.
    /// Apple: originalTransactionId
    /// Google: full SHA-256 hash of the purchase token
    /// </summary>
    public string SubscriptionKey { get; set; } = string.Empty;

    /// <summary>
    /// Platform (apple/google).
    /// </summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>
    /// PlayFab player ID.
    /// </summary>
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>
    /// Store product ID.
    /// </summary>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>
    /// Subscription tier key.
    /// </summary>
    public string TierKey { get; set; } = string.Empty;

    /// <summary>
    /// Tier precedence for comparison.
    /// </summary>
    public int TierPrecedence { get; set; }

    /// <summary>
    /// Current subscription status.
    /// </summary>
    public SubscriptionStatus Status { get; set; }

    /// <summary>
    /// Economy item ID currently active for this subscription.
    /// </summary>
    public string? ActiveEconomyItemId { get; set; }

    /// <summary>
    /// Immutable snapshot of every Economy item currently granted by this subscription.
    /// Records written before this field existed transparently fall back to
    /// <see cref="ActiveEconomyItemId"/>.
    /// </summary>
    public IReadOnlyList<string> ActiveEconomyItemIds
    {
        get
        {
            var legacyItemId = ActiveEconomyItemId;
            if (_activeEconomyItemIds.Count > 0 || string.IsNullOrWhiteSpace(legacyItemId))
            {
                return _activeEconomyItemIds;
            }

            return Array.AsReadOnly(new[] { legacyItemId });
        }
    }

    /// <summary>
    /// Replaces the granted-item snapshot. Input collections are copied, normalized,
    /// de-duplicated, and never exposed as mutable storage.
    /// </summary>
    public void SetActiveEconomyItemIds(IEnumerable<string>? itemIds)
    {
        if (itemIds == null)
        {
            _activeEconomyItemIds = EmptyEconomyItemIds;
            ActiveEconomyItemId = null;
            return;
        }

        var uniqueItemIds = new HashSet<string>(StringComparer.Ordinal);
        var snapshot = new List<string>();
        foreach (var itemId in itemIds)
        {
            if (string.IsNullOrWhiteSpace(itemId) || !uniqueItemIds.Add(itemId))
            {
                continue;
            }

            snapshot.Add(itemId);
        }

        _activeEconomyItemIds = snapshot.Count == 0
            ? EmptyEconomyItemIds
            : snapshot.AsReadOnly();
        ActiveEconomyItemId = snapshot.Count > 0 ? snapshot[0] : null;
    }

    /// <summary>
    /// Whether auto-renew is enabled.
    /// </summary>
    public bool AutoRenew { get; set; }

    /// <summary>
    /// Latest non-sensitive store order identifier observed during authoritative verification.
    /// Purchase tokens and receipts must never be stored here.
    /// </summary>
    public string? LatestStoreOrderId { get; set; }

    /// <summary>
    /// Whether the latest authoritative store snapshot represents a test/sandbox purchase.
    /// </summary>
    public bool IsSandbox { get; set; }

    /// <summary>
    /// Start of current billing period.
    /// </summary>
    public DateTime PeriodStartUtc { get; set; }

    /// <summary>
    /// End of current billing period.
    /// </summary>
    public DateTime PeriodEndUtc { get; set; }

    /// <summary>
    /// Original purchase date.
    /// </summary>
    public DateTime OriginalPurchaseDateUtc { get; set; }

    /// <summary>
    /// Last event timestamp.
    /// </summary>
    public DateTime LastEventAtUtc { get; set; }

    /// <summary>
    /// Scheduled tier change (for downgrades at next renewal).
    /// </summary>
    public string? PendingTierKey { get; set; }

    /// <summary>
    /// Scheduled product change.
    /// </summary>
    public string? PendingProductId { get; set; }

    /// <summary>
    /// Grace period end if applicable.
    /// </summary>
    public DateTime? GracePeriodEndUtc { get; set; }

    /// <summary>
    /// When the record was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// When the record was last updated.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// Whether the subscription currently provides benefits.
    /// </summary>
    public bool IsActive =>
        Status == SubscriptionStatus.Active ||
        Status == SubscriptionStatus.GracePeriod ||
        (Status == SubscriptionStatus.Cancelled && PeriodEndUtc > DateTime.UtcNow);

    /// <summary>
    /// Creates a detached copy suitable for side-effect-first lifecycle updates.
    /// </summary>
    public SubscriptionRecord Copy()
    {
        var copy = new SubscriptionRecord
        {
            SubscriptionKey = SubscriptionKey,
            Platform = Platform,
            PlayerId = PlayerId,
            ProductId = ProductId,
            TierKey = TierKey,
            TierPrecedence = TierPrecedence,
            Status = Status,
            ActiveEconomyItemId = ActiveEconomyItemId,
            AutoRenew = AutoRenew,
            LatestStoreOrderId = LatestStoreOrderId,
            IsSandbox = IsSandbox,
            PeriodStartUtc = PeriodStartUtc,
            PeriodEndUtc = PeriodEndUtc,
            OriginalPurchaseDateUtc = OriginalPurchaseDateUtc,
            LastEventAtUtc = LastEventAtUtc,
            PendingTierKey = PendingTierKey,
            PendingProductId = PendingProductId,
            GracePeriodEndUtc = GracePeriodEndUtc,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = UpdatedAtUtc
        };
        copy.SetActiveEconomyItemIds(ActiveEconomyItemIds);
        return copy;
    }

    /// <summary>
    /// Creates a subscription key for Apple.
    /// </summary>
    public static string CreateAppleKey(string originalTransactionId)
    {
        return $"apple:{originalTransactionId}";
    }

    /// <summary>
    /// Creates a subscription key for Google.
    /// </summary>
    public static string CreateGoogleKey(string purchaseToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purchaseToken);

        // Purchase tokens are bearer-like credentials. Keep the lookup deterministic
        // without persisting the raw token as part of the subscription identifier.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(purchaseToken));
        return $"google:{Convert.ToHexString(hash)}";
    }
}
