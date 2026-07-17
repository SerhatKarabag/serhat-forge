using System;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Domain;

/// <summary>
/// Type of webhook event.
/// </summary>
public enum WebhookEventType
{
    Unknown,

    // Unified events (used by parsers/services)
    InitialPurchase,
    Resubscribed,
    Renewed,
    Cancelled,
    Expired,
    Refunded,
    Chargeback,
    GracePeriodStarted,
    GracePeriodEnded,
    Paused,
    Resumed,
    UpgradeDowngrade,
    Revoked,
    Recovered,
    Other,

    // Legacy/compatibility events
    SubscriptionPurchased,
    SubscriptionRenewed,
    SubscriptionCancelled,
    SubscriptionExpired,
    SubscriptionGracePeriod,
    SubscriptionPaused,
    SubscriptionReactivated,
    SubscriptionUpgraded,
    SubscriptionDowngraded,
    SubscriptionCrossgraded,
    RefundDeclined,
    BillingRetry,
    BillingRecovered,
    PurchaseCompleted,
    ConsumptionRequest
}

/// <summary>
/// Parsed webhook event.
/// </summary>
public sealed class WebhookEvent
{
    /// <summary>
    /// Event ID for deduplication.
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// Event type.
    /// </summary>
    public WebhookEventType EventType { get; set; }

    /// <summary>
    /// Platform (apple/google).
    /// </summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>
    /// Subscription key if applicable.
    /// </summary>
    public string? SubscriptionKey { get; set; }

    /// <summary>
    /// Product ID.
    /// </summary>
    public string? ProductId { get; set; }

    /// <summary>
    /// Transaction ID.
    /// </summary>
    public string? TransactionId { get; set; }

    /// <summary>
    /// Original transaction ID (Apple subscriptions).
    /// </summary>
    public string? OriginalTransactionId { get; set; }

    /// <summary>
    /// Event timestamp.
    /// </summary>
    public DateTime EventTimestampUtc { get; set; }

    /// <summary>
    /// Period start (subscriptions).
    /// </summary>
    public DateTime? PeriodStartUtc { get; set; }

    /// <summary>
    /// Period end (subscriptions).
    /// </summary>
    public DateTime? PeriodEndUtc { get; set; }

    /// <summary>
    /// Expiration date if applicable.
    /// </summary>
    public DateTime? ExpirationDateUtc { get; set; }

    /// <summary>
    /// New status if applicable.
    /// </summary>
    public SubscriptionStatus? NewStatus { get; set; }

    /// <summary>
    /// Whether auto-renew is enabled.
    /// </summary>
    public bool? AutoRenew { get; set; }

    /// <summary>
    /// Grace period end if applicable.
    /// </summary>
    public DateTime? GracePeriodEndUtc { get; set; }

    /// <summary>
    /// When the webhook was received.
    /// </summary>
    public DateTime? ReceivedAtUtc { get; set; }

    /// <summary>
    /// Whether this is from sandbox.
    /// </summary>
    public bool IsSandbox { get; set; }

    /// <summary>
    /// New tier key for upgrade/downgrade.
    /// </summary>
    public string? NewTierKey { get; set; }

    /// <summary>
    /// New product ID for upgrade/downgrade.
    /// </summary>
    public string? NewProductId { get; set; }

    /// <summary>
    /// Raw payload for debugging (truncated, no sensitive data).
    /// </summary>
    public string? RawPayloadPreview { get; set; }

    /// <summary>
    /// Canonical identity for an entitlement side effect. Provider delivery IDs remain the
    /// webhook claim identity; this value makes different deliveries of the same authoritative
    /// store transition converge on one grant/revoke operation.
    /// </summary>
    public string? EntitlementOperationId { get; set; }

    /// <summary>
    /// Whether the signed store transaction is an auto-renewable subscription.
    /// </summary>
    public bool IsSubscription { get; set; }

    /// <summary>
    /// Apple-signed refund/revocation classification. This is retained as a bounded enum-like
    /// string rather than raw signed payload data.
    /// </summary>
    public string? RevocationType { get; set; }

    /// <summary>
    /// Apple-signed refunded percentage in milliunits (100000 means 100%).
    /// </summary>
    public int? RevocationPercentage { get; set; }

    /// <summary>
    /// Whether the signed provider data proves a complete one-time refund/revocation.
    /// Subscription refund notifications revoke access regardless of proration.
    /// </summary>
    public bool IsFullRefund { get; set; }
}

/// <summary>
/// Result of webhook processing.
/// </summary>
public sealed class WebhookProcessingResult
{
    public bool IsSuccess { get; }
    public bool WasDuplicate { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public bool IsRetryable { get; }

    public bool IsDuplicate => WasDuplicate;

    private WebhookProcessingResult(
        bool isSuccess,
        bool wasDuplicate,
        string? errorCode,
        string? errorMessage,
        bool isRetryable)
    {
        IsSuccess = isSuccess;
        WasDuplicate = wasDuplicate;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        IsRetryable = isRetryable;
    }

    public static WebhookProcessingResult Success() => new(true, false, null, null, false);
    public static WebhookProcessingResult Duplicate() => new(true, true, null, null, false);
    public static WebhookProcessingResult Failure(string message, string? errorCode = null, bool retryable = false) =>
        new(false, false, errorCode, message, retryable);
}
