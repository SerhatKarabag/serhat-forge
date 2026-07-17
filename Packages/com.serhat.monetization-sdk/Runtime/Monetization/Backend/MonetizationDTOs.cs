#nullable enable

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
        private string _platform = string.Empty;
        private string _productId = string.Empty;
        private string _transactionId = string.Empty;
        private string _receiptPayload = string.Empty;
        private string _productType = string.Empty;

        /// <summary>Store platform (apple/google).</summary>
        public string Platform
        {
            get => _platform;
            set => _platform = value ?? string.Empty;
        }

        /// <summary>Store product ID.</summary>
        public string ProductId
        {
            get => _productId;
            set => _productId = value ?? string.Empty;
        }

        /// <summary>Transaction ID from the store.</summary>
        public string TransactionId
        {
            get => _transactionId;
            set => _transactionId = value ?? string.Empty;
        }

        /// <summary>
        /// Google Play purchase token. Empty for Apple, whose authoritative lookup uses
        /// TransactionId and must not transport a raw App Store receipt/JWS.
        /// </summary>
        public string ReceiptPayload
        {
            get => _receiptPayload;
            set => _receiptPayload = value ?? string.Empty;
        }

        /// <summary>Product type hint (consumable/non-consumable/subscription).</summary>
        public string ProductType
        {
            get => _productType;
            set => _productType = value ?? string.Empty;
        }

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
        private List<string> _grantedItemIds = new();

        /// <summary>Whether verification succeeded.</summary>
        public bool Success { get; set; }

        /// <summary>Transaction key used for idempotency.</summary>
        public string? TransactionKey { get; set; }

        /// <summary>Economy item IDs granted.</summary>
        public List<string> GrantedItemIds
        {
            get => _grantedItemIds;
            set => _grantedItemIds = value ?? new List<string>();
        }

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
        /// <summary>Whether to bypass the client cache and fetch current backend state.</summary>
        public bool ForceRefresh { get; set; }
    }

    /// <summary>
    /// Response with player entitlements.
    /// </summary>
    public sealed class GetEntitlementsResponse
    {
        private List<EntitlementDto> _entitlements = new();

        /// <summary>All active entitlements.</summary>
        public List<EntitlementDto> Entitlements
        {
            get => _entitlements;
            set => _entitlements = value ?? new List<EntitlementDto>();
        }

        /// <summary>Active subscription if any.</summary>
        public SubscriptionDto? ActiveSubscription { get; set; }

        /// <summary>Server timestamp.</summary>
        public DateTime ServerTimestampUtc { get; set; }
    }
}
