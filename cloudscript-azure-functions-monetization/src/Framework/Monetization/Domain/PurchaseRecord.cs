using System;
using System.Collections.Generic;

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
    /// Format: {platform}:{transactionId}
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
    /// Creates a transaction key for idempotency.
    /// </summary>
    public static string CreateTransactionKey(string platform, string transactionId)
    {
        return $"{platform}:{transactionId}";
    }
}
