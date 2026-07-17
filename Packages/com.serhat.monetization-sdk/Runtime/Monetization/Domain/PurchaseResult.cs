#nullable enable

using System;
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
            ProductId = productId ?? string.Empty;
            TransactionId = transactionId;
            GrantedItemIds = DomainCollectionSnapshot.Copy(grantedItemIds);
            TierKey = tierKey;
            WasRestored = wasRestored;
            Error = error;
        }

        public static PurchaseResult Success(
            string productId,
            string transactionId,
            IReadOnlyList<string> grantedItemIds,
            string? tierKey = null,
            bool wasRestored = false)
        {
            EnsureSuccessfulPurchaseData(productId, transactionId);
            return new PurchaseResult(
                true,
                productId,
                transactionId,
                grantedItemIds ?? throw new ArgumentNullException(nameof(grantedItemIds)),
                tierKey,
                wasRestored,
                null);
        }

        public static PurchaseResult Failure(string productId, PurchaseError error) =>
            new(
                false,
                productId ?? string.Empty,
                null,
                null,
                null,
                false,
                error ?? throw new ArgumentNullException(nameof(error)));

        public static PurchaseResult Restored(
            string productId,
            string transactionId,
            IReadOnlyList<string> grantedItemIds,
            string? tierKey = null) =>
            Success(productId, transactionId, grantedItemIds, tierKey, wasRestored: true);

        private static void EnsureSuccessfulPurchaseData(string productId, string transactionId)
        {
            if (string.IsNullOrWhiteSpace(productId))
            {
                throw new ArgumentException("Product ID is required.", nameof(productId));
            }

            if (string.IsNullOrWhiteSpace(transactionId))
            {
                throw new ArgumentException("Transaction ID is required.", nameof(transactionId));
            }
        }
    }

    /// <summary>
    /// Terminal state of a restore operation.
    /// </summary>
    public enum RestoreResultStatus
    {
        /// <summary>Every discovered purchase was verified and restored.</summary>
        Succeeded = 0,

        /// <summary>The store had no restorable purchases.</summary>
        NoPurchases = 1,

        /// <summary>At least one purchase restored and at least one purchase failed.</summary>
        PartiallySucceeded = 2,

        /// <summary>The operation failed or none of the discovered purchases restored.</summary>
        Failed = 3
    }

    /// <summary>
    /// Discriminated result of a restore operation, including per-purchase partial failures.
    /// </summary>
    public sealed class RestoreResult
    {
        /// <summary>Terminal state of the restore operation.</summary>
        public RestoreResultStatus Status { get; }

        /// <summary>
        /// Whether the restore completed without any failures. No purchases is a successful,
        /// terminal outcome.
        /// </summary>
        public bool IsSuccess =>
            Status == RestoreResultStatus.Succeeded ||
            Status == RestoreResultStatus.NoPurchases;

        /// <summary>Whether both successful and failed purchase results are present.</summary>
        public bool IsPartialSuccess => Status == RestoreResultStatus.PartiallySucceeded;

        /// <summary>
        /// Operation-level error, or the first purchase error for partial/all-purchase failure.
        /// Inspect <see cref="FailedPurchases"/> for every per-purchase error.
        /// </summary>
        public PurchaseError? Error { get; }

        /// <summary>Purchases that were successfully verified and restored.</summary>
        public IReadOnlyList<PurchaseResult> RestoredPurchases { get; }

        /// <summary>Purchases discovered by the store but not restored.</summary>
        public IReadOnlyList<PurchaseResult> FailedPurchases { get; }

        private RestoreResult(
            RestoreResultStatus status,
            IReadOnlyList<PurchaseResult>? restoredPurchases,
            IReadOnlyList<PurchaseResult>? failedPurchases,
            PurchaseError? error)
        {
            Status = status;
            RestoredPurchases = DomainCollectionSnapshot.Copy(restoredPurchases);
            FailedPurchases = DomainCollectionSnapshot.Copy(failedPurchases);
            Error = error;
        }

        /// <summary>
        /// Creates a result from all per-purchase verification results. Despite the legacy method
        /// name, mixed or all-failed inputs are classified as partial or failed respectively.
        /// </summary>
        public static RestoreResult Success(IReadOnlyList<PurchaseResult> restoredPurchases) =>
            FromPurchases(restoredPurchases);

        public static RestoreResult Failure(PurchaseError error) =>
            new(
                RestoreResultStatus.Failed,
                null,
                null,
                error ?? throw new ArgumentNullException(nameof(error)));

        public static RestoreResult NoRestorations() =>
            new(RestoreResultStatus.NoPurchases, null, null, null);

        /// <summary>
        /// Classifies per-purchase results into a complete, partial, empty, or failed outcome.
        /// </summary>
        /// <param name="purchaseResults">All attempted restore verification results.</param>
        public static RestoreResult FromPurchases(IReadOnlyList<PurchaseResult> purchaseResults)
        {
            if (purchaseResults == null)
            {
                throw new ArgumentNullException(nameof(purchaseResults));
            }

            if (purchaseResults.Count == 0)
            {
                return NoRestorations();
            }

            List<PurchaseResult>? restored = null;
            List<PurchaseResult>? failed = null;
            PurchaseError? firstError = null;

            for (var i = 0; i < purchaseResults.Count; i++)
            {
                var purchaseResult = purchaseResults[i]
                    ?? throw new ArgumentException(
                        "Restore results cannot contain null entries.",
                        nameof(purchaseResults));

                if (purchaseResult.IsSuccess)
                {
                    (restored ??= new List<PurchaseResult>()).Add(purchaseResult);
                    continue;
                }

                (failed ??= new List<PurchaseResult>()).Add(purchaseResult);
                firstError ??= purchaseResult.Error;
            }

            if (failed == null)
            {
                return new RestoreResult(
                    RestoreResultStatus.Succeeded,
                    restored,
                    null,
                    null);
            }

            firstError ??= PurchaseError.Unknown("A restored purchase failed without an error.");

            if (restored == null)
            {
                return new RestoreResult(
                    RestoreResultStatus.Failed,
                    null,
                    failed,
                    firstError);
            }

            return new RestoreResult(
                RestoreResultStatus.PartiallySucceeded,
                restored,
                failed,
                firstError);
        }
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
            AvailableProducts = DomainCollectionSnapshot.Copy(products);
            Error = error;
        }

        public static InitializationResult Success(IReadOnlyList<ProductInfo> products) =>
            new(true, products ?? throw new ArgumentNullException(nameof(products)), null);

        public static InitializationResult Failure(PurchaseError error) =>
            new(false, null, error ?? throw new ArgumentNullException(nameof(error)));
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
            if (string.IsNullOrWhiteSpace(productId))
            {
                throw new ArgumentException("Product ID is required.", nameof(productId));
            }

            ProductId = productId;
            Type = type;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            PriceString = priceString ?? string.Empty;
            PriceDecimal = priceDecimal;
            CurrencyCode = currencyCode ?? string.Empty;
            TierKey = tierKey;
        }
    }

    internal static class DomainCollectionSnapshot
    {
        public static IReadOnlyList<T> Copy<T>(IReadOnlyList<T>? source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<T>();
            }

            var copy = new T[source.Count];
            for (var i = 0; i < copy.Length; i++)
            {
                copy[i] = source[i];
            }

            return Array.AsReadOnly(copy);
        }
    }
}
