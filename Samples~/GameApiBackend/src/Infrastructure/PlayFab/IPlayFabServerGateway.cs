using Serhat.Forge.CloudScript.Domain.DTOs;

namespace Serhat.Forge.CloudScript.Infrastructure.PlayFab;

/// <summary>
/// Interface for PlayFab server-side operations.
/// </summary>
public interface IPlayFabServerGateway
{
    /// <summary>
    /// Gets player progress from PlayFab ReadOnly Data.
    /// </summary>
    Task<PlayFabResult<PlayerProgressDto>> GetPlayerProgressAsync(
        string playFabId,
        CancellationToken ct = default,
        bool autoRepair = true,
        GameplayBalanceConfigDto? gameplayBalance = null,
        CrownEventConfigDto? crownEventConfig = null);

    /// <summary>
    /// Saves player progress to PlayFab ReadOnly Data.
    /// </summary>
    Task<PlayFabResult<bool>> SavePlayerProgressAsync(
        string playFabId,
        PlayerProgressDto progress,
        CancellationToken ct = default,
        GameplayBalanceConfigDto? gameplayBalance = null,
        CrownEventConfigDto? crownEventConfig = null);

    /// <summary>
    /// Gets a title data value by key.
    /// </summary>
    Task<PlayFabResult<string>> GetTitleDataAsync(
        string key,
        CancellationToken ct = default);

    /// <summary>
    /// Mirrors player stars/level into leaderboard services.
    /// </summary>
    Task<PlayFabResult<bool>> SyncStarsLeaderboardAsync(
        string playFabId,
        int stars,
        int currentLevel,
        CancellationToken ct = default);

    /// <summary>
    /// Reads leaderboard page + current player row.
    /// </summary>
    Task<PlayFabResult<GetLeaderboardResultDto>> GetStarsLeaderboardAsync(
        string playFabId,
        bool countryOnly,
        int pageSize,
        int startingPosition,
        CancellationToken ct = default);

}

/// <summary>
/// Result of a PlayFab operation.
/// </summary>
public sealed class PlayFabResult<T>
{
    public bool IsSuccess { get; }
    public T? Data { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public bool IsRetryable { get; }

    private PlayFabResult(bool isSuccess, T? data, string? errorCode, string? errorMessage, bool isRetryable)
    {
        IsSuccess = isSuccess;
        Data = data;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        IsRetryable = isRetryable;
    }

    public static PlayFabResult<T> Success(T data) => new(true, data, null, null, false);

    public static PlayFabResult<T> Failure(string errorCode, string message, bool isRetryable = false) =>
        new(false, default, errorCode, message, isRetryable);
}
