using System;
using System.Collections.Generic;
using System.Text.Json;
using Serhat.Forge.CloudScript.Domain;
using Serhat.Forge.CloudScript.Domain.DTOs;
using Microsoft.Extensions.Logging;
using PlayFab;
using PlayFab.ServerModels;

namespace Serhat.Forge.CloudScript.Infrastructure.PlayFab;

/// <summary>
/// PlayFab server API gateway implementation.
/// </summary>
public sealed partial class PlayFabServerGateway : IPlayFabServerGateway
{
    private const string PlayerProgressKey = "PlayerProgress";
    private const int MaxSaveAttempts = 4;
    private readonly string _titleId;
    private readonly string _secretKey;
    private readonly ILogger<PlayFabServerGateway> _logger;

    public PlayFabServerGateway(string titleId, string secretKey, ILogger<PlayFabServerGateway> logger)
    {
        _titleId = titleId;
        _secretKey = secretKey;
        _logger = logger;

        // Configure PlayFab SDK
        PlayFabSettings.staticSettings.TitleId = titleId;
        PlayFabSettings.staticSettings.DeveloperSecretKey = secretKey;
    }

    public async Task<PlayFabResult<PlayerProgressDto>> GetPlayerProgressAsync(
        string playFabId,
        CancellationToken ct = default,
        bool autoRepair = true,
        GameplayBalanceConfigDto? gameplayBalance = null,
        CrownEventConfigDto? crownEventConfig = null)
    {
        if (string.IsNullOrWhiteSpace(playFabId))
        {
            return PlayFabResult<PlayerProgressDto>.Failure(
                ErrorCodes.ValidationFailed,
                "PlayerId is required.");
        }

        gameplayBalance ??= await GameplayBalanceProvider.GetAsync(this, _logger, playFabId, ct);
        crownEventConfig ??= await CrownEventConfigProvider.GetAsync(this, _logger, playFabId, ct);
        return await GetPlayerProgressCoreAsync(playFabId, gameplayBalance, crownEventConfig, ct, autoRepair);
    }

    private async Task<PlayFabResult<PlayerProgressDto>> GetPlayerProgressCoreAsync(
        string playFabId,
        GameplayBalanceConfigDto gameplayBalance,
        CrownEventConfigDto crownEventConfig,
        CancellationToken ct,
        bool autoRepair)
    {
        if (string.IsNullOrWhiteSpace(playFabId))
        {
            return PlayFabResult<PlayerProgressDto>.Failure(
                ErrorCodes.ValidationFailed,
                "PlayerId is required.");
        }

        _logger.LogDebug("Getting player progress for {PlayFabId}", playFabId);

        var request = new GetUserDataRequest
        {
            PlayFabId = playFabId,
            Keys = new List<string> { PlayerProgressKey }
        };

        var result = await PlayFabServerAPI.GetUserReadOnlyDataAsync(request);

        if (result.Error != null)
        {
            return MapPlayFabError<PlayerProgressDto>(result.Error);
        }

        if (result.Result?.Data == null || !result.Result.Data.TryGetValue(PlayerProgressKey, out var record))
        {
            // New player - create default state
            _logger.LogDebug("No existing progress for {PlayFabId}, creating default", playFabId);
            var defaultProgress = PlayerProgressMerger.CreateDefaultProgress(playFabId, gameplayBalance, crownEventConfig);

            // Persist the initial snapshot immediately so subsequent calls
            // observe the same baseline (prevents first-session timer drift).
            if (autoRepair)
            {
                await TryPersistNormalizedSnapshotAsync(playFabId, defaultProgress, ct);
            }

            return PlayFabResult<PlayerProgressDto>.Success(defaultProgress);
        }

        try
        {
            var progress = JsonSerializer.Deserialize<PlayerProgressDto>(record.Value);
            if (progress == null)
            {
                _logger.LogWarning("Failed to deserialize player progress for {PlayFabId}", playFabId);
                return PlayFabResult<PlayerProgressDto>.Success(
                    PlayerProgressMerger.CreateDefaultProgress(playFabId, gameplayBalance, crownEventConfig));
            }

            var normalized = PlayerProgressMerger.NormalizeProgress(progress, playFabId, gameplayBalance, crownEventConfig);

            if (autoRepair && ShouldRepairReadModel(progress, normalized))
            {
                await TryPersistNormalizedSnapshotAsync(playFabId, normalized, ct);
            }

            return PlayFabResult<PlayerProgressDto>.Success(normalized);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON error deserializing player progress for {PlayFabId}", playFabId);
            return PlayFabResult<PlayerProgressDto>.Failure(
                ErrorCodes.SerializationError,
                "Failed to deserialize player progress");
        }
    }

