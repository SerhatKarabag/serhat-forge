using System;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Domain;

/// <summary>
/// Result of store verification.
/// </summary>
public sealed record VerificationResult
{
    public bool IsValid { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool IsRetryable { get; init; }

    /// <summary>
    /// Product ID from the store.
    /// </summary>
    public string? ProductId { get; init; }

    /// <summary>
    /// Transaction ID from the store.
    /// </summary>
    public string? TransactionId { get; init; }

    /// <summary>
    /// For Apple subscriptions: original transaction ID.
    /// </summary>
    public string? OriginalTransactionId { get; init; }

    /// <summary>
    /// Purchase date.
    /// </summary>
    public DateTime? PurchaseDateUtc { get; init; }

    /// <summary>
    /// Expiration date (for subscriptions).
    /// </summary>
    public DateTime? ExpirationDateUtc { get; init; }

    /// <summary>
    /// Whether this is a subscription.
    /// </summary>
    public bool IsSubscription { get; init; }

    /// <summary>
    /// Subscription status if applicable.
    /// </summary>
    public SubscriptionStatus? SubscriptionStatus { get; init; }

    /// <summary>
    /// Whether auto-renew is enabled.
    /// </summary>
    public bool? AutoRenew { get; init; }

    /// <summary>
    /// Whether this was a sandbox/test purchase.
    /// </summary>
    public bool IsSandbox { get; init; }

    /// <summary>
    /// Grace period end if applicable.
    /// </summary>
    public DateTime? GracePeriodEndUtc { get; init; }

    private VerificationResult(
        bool isValid,
        string? errorCode = null,
        string? errorMessage = null,
        bool isRetryable = false)
    {
        IsValid = isValid;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        IsRetryable = isRetryable;
    }

    public static VerificationResult Valid() => new(true);

    public static VerificationResult Invalid(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage);

    public static VerificationResult Retryable(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage, isRetryable: true);

    public static VerificationResult InvalidReceipt(string? details = null) =>
        new(false, "INVALID_RECEIPT", details ?? "Receipt validation failed");

    public static VerificationResult ExpiredReceipt() =>
        new(false, "EXPIRED_RECEIPT", "Receipt has expired");

    public static VerificationResult ProductMismatch(string expected, string actual) =>
        new(false, "PRODUCT_MISMATCH", $"Product mismatch: expected {expected}, got {actual}");

    public static VerificationResult StoreError(string message) =>
        Retryable("STORE_ERROR", message);
}
