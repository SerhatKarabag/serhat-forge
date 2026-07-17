namespace Serhat.Forge.CloudScript.Domain.DTOs;

/// <summary>
/// Request payload for one-player leaderboard backfill.
/// Intended to be called from PlayFab CloudScript segment actions.
/// </summary>
public sealed class BackfillLeaderboardPlayerRequestDto
{
    public string PlayFabId { get; set; } = string.Empty;
    public bool AssignRandomDisplayName { get; set; } = true;
    public bool OverwriteDisplayName { get; set; }
    public string DisplayNamePrefix { get; set; } = "Player";
    public int RandomDigits { get; set; } = 6;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Result payload for one-player leaderboard backfill.
/// </summary>
public sealed class BackfillLeaderboardPlayerResultDto
{
    public string PlayFabId { get; set; } = string.Empty;
    public int Stars { get; set; }
    public int Level { get; set; }
    public string PreviousDisplayName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool DisplayNameChanged { get; set; }
    public bool LeaderboardSynced { get; set; }
}
