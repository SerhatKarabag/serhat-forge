using System.Collections.Generic;

namespace Serhat.Backend.Monetization.Domain
{
    /// <summary>
    /// Result of a purchase operation.
    /// </summary>
    public sealed class PurchaseResult
    {
        public bool IsSuccess { get; }
        public PurchaseError? Error { get; }

        /// <summary>Product ID that was purchased.</summary>
        public string ProductId { get; }

        /// <summary>Transaction identifier from the store.</summary>
        public string? TransactionId { get; }

        /// <summary>Economy item IDs granted by the server.</summary>
        public IReadOnlyList<string> GrantedItemIds { get; }

        /// <summary>For subscriptions: the active tier key.</summary>
        public string? TierKey { get; }

        /// <summary>Whether this was a restored purchase.</summary>
        public bool WasRestored { get; }

        private PurchaseResult(
            bool isSuccess,
            string productId,
            string? transactionId,
            IReadOnlyList<string>? grantedItemIds,
            string? tierKey,
            bool wasRestored,
            PurchaseError? error)
        {
            IsSuccess = isSuccess;
            ProductId = productId;
            TransactionId = transactionId;
            GrantedItemIds = grantedItemIds ?? System.Array.Empty<string>();
            TierKey = tierKey;
            WasRestored = wasRestored;
            Error = error;
        }

        public static PurchaseResult Success(
            string productId,
            string transactionId,
            IReadOnlyList<string> grantedItemIds,
            string? tierKey = null,
            bool wasRestored = false) =>
            new(true, productId, transactionId, grantedItemIds, tierKey, wasRestored, null);

        public static PurchaseResult Failure(string productId, PurchaseError error) =>
            new(false, productId, null, null, null, false, error);

        public static PurchaseResult Restored(
            string productId,
            string transactionId,
            IReadOnlyList<string> grantedItemIds,
            string? tierKey = null) =>
            new(true, productId, transactionId, grantedItemIds, tierKey, true, null);
    }

    /// <summary>
    /// Result of restore purchases operation.
    /// </summary>
    public sealed class RestoreResult
    {
        public bool IsSuccess { get; }
        public PurchaseError? Error { get; }
        public IReadOnlyList<PurchaseResult> RestoredPurchases { get; }

        private RestoreResult(
            bool isSuccess,
            IReadOnlyList<PurchaseResult>? restoredPurchases,
            PurchaseError? error)
        {
            IsSuccess = isSuccess;
            RestoredPurchases = restoredPurchases ?? System.Array.Empty<PurchaseResult>();
            Error = error;
        }

        public static RestoreResult Success(IReadOnlyList<PurchaseResult> restoredPurchases) =>
            new(true, restoredPurchases, null);

        public static RestoreResult Failure(PurchaseError error) =>
            new(false, null, error);

        public static RestoreResult NoRestorations() =>
            new(true, System.Array.Empty<PurchaseResult>(), null);
    }

    /// <summary>
    /// Result of store initialization.
    /// </summary>
    public sealed class InitializationResult
    {
        public bool IsSuccess { get; }
        public PurchaseError? Error { get; }
        public IReadOnlyList<ProductInfo> AvailableProducts { get; }

        private InitializationResult(
            bool isSuccess,
            IReadOnlyList<ProductInfo>? products,
            PurchaseError? error)
        {
            IsSuccess = isSuccess;
            AvailableProducts = products ?? System.Array.Empty<ProductInfo>();
            Error = error;
        }

        public static InitializationResult Success(IReadOnlyList<ProductInfo> products) =>
            new(true, products, null);

        public static InitializationResult Failure(PurchaseError error) =>
            new(false, null, error);
    }

    /// <summary>
    /// Product information from the store.
    /// </summary>
    public sealed class ProductInfo
    {
        public string ProductId { get; }
        public ProductType Type { get; }
        public string Title { get; }
        public string Description { get; }
        public string PriceString { get; }
        public decimal PriceDecimal { get; }
        public string CurrencyCode { get; }
        public string? TierKey { get; }

        public ProductInfo(
            string productId,
            ProductType type,
            string title,
            string description,
            string priceString,
            decimal priceDecimal,
            string currencyCode,
            string? tierKey = null)
        {
            ProductId = productId;
            Type = type;
            Title = title;
            Description = description;
            PriceString = priceString;
            PriceDecimal = priceDecimal;
            CurrencyCode = currencyCode;
            TierKey = tierKey;
        }
    }
}
