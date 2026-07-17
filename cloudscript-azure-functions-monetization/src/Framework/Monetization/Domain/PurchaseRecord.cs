using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Domain;

/// <summary>
/// Product type enumeration.
/// </summary>
public enum ProductType
{
    Consumable,
    NonConsumable,
    Subscription
}

/// <summary>
/// Purchase status.
/// </summary>
public enum PurchaseStatus
{
    Pending,
    Verified,
    Granted,
    Failed,
    Refunded
}

/// <summary>
/// Platform identifier.
/// </summary>
public static class Platform
{
    public const string Apple = "apple";
    public const string Google = "google";
}

/// <summary>
/// Durable record of a verified purchase.
/// Used for idempotency and audit trail.
/// </summary>
public sealed class PurchaseRecord
{
    /// <summary>
    /// Unique key for idempotency.
    /// Apple format: apple:{verifiedTransactionId}.
    /// Google format: google:{SHA-256 purchase token}.
    /// </summary>
    public string TransactionKey { get; set; } = string.Empty;

    /// <summary>
    /// Platform (apple/google).
    /// </summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>
    /// Store product ID.
    /// </summary>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>
    /// Product type.
    /// </summary>
    public ProductType ProductType { get; set; }

    /// <summary>
    /// PlayFab player ID.
    /// </summary>
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>
    /// Purchase status.
    /// </summary>
    public PurchaseStatus Status { get; set; }

    /// <summary>
    /// Economy item IDs granted.
    /// </summary>
    public List<string> GrantedEconomyItemIds { get; set; } = new();

    /// <summary>
    /// Quantity granted (for consumables).
    /// </summary>
    public int QuantityGranted { get; set; } = 1;

    /// <summary>
    /// For subscriptions: tier key.
    /// </summary>
    public string? TierKey { get; set; }

    /// <summary>
    /// Immutable entitlement payload captured when this purchase is first claimed. A retry must
    /// never reinterpret an already verified payment through mutable catalog configuration.
    /// </summary>
    public bool HasGrantPayloadSnapshot { get; set; }

    public List<string> GrantEconomyItemIds { get; set; } = new();

    public List<int>? GrantQuantities { get; set; }

    public Dictionary<string, string>? GrantMetadata { get; set; }

    /// <summary>Immutable subscription precedence captured with the grant payload.</summary>
    public int TierPrecedence { get; set; }

    /// <summary>
    /// Cached response JSON for idempotent replay.
    /// </summary>
    public string? CachedResponseJson { get; set; }

    /// <summary>
    /// Error code if failed.
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// When the purchase was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// When the purchase was last updated.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// Store-specific transaction ID.
    /// </summary>
    public string StoreTransactionId { get; set; } = string.Empty;

    /// <summary>
    /// For Apple subscriptions: original transaction ID.
    /// </summary>
    public string? OriginalTransactionId { get; set; }

    /// <summary>
    /// Opaque owner token for the worker currently processing this purchase.
    /// A worker may update the state machine only while it owns this lease.
    /// </summary>
    public string? ProcessingLeaseId { get; set; }

    /// <summary>
    /// UTC expiry for the current processing lease. Expired Pending/Verified records may be
    /// reclaimed after a process crash or function timeout.
    /// </summary>
    public DateTime? ProcessingLeaseExpiresAtUtc { get; set; }

    /// <summary>
    /// Earliest UTC time at which a retryable failure may be attempted again.
    /// </summary>
    public DateTime? NextRetryAtUtc { get; set; }

    /// <summary>
    /// Whether the current non-terminal error can be retried safely.
    /// Pending means store verification must resume; Verified means only the idempotent grant
    /// and completion stages remain.
    /// </summary>
    public bool IsRetryable { get; set; }

    /// <summary>Total number of processing leases acquired for this purchase.</summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// UTC timestamp persisted before the first outbound entitlement grant attempt. Automatic
    /// retries are bounded because the downstream provider's idempotency record is not perpetual.
    /// </summary>
    public DateTime? FirstGrantAttemptAtUtc { get; set; }

    /// <summary>
    /// Distinguishes new rows that durably reached the pre-grant state from legacy Verified rows
    /// whose missing grant timestamp is ambiguous.
    /// </summary>
    public bool HasGrantAttemptTracking { get; set; }

    /// <summary>Persisted store-verification snapshot used to resume after a crash.</summary>
    public bool HasStoreVerificationSnapshot { get; set; }

    public DateTime? StorePurchaseDateUtc { get; set; }

    public DateTime? StoreExpirationDateUtc { get; set; }

    public SubscriptionStatus? StoreSubscriptionStatus { get; set; }

    public bool? StoreAutoRenew { get; set; }

    public bool StoreIsSandbox { get; set; }

    public DateTime? StoreGracePeriodEndUtc { get; set; }

