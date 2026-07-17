using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serhat.Backend.Monetization.Domain;

namespace Serhat.Backend.Monetization.Abstractions
{
    /// <summary>
    /// Raw receipt data from the store for server verification.
    /// </summary>
    public sealed class StoreReceipt
    {
        /// <summary>Store platform (apple/google).</summary>
        public string Platform { get; set; } = string.Empty;

        /// <summary>Store product ID.</summary>
        public string ProductId { get; set; } = string.Empty;

        /// <summary>Transaction ID from the store.</summary>
        public string TransactionId { get; set; } = string.Empty;

        /// <summary>
        /// Receipt payload (Base64 encoded).
        /// - iOS: Full App Store receipt
        /// - Android: Purchase token
        /// </summary>
        public string ReceiptPayload { get; set; } = string.Empty;

        /// <summary>
        /// Additional data for verification.
        /// - Android: Package name, subscription flag
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Store purchase result before server verification.
    /// </summary>
    public sealed class StorePurchaseResult
    {
        public bool IsSuccess { get; }
        public PurchaseError? Error { get; }
        public StoreReceipt? Receipt { get; }
        public bool IsPending { get; }

        private StorePurchaseResult(
            bool isSuccess,
            StoreReceipt? receipt,
            bool isPending,
            PurchaseError? error)
        {
            IsSuccess = isSuccess;
            Receipt = receipt;
            IsPending = isPending;
            Error = error;
        }

        public static StorePurchaseResult Success(StoreReceipt receipt) =>
            new(true, receipt, false, null);

        public static StorePurchaseResult Pending(StoreReceipt receipt) =>
            new(false, receipt, true, PurchaseError.Pending());

        public static StorePurchaseResult Failure(PurchaseError error) =>
            new(false, null, false, error);
    }

    /// <summary>
    /// Abstraction over platform store (Unity IAP).
    /// </summary>
    public interface IStoreClient
    {
        /// <summary>Whether the store is initialized.</summary>
        bool IsInitialized { get; }

        /// <summary>Event raised when a pending purchase completes.</summary>
        event Action<StorePurchaseResult>? OnPendingPurchaseCompleted;

        /// <summary>
        /// Initializes the store with the provided product definitions.
        /// </summary>
        Task<InitializationResult> InitializeAsync(IReadOnlyList<ProductDefinition> products);

        /// <summary>
        /// Initiates a purchase.
        /// </summary>
        Task<StorePurchaseResult> PurchaseAsync(string productId);

        /// <summary>
        /// Gets available products with pricing.
        /// </summary>
        IReadOnlyList<ProductInfo> GetProducts();

        /// <summary>
        /// Gets a specific product.
        /// </summary>
        ProductInfo? GetProduct(string productId);

        /// <summary>
        /// Confirms a pending purchase after server verification.
        /// </summary>
        void ConfirmPendingPurchase(string productId, string transactionId);

        /// <summary>
        /// Restores non-consumable and subscription purchases.
        /// </summary>
        Task<IReadOnlyList<StoreReceipt>> RestoreTransactionsAsync();
    }
}
