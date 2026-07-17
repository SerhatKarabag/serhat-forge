using System;
using System.Collections.Generic;

namespace Serhat.Forge.CloudScript.Domain.DTOs;

/// <summary>
/// Product type codes aligned with client monetization SDK enum order.
/// </summary>
public enum ProductTypeCode
{
    Consumable = 0,
    NonConsumable = 1,
    Subscription = 2
}

/// <summary>
/// Subscription status codes aligned with client monetization SDK enum order.
/// </summary>
public enum SubscriptionStatusCode
{
    None = 0,
    Active = 1,
    GracePeriod = 2,
    Paused = 3,
    Cancelled = 4,
    Expired = 5,
    Refunded = 6,
    Chargeback = 7
}

/// <summary>
/// Request payload for purchase verification.
/// </summary>
public sealed class VerifyPurchaseRequestDto
{
    public string Platform { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public string ReceiptPayload { get; set; } = string.Empty;
    public string ProductType { get; set; } = string.Empty;
    public string? TierKey { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// Response payload for purchase verification.
/// </summary>
public sealed class VerifyPurchaseResponseDto
{
    public bool Success { get; set; }
    public string? TransactionKey { get; set; }
    public List<string> GrantedItemIds { get; set; } = new();
    public SubscriptionDto? Subscription { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool WasDuplicate { get; set; }
}

/// <summary>
/// Request payload for entitlement query.
/// </summary>
public sealed class GetEntitlementsRequestDto
{
    public bool ForceRefresh { get; set; }
}

/// <summary>
/// Response payload for entitlement query.
/// </summary>
public sealed class GetEntitlementsResponseDto
{
    public List<EntitlementDto> Entitlements { get; set; } = new();
    public SubscriptionDto? ActiveSubscription { get; set; }
    public DateTime ServerTimestampUtc { get; set; }
}

/// <summary>
/// Entitlement view model returned to client.
/// </summary>
public sealed class EntitlementDto
{
    public string ItemId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? SourceProductId { get; set; }
    public ProductTypeCode ProductType { get; set; }
    public int Quantity { get; set; } = 1;
    public DateTime GrantedAtUtc { get; set; }
    public string? TransactionId { get; set; }
}

/// <summary>
/// Subscription state returned to client and persisted in progress.
/// </summary>
public sealed class SubscriptionDto
{
    public string ProductId { get; set; } = string.Empty;
    public string TierKey { get; set; } = string.Empty;
    public SubscriptionStatusCode Status { get; set; } = SubscriptionStatusCode.None;
    public bool AutoRenew { get; set; }
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public DateTime OriginalPurchaseDateUtc { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string? GrantedItemId { get; set; }
    public int? GracePeriodDaysRemaining { get; set; }
}

/// <summary>
/// Verified purchase record persisted for anti-abuse checks and idempotency.
/// </summary>
public sealed class VerifiedPurchaseRecordDto
{
    public string ProductId { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public ProductTypeCode ProductType { get; set; } = ProductTypeCode.Consumable;
    public string TierKey { get; set; } = string.Empty;
    public bool WasRestored { get; set; }
    public string GrantedItemId { get; set; } = string.Empty;
    public DateTime VerifiedAtUtc { get; set; }
}
