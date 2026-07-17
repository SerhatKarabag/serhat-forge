#nullable enable

using Serhat.Backend.Monetization.Domain;

namespace Serhat.Backend.Monetization.Abstractions
{
    /// <summary>
    /// Defines tier precedence and intended upgrade/downgrade policy for subscriptions.
    /// The base purchase service fails closed for active-subscription replacement; games that
    /// implement the complete store/backend replacement lifecycle can reuse this policy there.
    /// </summary>
    public interface ITierPolicy
    {
        /// <summary>
        /// Gets the precedence level for a tier (higher = better tier).
        /// </summary>
        /// <param name="tierKey">Tier key.</param>
        /// <returns>Precedence value. Higher values indicate better tiers.</returns>
        int GetTierPrecedence(string tierKey);

        /// <summary>
        /// Compares two tiers and returns the change type and policy.
        /// </summary>
        /// <param name="fromTierKey">Current tier key (null if no subscription).</param>
        /// <param name="toTierKey">Target tier key.</param>
        /// <returns>Tier change result with policy.</returns>
        TierChangeResult CompareTiers(string? fromTierKey, string toTierKey);

        /// <summary>
        /// Gets the intended policy for a project-specific upgrade implementation.
        /// Default: Immediate (user gets benefits right away).
        /// </summary>
        TierChangePolicy UpgradePolicy { get; }

        /// <summary>
        /// Gets the intended policy for a project-specific downgrade implementation.
        /// Default: NextRenewal (user keeps current tier until period ends).
        /// </summary>
        TierChangePolicy DowngradePolicy { get; }

        /// <summary>
        /// Validates if a tier transition is allowed.
        /// </summary>
        /// <param name="fromTierKey">Current tier key.</param>
        /// <param name="toTierKey">Target tier key.</param>
        /// <returns>True if transition is allowed.</returns>
        bool IsTransitionAllowed(string? fromTierKey, string toTierKey);

        /// <summary>
        /// Gets the display name for a tier.
        /// </summary>
        /// <param name="tierKey">Tier key.</param>
        /// <returns>Display name or tier key if not found.</returns>
        string GetTierDisplayName(string tierKey);
    }
}
