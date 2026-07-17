using System;
using System.Collections.Generic;
using Serhat.Backend.Monetization.Domain;

namespace Serhat.Backend.Monetization.Backend
{
    /// <summary>
    /// Request to verify a purchase on the server.
    /// </summary>
    public sealed class VerifyPurchaseRequest
    {
        /// <summary>Store platform (apple/google).</summary>
        public string Platform { get; set; } = string.Empty;

        /// <summary>Store product ID.</summary>
        public string ProductId { get; set; } = string.Empty;

        /// <summary>Transaction ID from the store.</summary>
        public string TransactionId { get; set; } = string.Empty;

        /// <summary>Receipt payload for verification.</summary>
        public string ReceiptPayload { get; set; } = string.Empty;

        /// <summary>Product type hint (consumable/non-consumable/subscription).</summary>
        public string ProductType { get; set; } = string.Empty;

        /// <summary>For subscriptions: tier key.</summary>
        public string? TierKey { get; set; }

        /// <summary>Additional metadata (package name, etc.).</summary>
        public Dictionary<string, string>? Metadata { get; set; }
    }

    /// <summary>
    /// Response from purchase verification.
    /// </summary>
    public sealed class VerifyPurchaseResponse
    {
        /// <summary>Whether verification succeeded.</summary>
        public bool Success { get; set; }

        /// <summary>Transaction key used for idempotency.</summary>
        public string? TransactionKey { get; set; }

        /// <summary>Economy item IDs granted.</summary>
        public List<string> GrantedItemIds { get; set; } = new();

        /// <summary>For subscriptions: updated subscription state.</summary>
        public SubscriptionDto? Subscription { get; set; }

        /// <summary>Error code if failed.</summary>
        public string? ErrorCode { get; set; }

        /// <summary>Error message if failed.</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>Whether this was a duplicate request (idempotent).</summary>
        public bool WasDuplicate { get; set; }
    }

    /// <summary>
    /// Request to get player entitlements.
    /// </summary>
    public sealed class GetEntitlementsRequest
    {
        /// <summary>Whether to force refresh from store (re-verify active subscriptions).</summary>
        public bool ForceRefresh { get; set; }
    }

    /// <summary>
    /// Response with player entitlements.
    /// </summary>
    public sealed class GetEntitlementsResponse
    {
        /// <summary>All active entitlements.</summary>
        public List<EntitlementDto> Entitlements { get; set; } = new();

        /// <summary>Active subscription if any.</summary>
        public SubscriptionDto? ActiveSubscription { get; set; }

        /// <summary>Server timestamp.</summary>
        public DateTime ServerTimestampUtc { get; set; }
    }
}
