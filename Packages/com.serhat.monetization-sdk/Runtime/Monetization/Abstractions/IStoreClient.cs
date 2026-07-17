#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Backend.Monetization.Domain;

namespace Serhat.Backend.Monetization.Abstractions
{
    /// <summary>
    /// Raw receipt data from the store for server verification.
    /// </summary>
    public sealed class StoreReceipt
    {
        private string _platform = string.Empty;
        private string _productId = string.Empty;
        private string _transactionId = string.Empty;
        private string _receiptPayload = string.Empty;
        private Dictionary<string, string> _metadata = new();

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
        /// Platform verification payload. Google requires the purchase token. Apple uses only
        /// TransactionId and leaves this value empty to avoid transporting raw App Store data.
        /// </summary>
        public string ReceiptPayload
        {
            get => _receiptPayload;
            set => _receiptPayload = value ?? string.Empty;
        }

        /// <summary>
        /// Additional data for verification.
        /// - Android: Package name, subscription flag
        /// </summary>
        public Dictionary<string, string> Metadata
        {
            get => _metadata;
            set => _metadata = value ?? new Dictionary<string, string>();
        }
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
            new(true, receipt ?? throw new ArgumentNullException(nameof(receipt)), false, null);

        public static StorePurchaseResult Pending(StoreReceipt? receipt = null) =>
            new(false, receipt, true, PurchaseError.Pending());

        public static StorePurchaseResult Failure(PurchaseError error) =>
            new(false, null, false, error ?? throw new ArgumentNullException(nameof(error)));
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

    /// <summary>
    /// Optional store capability for binding a platform purchase to the authenticated game
    /// account. Configure this before initiating purchases.
    /// </summary>
    public interface IStoreAccountBinding
    {
        /// <summary>
        /// Sets the Google Play obfuscated account identifier embedded into future purchases.
        /// The value must be stable for the authenticated player and at most 64 characters.
        /// </summary>
        void SetGoogleObfuscatedAccountId(string accountId);

        /// <summary>
        /// Sets the deterministic StoreKit appAccountToken embedded into future Apple purchases.
        /// The value must be derived from the authenticated player's stable ID.
        /// </summary>
        void SetAppleAppAccountToken(Guid appAccountToken);
    }

    /// <summary>
    /// Optional production-grade store capability with observable confirmation results and
    /// cancellation-aware operations. Implement this interface when the adapter can report
    /// whether a confirmation reached its terminal callback and can bound callback-based waits.
    /// </summary>
    /// <remarks>
    /// This interface is additive so existing <see cref="IStoreClient"/> implementations remain
    /// source compatible. Consumers should prefer this capability when available and retain the
    /// pending receipt whenever confirmation fails or is cancelled. Adapters should apply a safe
    /// upper bound; callers may enforce a shorter policy with a linked
    /// <see cref="CancellationTokenSource"/> and
    /// <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/>.
    /// </remarks>
    public interface IResilientStoreClient : IStoreClient
    {
        /// <summary>
        /// Initializes the store with cancellation support for callback-based startup work.
        /// </summary>
        /// <param name="products">Product definitions requested from the store.</param>
        /// <param name="cancellationToken">Cancels the local initialization wait.</param>
        /// <exception cref="OperationCanceledException">
        /// The local wait was cancelled before initialization completed.
        /// </exception>
        Task<InitializationResult> InitializeAsync(
            IReadOnlyList<ProductDefinition> products,
            CancellationToken cancellationToken);

        /// <summary>
        /// Initiates a purchase with cancellation support for the local store wait.
        /// </summary>
        /// <remarks>
        /// Cancelling the wait does not cancel a transaction already accepted by the platform.
        /// A late completion must still be reconciled through
        /// <see cref="IStoreClient.OnPendingPurchaseCompleted"/>.
        /// </remarks>
        /// <param name="productId">Store product identifier.</param>
        /// <param name="cancellationToken">Cancels the local purchase wait.</param>
        /// <exception cref="OperationCanceledException">
        /// The local wait was cancelled before the purchase reached a terminal local state.
        /// </exception>
        Task<StorePurchaseResult> PurchaseAsync(
            string productId,
            CancellationToken cancellationToken);

        /// <summary>
        /// Confirms a pending purchase after successful server verification and reports whether
        /// the store SDK reported the terminal confirmation callback.
        /// </summary>
        /// <param name="productId">Store product identifier.</param>
        /// <param name="transactionId">Exact transaction identifier being confirmed.</param>
        /// <param name="cancellationToken">
        /// Cancels the local wait. Cancellation must not be interpreted as revoking or consuming
        /// the underlying store transaction.
        /// </param>
        /// <returns>
        /// A result that is successful only after the adapter receives the store's successful
        /// confirmation callback. A failure means the pending receipt must be retained for retry.
        /// </returns>
        /// <exception cref="OperationCanceledException">
        /// The local wait was cancelled before confirmation completed.
        /// </exception>
        Task<StoreOperationResult> ConfirmPendingPurchaseAsync(
            string productId,
            string transactionId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Restores non-consumable and subscription purchases with an explicit result union.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the local wait. Cancellation must not discard late store callbacks or pending
        /// transactions; the adapter remains responsible for reconciling them.
        /// </param>
        /// <returns>
        /// A result distinguishing complete success, partial success, no purchases, and failure.
        /// Receipts returned by a partial result are valid and may be verified independently.
        /// </returns>
        /// <exception cref="OperationCanceledException">
        /// The local wait was cancelled before the restore operation completed.
        /// </exception>
        Task<StoreRestoreResult> RestoreTransactionsAsync(
            CancellationToken cancellationToken = default);
    }
}