    public async Task<PlayFabResult<bool>> SavePlayerProgressAsync(
        string playFabId,
        PlayerProgressDto progress,
        CancellationToken ct = default,
        GameplayBalanceConfigDto? gameplayBalance = null,
        CrownEventConfigDto? crownEventConfig = null)
    {
        if (string.IsNullOrWhiteSpace(playFabId))
        {
            return PlayFabResult<bool>.Failure(
                ErrorCodes.ValidationFailed,
                "PlayerId is required.");
        }

        if (progress == null)
        {
            return PlayFabResult<bool>.Failure(
                ErrorCodes.ValidationFailed,
                "Progress payload is required.");
        }

        _logger.LogDebug("Saving player progress for {PlayFabId}, version {Version}", playFabId, progress.StateVersion);

        try
        {
            gameplayBalance ??= await GameplayBalanceProvider.GetAsync(this, _logger, playFabId, ct);
            crownEventConfig ??= await CrownEventConfigProvider.GetAsync(this, _logger, playFabId, ct);
            var candidate = progress;

            for (var attempt = 1; attempt <= MaxSaveAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                var latestResult = await GetPlayerProgressCoreAsync(
                    playFabId,
                    gameplayBalance,
                    crownEventConfig,
                    ct,
                    autoRepair: false);
                if (latestResult.IsSuccess && latestResult.Data != null)
                {
                    candidate = PlayerProgressMerger.MergeForPersistence(
                        latestResult.Data,
                        candidate,
                        playFabId,
                        gameplayBalance,
                        crownEventConfig);
                }

                candidate.PlayerId = playFabId;
                var stateJson = JsonSerializer.Serialize(candidate);

                var request = new UpdateUserDataRequest
                {
                    PlayFabId = playFabId,
                    Data = new Dictionary<string, string>
                    {
                        { PlayerProgressKey, stateJson }
                    }
                };

                var result = await PlayFabServerAPI.UpdateUserReadOnlyDataAsync(request);
                if (result.Error == null)
                {
                    return PlayFabResult<bool>.Success(true);
                }

                if (!IsRetryableWriteError(result.Error) || attempt == MaxSaveAttempts)
                {
                    return MapPlayFabError<bool>(result.Error);
                }

                var delay = GetRetryDelay(attempt);
                _logger.LogWarning(
                    "Transient PlayFab write error {Error} for {PlayFabId}; retrying attempt {Attempt}/{MaxAttempts} after {DelayMs}ms",
                    result.Error.Error,
                    playFabId,
                    attempt,
                    MaxSaveAttempts,
                    delay.TotalMilliseconds);

                await Task.Delay(delay, ct);
            }

            return PlayFabResult<bool>.Failure(
                ErrorCodes.PlayFabError,
                "Failed to save player progress after retries",
                isRetryable: true);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON error serializing player progress for {PlayFabId}", playFabId);
            return PlayFabResult<bool>.Failure(
                ErrorCodes.SerializationError,
                "Failed to serialize player progress");
        }
    }

    public async Task<PlayFabResult<string>> GetTitleDataAsync(
        string key,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return PlayFabResult<string>.Failure(
                ErrorCodes.ValidationFailed,
                "Title data key is required.");
        }

        ct.ThrowIfCancellationRequested();

