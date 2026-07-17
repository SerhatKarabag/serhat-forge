using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;

/// <summary>
/// Request to grant entitlements.
/// </summary>
public sealed class GrantRequest
{
    /// <summary>
    /// PlayFab player ID.
    /// </summary>
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>
    /// Economy item IDs to grant.
    /// </summary>
    public List<string> ItemIds { get; set; } = new();

    /// <summary>
    /// Quantities for each item (for consumables).
    /// </summary>
    public List<int>? Quantities { get; set; }

    /// <summary>
    /// Idempotency key for the grant operation.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// Optional metadata to attach.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// Result of grant operation.
/// </summary>
public sealed class GrantResult
{
    public bool IsSuccess { get; }
    public List<string> GrantedItemIds { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public bool WasDuplicate { get; }

    private GrantResult(
        bool isSuccess,
        List<string>? grantedItemIds,
        string? errorCode,
        string? errorMessage,
        bool wasDuplicate)
    {
        IsSuccess = isSuccess;
        GrantedItemIds = grantedItemIds ?? new List<string>();
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        WasDuplicate = wasDuplicate;
    }

    public static GrantResult Success(List<string> grantedItemIds, bool wasDuplicate = false) =>
        new(true, grantedItemIds, null, null, wasDuplicate);

    public static GrantResult Failure(string errorCode, string errorMessage) =>
        new(false, null, errorCode, errorMessage, false);
}

/// <summary>
/// Request to revoke entitlements.
/// </summary>
public sealed class RevokeRequest
{
    public string PlayerId { get; set; } = string.Empty;
    public List<string> ItemIds { get; set; } = new();
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>
/// Abstraction for granting/revoking entitlements via PlayFab Economy v2.
/// </summary>
public interface IEntitlementGranter
{
    /// <summary>
    /// Grants economy items to a player.
    /// </summary>
    Task<GrantResult> GrantItemsAsync(GrantRequest request, CancellationToken ct = default);

    /// <summary>
    /// Revokes economy items from a player.
    /// Note: Economy v2 deletion may not be fully supported; implementation may mark as inactive instead.
    /// </summary>
    Task<GrantResult> RevokeItemsAsync(RevokeRequest request, CancellationToken ct = default);

    /// <summary>
    /// Gets current inventory items for a player.
    /// </summary>
    Task<List<string>> GetPlayerItemsAsync(string playerId, CancellationToken ct = default);
}
