namespace Serhat.Forge.CloudScript.Domain.DTOs;

/// <summary>
/// Request payload for refreshing the caller's leaderboard metadata.
/// Used after a client-side display name change so the leaderboard's
/// metadata "D" field reflects the new name without waiting for the
/// next level submission.
/// </summary>
public sealed class RefreshLeaderboardMetadataRequestDto
{
}

/// <summary>
/// Result payload for leaderboard metadata refresh.
/// </summary>
public sealed class RefreshLeaderboardMetadataResultDto
{
    public bool Success { get; set; }
    public int Stars { get; set; }
    public int Level { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}