        var request = new GetTitleDataRequest
        {
            Keys = new List<string> { key }
        };

        var result = await PlayFabServerAPI.GetTitleDataAsync(request);
        if (result.Error != null)
        {
            return MapPlayFabError<string>(result.Error);
        }

        if (result.Result?.Data == null || !result.Result.Data.TryGetValue(key, out var value))
        {
            return PlayFabResult<string>.Success(string.Empty);
        }

        return PlayFabResult<string>.Success(value ?? string.Empty);
    }

    private static bool IsRetryableWriteError(PlayFabError error)
    {
        return error.Error == PlayFabErrorCode.ServiceUnavailable ||
               error.Error == PlayFabErrorCode.ConnectionError ||
               error.Error == PlayFabErrorCode.APIClientRequestRateLimitExceeded ||
               error.Error == PlayFabErrorCode.DataUpdateRateExceeded ||
               error.Error == PlayFabErrorCode.ConcurrentEditError;
    }

    private static TimeSpan GetRetryDelay(int attempt)
    {
        var baseDelayMs = 250 * attempt;
        return TimeSpan.FromMilliseconds(baseDelayMs);
    }

    private async Task TryPersistNormalizedSnapshotAsync(
        string playFabId,
        PlayerProgressDto normalized,
        CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            normalized.PlayerId = playFabId;
            var stateJson = JsonSerializer.Serialize(normalized);
            var repairRequest = new UpdateUserDataRequest
            {
                PlayFabId = playFabId,
                Data = new Dictionary<string, string>
                {
                    { PlayerProgressKey, stateJson }
                }
            };

            var repairResult = await PlayFabServerAPI.UpdateUserReadOnlyDataAsync(repairRequest);
            if (repairResult.Error != null)
            {
                _logger.LogWarning(
                    "Failed to persist normalized snapshot for {PlayFabId}: {Error} - {Message}",
                    playFabId,
                    repairResult.Error.Error,
                    repairResult.Error.ErrorMessage);
            }
            else
            {
                _logger.LogInformation("Persisted normalized snapshot for {PlayFabId}", playFabId);
            }
        }
        catch (OperationCanceledException)
        {
            // Respect cancellation silently.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error while persisting normalized snapshot for {PlayFabId}", playFabId);
        }
    }

    private static bool ShouldRepairReadModel(PlayerProgressDto original, PlayerProgressDto normalized)
    {
        if (original.SchemaVersion != normalized.SchemaVersion ||
            original.StateVersion != normalized.StateVersion ||
            !string.Equals(original.PlayerId, normalized.PlayerId, StringComparison.Ordinal) ||
            original.CurrentLevel != normalized.CurrentLevel ||
            original.Lives != normalized.Lives ||
            original.MaxLives != normalized.MaxLives ||
            original.Coins != normalized.Coins ||
            original.TotalCoinsEarned != normalized.TotalCoinsEarned ||
            original.Stars != normalized.Stars ||
            original.HasInfiniteLives != normalized.HasInfiniteLives ||
            original.InfiniteLivesEndUtc != normalized.InfiniteLivesEndUtc ||
            original.NextLifeTimeUtc != normalized.NextLifeTimeUtc ||
            original.WinStreak != normalized.WinStreak ||
            original.HasRemovedAds != normalized.HasRemovedAds ||
            original.PiggyBankCoins != normalized.PiggyBankCoins ||
            original.PiggyBankStartedUtc != normalized.PiggyBankStartedUtc ||
            original.PiggyBankDurationSeconds != normalized.PiggyBankDurationSeconds ||
            original.PiggyBankMaxCoins != normalized.PiggyBankMaxCoins ||
            !CrownEventStateEqual(original.CrownEvent, normalized.CrownEvent))
        {
            return true;
        }

        if (!SubscriptionsEqual(original.ActiveSubscription, normalized.ActiveSubscription))
        {
            return true;
        }

        if (!IntDictionaryEqual(original.BoostersOwned, normalized.BoostersOwned) ||
            !IntDictionaryEqual(original.BoostersFree, normalized.BoostersFree))
        {
            return true;
        }

        if (!LevelResultsEqual(original.Results, normalized.Results))
        {
            return true;
        }

        return false;
    }

    private static bool CrownEventStateEqual(CrownEventStateDto? left, CrownEventStateDto? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left == null || right == null)
        {
            return false;
        }

        if (left.CycleIndex != right.CycleIndex ||
            left.CrownsInCycle != right.CrownsInCycle ||
            left.LastUpdatedUtc != right.LastUpdatedUtc)
        {
            return false;
        }

        var leftMilestones = left.ClaimedMilestones ?? new List<int>();
        var rightMilestones = right.ClaimedMilestones ?? new List<int>();
        if (leftMilestones.Count != rightMilestones.Count)
        {
            return false;
        }

        for (var i = 0; i < leftMilestones.Count; i++)
        {
            if (leftMilestones[i] != rightMilestones[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IntDictionaryEqual(
        Dictionary<string, int>? left,
        Dictionary<string, int>? right)
    {
        var l = left ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var r = right ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (l.Count != r.Count)
        {
            return false;
        }

        foreach (var (key, value) in l)
        {
            if (!r.TryGetValue(key, out var rightValue) || rightValue != value)
            {
                return false;
            }
        }

        return true;
    }

    private static bool LevelResultsEqual(
        Dictionary<string, LevelResultDto>? left,
        Dictionary<string, LevelResultDto>? right)
    {
        var l = left ?? new Dictionary<string, LevelResultDto>(StringComparer.Ordinal);
        var r = right ?? new Dictionary<string, LevelResultDto>(StringComparer.Ordinal);
        if (l.Count != r.Count)
        {
            return false;
        }

        foreach (var (key, value) in l)
        {
            if (!r.TryGetValue(key, out var other) || other == null || value == null)
            {
                return false;
            }

            if (value.Stars != other.Stars || Math.Abs(value.TimeSec - other.TimeSec) > 0.0001f)
            {
                return false;
            }
        }

        return true;
    }

    private static bool SubscriptionsEqual(SubscriptionDto? left, SubscriptionDto? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left == null || right == null)
        {
            return false;
        }

        return string.Equals(left.ProductId, right.ProductId, StringComparison.Ordinal) &&
               string.Equals(left.TierKey, right.TierKey, StringComparison.Ordinal) &&
               left.Status == right.Status &&
               left.AutoRenew == right.AutoRenew &&
               left.PeriodStartUtc == right.PeriodStartUtc &&
               left.PeriodEndUtc == right.PeriodEndUtc &&
               left.OriginalPurchaseDateUtc == right.OriginalPurchaseDateUtc &&
               string.Equals(left.Platform, right.Platform, StringComparison.Ordinal) &&
               string.Equals(left.GrantedItemId, right.GrantedItemId, StringComparison.Ordinal) &&
               left.GracePeriodDaysRemaining == right.GracePeriodDaysRemaining;
    }

    private PlayFabResult<T> MapPlayFabError<T>(PlayFabError error)
    {
        _logger.LogWarning("PlayFab error: {Error} - {Message}", error.Error, error.ErrorMessage);

        var (errorCode, isRetryable) = error.Error switch
        {
            PlayFabErrorCode.ServiceUnavailable => (ErrorCodes.PlayFabError, true),
            PlayFabErrorCode.ConnectionError => (ErrorCodes.PlayFabError, true),
            PlayFabErrorCode.APIClientRequestRateLimitExceeded => (ErrorCodes.RateLimited, true),
            PlayFabErrorCode.DataUpdateRateExceeded => (ErrorCodes.RateLimited, true),
            PlayFabErrorCode.ConcurrentEditError => (ErrorCodes.Conflict, true),
            _ => (ErrorCodes.PlayFabError, error.HttpCode >= 500)
        };

        return PlayFabResult<T>.Failure(
            errorCode,
            error.ErrorMessage ?? "PlayFab error",
            isRetryable);
    }
}
