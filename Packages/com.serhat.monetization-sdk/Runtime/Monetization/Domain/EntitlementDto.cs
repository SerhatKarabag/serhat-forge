using System;
using System.Collections.Generic;

namespace Serhat.Backend.Monetization.Domain
{
    /// <summary>
    /// Entitlement information from the server.
    /// Represents a granted item in PlayFab Economy v2 inventory.
    /// </summary>
    public sealed class EntitlementDto
    {
        /// <summary>Economy item ID in PlayFab catalog.</summary>
        public string ItemId { get; set; } = string.Empty;

        /// <summary>Display name if available.</summary>
        public string? DisplayName { get; set; }

        /// <summary>Store product ID that granted this entitlement.</summary>
        public string? SourceProductId { get; set; }

        /// <summary>Type of the source product.</summary>
        public ProductType ProductType { get; set; }

        /// <summary>For consumables: current quantity owned.</summary>
        public int Quantity { get; set; } = 1;

        /// <summary>When this entitlement was granted.</summary>
        public DateTime GrantedAtUtc { get; set; }

        /// <summary>Transaction ID that granted this entitlement.</summary>
        public string? TransactionId { get; set; }
    }

    /// <summary>
    /// Collection of player entitlements.
    /// </summary>
    public sealed class EntitlementsResponse
    {
        /// <summary>All active entitlements.</summary>
        public List<EntitlementDto> Entitlements { get; set; } = new();

        /// <summary>Active subscription if any.</summary>
        public SubscriptionDto? ActiveSubscription { get; set; }

        /// <summary>Server timestamp for cache validation.</summary>
        public DateTime ServerTimestampUtc { get; set; }
    }
}
