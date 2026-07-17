using System;

namespace Serhat.Backend.Monetization.Domain
{
    /// <summary>
    /// Subscription status states.
    /// </summary>
    public enum SubscriptionStatus
    {
        /// <summary>No active subscription.</summary>
        None,

        /// <summary>Subscription is active and in good standing.</summary>
        Active,

        /// <summary>Payment failed but in grace period.</summary>
        GracePeriod,

        /// <summary>Subscription is paused (Google Play feature).</summary>
        Paused,

        /// <summary>User cancelled but still active until period end.</summary>
        Cancelled,

        /// <summary>Subscription has expired.</summary>
        Expired,

        /// <summary>Refunded by store.</summary>
        Refunded,

        /// <summary>Chargeback occurred.</summary>
        Chargeback
    }

    /// <summary>
    /// Subscription information from the server.
    /// </summary>
    public sealed class SubscriptionDto
    {
        /// <summary>Subscription product ID.</summary>
        public string ProductId { get; set; } = string.Empty;

        /// <summary>Tier key for this subscription level.</summary>
        public string TierKey { get; set; } = string.Empty;

        /// <summary>Current subscription status.</summary>
        public SubscriptionStatus Status { get; set; }

        /// <summary>Whether auto-renew is enabled.</summary>
        public bool AutoRenew { get; set; }

        /// <summary>Start of current billing period.</summary>
        public DateTime PeriodStartUtc { get; set; }

        /// <summary>End of current billing period (expiration if not renewed).</summary>
        public DateTime PeriodEndUtc { get; set; }

        /// <summary>Original purchase date.</summary>
        public DateTime OriginalPurchaseDateUtc { get; set; }

        /// <summary>Platform where subscription was purchased (apple/google).</summary>
        public string Platform { get; set; } = string.Empty;

        /// <summary>Economy item ID granted for this subscription tier.</summary>
        public string? GrantedItemId { get; set; }

        /// <summary>Days remaining in grace period (if applicable).</summary>
        public int? GracePeriodDaysRemaining { get; set; }

        /// <summary>Whether the subscription is currently providing benefits.</summary>
        public bool IsActive =>
            Status == SubscriptionStatus.Active ||
            Status == SubscriptionStatus.GracePeriod ||
            Status == SubscriptionStatus.Cancelled;

        /// <summary>Whether the subscription will renew.</summary>
        public bool WillRenew =>
            AutoRenew &&
            Status != SubscriptionStatus.Cancelled &&
            Status != SubscriptionStatus.Expired &&
            Status != SubscriptionStatus.Refunded &&
            Status != SubscriptionStatus.Chargeback;
    }
}