    /// <summary>
    /// Creates a transaction key for idempotency.
    /// </summary>
    public static string CreateTransactionKey(string platform, string transactionId)
    {
        return $"{platform}:{transactionId}";
    }

    /// <summary>
    /// Creates the canonical Google purchase identity. Google purchase tokens are the durable,
    /// globally unique claim credential; client-supplied transaction/order IDs are untrusted.
    /// </summary>
    public static string CreateGoogleTransactionKey(string purchaseToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purchaseToken);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(purchaseToken));
        return global::Serhat.Forge.CloudScript.Framework.Monetization.Domain.Platform.Google +
               ":" +
               Convert.ToHexString(digest);
    }

    internal PurchaseRecord Copy() => new()
    {
        TransactionKey = TransactionKey,
        Platform = Platform,
        ProductId = ProductId,
        ProductType = ProductType,
        PlayerId = PlayerId,
        Status = Status,
        GrantedEconomyItemIds = new List<string>(GrantedEconomyItemIds),
        QuantityGranted = QuantityGranted,
        TierKey = TierKey,
        HasGrantPayloadSnapshot = HasGrantPayloadSnapshot,
        GrantEconomyItemIds = new List<string>(GrantEconomyItemIds),
        GrantQuantities = GrantQuantities == null ? null : new List<int>(GrantQuantities),
        GrantMetadata = GrantMetadata == null
            ? null
            : new Dictionary<string, string>(GrantMetadata, StringComparer.Ordinal),
        TierPrecedence = TierPrecedence,
        CachedResponseJson = CachedResponseJson,
        ErrorCode = ErrorCode,
        ErrorMessage = ErrorMessage,
        CreatedAtUtc = CreatedAtUtc,
        UpdatedAtUtc = UpdatedAtUtc,
        StoreTransactionId = StoreTransactionId,
        OriginalTransactionId = OriginalTransactionId,
        ProcessingLeaseId = ProcessingLeaseId,
        ProcessingLeaseExpiresAtUtc = ProcessingLeaseExpiresAtUtc,
        NextRetryAtUtc = NextRetryAtUtc,
        IsRetryable = IsRetryable,
        AttemptCount = AttemptCount,
        FirstGrantAttemptAtUtc = FirstGrantAttemptAtUtc,
        HasGrantAttemptTracking = HasGrantAttemptTracking,
        HasStoreVerificationSnapshot = HasStoreVerificationSnapshot,
        StorePurchaseDateUtc = StorePurchaseDateUtc,
        StoreExpirationDateUtc = StoreExpirationDateUtc,
        StoreSubscriptionStatus = StoreSubscriptionStatus,
        StoreAutoRenew = StoreAutoRenew,
        StoreIsSandbox = StoreIsSandbox,
        StoreGracePeriodEndUtc = StoreGracePeriodEndUtc
    };

    internal bool CanAcquireProcessingLease(DateTime nowUtc)
    {
        if (Status != PurchaseStatus.Pending && Status != PurchaseStatus.Verified)
        {
            return false;
        }

        if (ProcessingLeaseExpiresAtUtc.HasValue && ProcessingLeaseExpiresAtUtc.Value > nowUtc)
        {
            return false;
        }

        return !NextRetryAtUtc.HasValue || NextRetryAtUtc.Value <= nowUtc;
    }

    internal bool HasSameImmutableIdentity(PurchaseRecord other) =>
        other != null &&
        string.Equals(TransactionKey, other.TransactionKey, StringComparison.Ordinal) &&
        string.Equals(Platform, other.Platform, StringComparison.Ordinal) &&
        string.Equals(ProductId, other.ProductId, StringComparison.Ordinal) &&
        string.Equals(PlayerId, other.PlayerId, StringComparison.Ordinal) &&
        ProductType == other.ProductType;

    internal void AcquireProcessingLease(string leaseId, DateTime nowUtc, TimeSpan leaseDuration)
    {
        ProcessingLeaseId = leaseId;
        ProcessingLeaseExpiresAtUtc = nowUtc.Add(leaseDuration);
        NextRetryAtUtc = null;
        IsRetryable = false;
        ErrorCode = null;
        ErrorMessage = null;
        UpdatedAtUtc = nowUtc;
        if (AttemptCount < int.MaxValue)
        {
            AttemptCount++;
        }
    }

    internal bool TryRenewProcessingLease(
        string leaseId,
        DateTime nowUtc,
        TimeSpan leaseDuration)
    {
        if (!string.Equals(ProcessingLeaseId, leaseId, StringComparison.Ordinal) ||
            !ProcessingLeaseExpiresAtUtc.HasValue ||
            ProcessingLeaseExpiresAtUtc.Value <= nowUtc ||
            (Status != PurchaseStatus.Pending && Status != PurchaseStatus.Verified))
        {
            return false;
        }

        ProcessingLeaseExpiresAtUtc = nowUtc.Add(leaseDuration);
        UpdatedAtUtc = nowUtc;
        return true;
    }
}
