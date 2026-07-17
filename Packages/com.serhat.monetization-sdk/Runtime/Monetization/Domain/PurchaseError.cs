#nullable enable

using System;

namespace Serhat.Backend.Monetization.Domain
{
    /// <summary>
    /// Error codes for purchase operations.
    /// Maps to backend error codes with retryable semantics.
    /// </summary>
    public enum PurchaseErrorCode
    {
        /// <summary>No error.</summary>
        None = 0,

        /// <summary>User cancelled the purchase flow.</summary>
        UserCancelled = 1,

        /// <summary>Store not initialized. Call InitializeAsync first.</summary>
        StoreNotInitialized = 2,

        /// <summary>Store is unavailable (network, region, etc.).</summary>
        StoreUnavailable = 3,

        /// <summary>Product not found in store catalog.</summary>
        ProductNotFound = 4,

        /// <summary>Product not allowed by server configuration.</summary>
        ProductNotAllowed = 5,

        /// <summary>Server verification failed.</summary>
        VerificationFailed = 6,

        /// <summary>Already owns this non-consumable/subscription.</summary>
        AlreadyOwned = 7,

        /// <summary>Network error during verification.</summary>
        NetworkError = 8,

        /// <summary>Rate limited by backend.</summary>
        RateLimited = 9,

        /// <summary>Purchase is pending (waiting for parent approval, etc.).</summary>
        Pending = 10,

        /// <summary>Store-specific error not mapped.</summary>
        StoreError = 11,

        /// <summary>Server internal error.</summary>
        ServerError = 12,

        /// <summary>
        /// Changing an active subscription requires a store- and backend-specific
        /// replacement flow that is not configured by this client.
        /// </summary>
        SubscriptionChangeNotSupported = 13,

        /// <summary>Unknown error.</summary>
        Unknown = 99
    }

    /// <summary>
    /// Purchase error with code, message, and retry information.
    /// </summary>
    public sealed class PurchaseError
    {
        public PurchaseErrorCode Code { get; }
        public string Message { get; }
        public bool IsRetryable { get; }
        public string? StoreErrorCode { get; }

        public PurchaseError(
            PurchaseErrorCode code,
            string message,
            bool isRetryable = false,
            string? storeErrorCode = null)
        {
            Code = code;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            IsRetryable = isRetryable;
            StoreErrorCode = storeErrorCode;
        }

        public static PurchaseError UserCancelled() =>
            new(PurchaseErrorCode.UserCancelled, "Purchase was cancelled by user");

        public static PurchaseError StoreNotInitialized() =>
            new(PurchaseErrorCode.StoreNotInitialized, "Store is not initialized. Call InitializeAsync first.");

        public static PurchaseError StoreUnavailable(string? details = null) =>
            new(PurchaseErrorCode.StoreUnavailable, details ?? "Store is unavailable", isRetryable: true);

        public static PurchaseError ProductNotFound(string productId) =>
            new(PurchaseErrorCode.ProductNotFound, $"Product '{productId}' not found in store catalog");

        public static PurchaseError ProductNotAllowed(string productId) =>
            new(PurchaseErrorCode.ProductNotAllowed, $"Product '{productId}' is not allowed by server configuration");

        public static PurchaseError VerificationFailed(string? reason = null) =>
            new(PurchaseErrorCode.VerificationFailed, reason ?? "Server verification failed");

        public static PurchaseError AlreadyOwned(string productId) =>
            new(PurchaseErrorCode.AlreadyOwned, $"Already owns '{productId}'");

        public static PurchaseError NetworkError(string? details = null) =>
            new(PurchaseErrorCode.NetworkError, details ?? "Network error", isRetryable: true);

        public static PurchaseError RateLimited() =>
            new(PurchaseErrorCode.RateLimited, "Rate limited. Try again later.", isRetryable: true);

        public static PurchaseError Pending() =>
            new(PurchaseErrorCode.Pending, "Purchase is pending approval");

        public static PurchaseError StoreError(string storeCode, string message) =>
            new(PurchaseErrorCode.StoreError, message, storeErrorCode: storeCode);

        public static PurchaseError ServerError(string? message = null) =>
            new(PurchaseErrorCode.ServerError, message ?? "Server error", isRetryable: true);

        public static PurchaseError SubscriptionChangeNotSupported(
            string currentProductId,
            string targetProductId) =>
            new(
                PurchaseErrorCode.SubscriptionChangeNotSupported,
                $"Changing active subscription '{currentProductId}' to '{targetProductId}' " +
                "requires a project-specific store replacement and backend lifecycle flow.");

        public static PurchaseError Unknown(string? message = null) =>
            new(PurchaseErrorCode.Unknown, message ?? "Unknown error");

        public override string ToString() => $"[{Code}] {Message}";
    }
}
