#nullable enable

using System.Collections.Generic;
using Serhat.Backend.Monetization.Abstractions;
using Serhat.Backend.Monetization.Domain;

namespace Serhat.Backend.Monetization.Samples
{
    /// <summary>
    /// Example implementation of ITierPolicy.
    /// Each game should implement this interface with its own tier rules. The base purchase
    /// service does not execute active-subscription replacement; these values are inputs to a
    /// project-specific store and backend replacement lifecycle.
    /// </summary>
    /// <remarks>
    /// This example defines a simple 2-tier system:
    /// - premium: Basic subscription tier (precedence 1)
    /// - pro: Advanced subscription tier (precedence 2)
    ///
    /// Rules:
    /// - Upgrades (premium -> pro): Immediate
    /// - Downgrades (pro -> premium): Next renewal
    /// - Cross-grades: Not allowed in this example
    /// </remarks>
    public sealed class ExampleTierPolicy : ITierPolicy
    {
        private readonly Dictionary<string, int> _tierPrecedence;
        private readonly Dictionary<string, string> _tierDisplayNames;

        public TierChangePolicy UpgradePolicy => TierChangePolicy.Immediate;
        public TierChangePolicy DowngradePolicy => TierChangePolicy.NextRenewal;

        public ExampleTierPolicy()
        {
            _tierPrecedence = new Dictionary<string, int>
            {
                [ExampleProductCatalog.TierPremium] = 1,
                [ExampleProductCatalog.TierPro] = 2
            };

            _tierDisplayNames = new Dictionary<string, string>
            {
                [ExampleProductCatalog.TierPremium] = "Premium",
                [ExampleProductCatalog.TierPro] = "Pro"
            };
        }

        public int GetTierPrecedence(string tierKey)
        {
            return _tierPrecedence.TryGetValue(tierKey, out var precedence) ? precedence : 0;
        }

        public TierChangeResult CompareTiers(string? fromTierKey, string toTierKey)
        {
            // No current subscription - this is a new subscription
            if (string.IsNullOrEmpty(fromTierKey))
            {
                return new TierChangeResult(
                    TierChangeType.NoChange,
                    TierChangePolicy.Immediate,
                    fromTierKey ?? "",
                    toTierKey);
            }

            // Same tier
            if (fromTierKey == toTierKey)
            {
                return new TierChangeResult(
                    TierChangeType.NoChange,
                    TierChangePolicy.Immediate,
                    fromTierKey,
                    toTierKey);
            }

            var fromPrecedence = GetTierPrecedence(fromTierKey);
            var toPrecedence = GetTierPrecedence(toTierKey);

            if (toPrecedence > fromPrecedence)
            {
                // Upgrade
                return new TierChangeResult(
                    TierChangeType.Upgrade,
                    UpgradePolicy,
                    fromTierKey,
                    toTierKey);
            }
            else if (toPrecedence < fromPrecedence)
            {
                // Downgrade
                return new TierChangeResult(
                    TierChangeType.Downgrade,
                    DowngradePolicy,
                    fromTierKey,
                    toTierKey);
            }
            else
            {
                // Same precedence but different tier = cross-grade
                return new TierChangeResult(
                    TierChangeType.CrossGrade,
                    TierChangePolicy.NextRenewal,
                    fromTierKey,
                    toTierKey);
            }
        }

        public bool IsTransitionAllowed(string? fromTierKey, string toTierKey)
        {
            // Allow new subscriptions
            if (string.IsNullOrEmpty(fromTierKey))
            {
                return true;
            }

            // Allow same tier (renewal)
            if (fromTierKey == toTierKey)
            {
                return true;
            }

            // Allow upgrades and downgrades
            var tierChange = CompareTiers(fromTierKey, toTierKey);
            return tierChange.ChangeType == TierChangeType.Upgrade ||
                   tierChange.ChangeType == TierChangeType.Downgrade;

            // Disallow cross-grades in this example
            // (you might allow them in your implementation)
        }

        public string GetTierDisplayName(string tierKey)
        {
            return _tierDisplayNames.TryGetValue(tierKey, out var name) ? name : tierKey;
        }
    }
}
