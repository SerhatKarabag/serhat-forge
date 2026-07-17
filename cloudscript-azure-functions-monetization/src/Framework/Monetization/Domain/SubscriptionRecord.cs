using System;

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
    /// <summary>
    /// Unique key for subscription.
    /// Apple: originalTransactionId
    /// Google: purchaseToken hash (first 64 chars)
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
    /// Whether auto-renew is enabled.
    /// </summary>
    public bool AutoRenew { get; set; }

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
        // Use first 64 chars of token as key (tokens can be very long)
        var truncated = purchaseToken.Length > 64
            ? purchaseToken[..64]
            : purchaseToken;
        return $"google:{truncated}";
    }
}
