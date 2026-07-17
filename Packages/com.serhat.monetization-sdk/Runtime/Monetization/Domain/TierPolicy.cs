namespace Serhat.Backend.Monetization.Domain
{
    /// <summary>
    /// Policy for handling tier changes in subscriptions.
    /// </summary>
    public enum TierChangePolicy
    {
        /// <summary>Change takes effect immediately.</summary>
        Immediate,

        /// <summary>Change takes effect at next renewal.</summary>
        NextRenewal
    }

    /// <summary>
    /// Result of tier comparison.
    /// </summary>
    public enum TierChangeType
    {
        /// <summary>Same tier, no change.</summary>
        NoChange,

        /// <summary>Moving to a higher tier.</summary>
        Upgrade,

        /// <summary>Moving to a lower tier.</summary>
        Downgrade,

        /// <summary>Cross-grade (different tier at same level).</summary>
        CrossGrade
    }

    /// <summary>
    /// Tier comparison result with policy.
    /// </summary>
    public sealed class TierChangeResult
    {
        public TierChangeType ChangeType { get; }
        public TierChangePolicy Policy { get; }
        public string FromTierKey { get; }
        public string ToTierKey { get; }

        public TierChangeResult(
            TierChangeType changeType,
            TierChangePolicy policy,
            string fromTierKey,
            string toTierKey)
        {
            ChangeType = changeType;
            Policy = policy;
            FromTierKey = fromTierKey;
            ToTierKey = toTierKey;
        }

        public bool RequiresConfirmation =>
            ChangeType == TierChangeType.Downgrade ||
            ChangeType == TierChangeType.CrossGrade;
    }
}
