using System;
using System.Collections.Generic;
using System.Globalization;
using Serhat.Forge.CloudScript.Domain;
using Serhat.Forge.CloudScript.Domain.DTOs;
using Serhat.Forge.CloudScript.Infrastructure.Idempotency;
using Serhat.Forge.CloudScript.Infrastructure.Logging;
using Serhat.Forge.CloudScript.Infrastructure.PlayFab;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Serhat.Forge.CloudScript.Functions;

/// <summary>
/// Grants the one-time Rate Us reward on the server side.
/// </summary>
public sealed class ClaimRateUsRewardFunction : FunctionBase
{
    private const int TriggerLevel = 10;
    private const int MaxRewardStars = 5;
    private const int CoinsPerStar = 2;
    private const string RewardClaimKey = "rate_us_reward_v2";
    private const string LegacyLowRatingRewardClaimKey = "rate_us_low_rating_reward_v1";

    private readonly IPlayFabServerGateway _playFab;

    public ClaimRateUsRewardFunction(
        IPlayFabServerGateway playFab,
        IIdempotencyStore idempotencyStore,
        ICorrelationContext correlationContext,
        ILogger<ClaimRateUsRewardFunction> logger)
        : base(idempotencyStore, correlationContext, logger)
    {
        _playFab = playFab;
    }

    [Function("ClaimRateUsReward")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData request)
    {
        var (envelope, errorResponse) = await ParseRequestAsync<ClaimRateUsRewardRequestDto>(request);
        if (errorResponse != null) return errorResponse;

        var playerId = GetPlayerId(request, envelope);
        var playerValidation = await EnsurePlayerIdAsync<ClaimRateUsRewardResultDto>(
            request,
            playerId,
            envelope!.CorrelationId);
        if (playerValidation != null) return playerValidation;

        Logger.LogInformation(
            "[{CorrelationId}] ClaimRateUsReward for {PlayerId}, stars={Stars}",
            envelope!.CorrelationId,
            playerId,
            envelope.Payload?.Stars);

        return await ExecuteIdempotentAsync<ClaimRateUsRewardRequestDto, ClaimRateUsRewardResultDto>(
            request,
            envelope,
            playerId,
            async runRequest => await ProcessClaimAsync(playerId, envelope.CorrelationId, runRequest));
    }

    private async Task<(ClaimRateUsRewardResultDto? Result, ErrorPayload? Error)> ProcessClaimAsync(
        string playerId,
        string correlationId,
        ClaimRateUsRewardRequestDto runRequest)
    {
        if (runRequest == null || runRequest.Stars <= 0 || runRequest.Stars > MaxRewardStars)
        {
            return (null, ErrorPayload.Create(
                ErrorCodes.ValidationFailed,
                $"Stars must be between 1 and {MaxRewardStars} for the Rate Us reward."));
        }

        var gameplayBalance = await GameplayBalanceProvider.GetAsync(_playFab, Logger, correlationId);
        var crownEventConfig = await CrownEventConfigProvider.GetAsync(_playFab, Logger, correlationId);
        var getResult = await _playFab.GetPlayerProgressAsync(playerId, gameplayBalance: gameplayBalance, crownEventConfig: crownEventConfig);
        if (!getResult.IsSuccess)
        {
            return (null, ErrorPayload.Create(
                getResult.ErrorCode ?? ErrorCodes.PlayFabError,
                getResult.ErrorMessage ?? "Failed to get current progress",
                getResult.IsRetryable));
        }

        var progress = PlayerProgressMerger.NormalizeProgress(
            getResult.Data ?? PlayerProgressMerger.CreateDefaultProgress(playerId, gameplayBalance, crownEventConfig),
            playerId,
            gameplayBalance,
            crownEventConfig);
        progress.ClaimedRewards ??= new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        if (!HasCompletedTriggerLevel(progress))
        {
            return (null, ErrorPayload.Create(
                ErrorCodes.InvalidRequest,
                $"Rate Us reward is only available after completing level {TriggerLevel}."));
        }

        if (progress.ClaimedRewards.ContainsKey(RewardClaimKey) ||
            progress.ClaimedRewards.ContainsKey(LegacyLowRatingRewardClaimKey))
        {
            return (new ClaimRateUsRewardResultDto
            {
                Success = true,
                WasDuplicate = true,
                CoinsGranted = 0,
                UpdatedProgress = progress
            }, null);
        }

        var nowUtc = DateTime.UtcNow;
        var coinsGranted = ClampToInt((long)runRequest.Stars * CoinsPerStar);
        progress.Coins = ClampToInt((long)progress.Coins + coinsGranted);
        progress.TotalCoinsEarned = ClampToInt((long)progress.TotalCoinsEarned + coinsGranted);
        progress.ClaimedRewards[RewardClaimKey] = nowUtc;
        progress.StateVersion++;
        progress.LastUpdatedUtc = nowUtc;

        var saveResult = await _playFab.SavePlayerProgressAsync(playerId, progress, gameplayBalance: gameplayBalance, crownEventConfig: crownEventConfig);
        if (!saveResult.IsSuccess)
        {
            return (null, ErrorPayload.Create(
                saveResult.ErrorCode ?? ErrorCodes.PlayFabError,
                saveResult.ErrorMessage ?? "Failed to save Rate Us reward",
                saveResult.IsRetryable));
        }

        var latestResult = await _playFab.GetPlayerProgressAsync(playerId, gameplayBalance: gameplayBalance, crownEventConfig: crownEventConfig);
        if (latestResult.IsSuccess && latestResult.Data != null)
        {
            progress = latestResult.Data;
        }

        return (new ClaimRateUsRewardResultDto
        {
            Success = true,
            WasDuplicate = false,
            CoinsGranted = coinsGranted,
            UpdatedProgress = progress
        }, null);
    }

    private static bool HasCompletedTriggerLevel(PlayerProgressDto progress)
    {
        if (progress.CurrentLevel > TriggerLevel)
        {
            return true;
        }

        return progress.Results != null &&
               progress.Results.ContainsKey(TriggerLevel.ToString(CultureInfo.InvariantCulture));
    }

    private static int ClampToInt(long value)
    {
        if (value <= 0)
        {
            return 0;
        }

        return value >= int.MaxValue ? int.MaxValue : (int)value;
    }
}
