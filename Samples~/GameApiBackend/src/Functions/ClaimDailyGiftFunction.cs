using System;
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
/// Claims the current calendar day's daily-gift reward and advances the streak on the server side.
/// </summary>
public sealed class ClaimDailyGiftFunction : FunctionBase
{
    private readonly IPlayFabServerGateway _playFab;

    public ClaimDailyGiftFunction(
        IPlayFabServerGateway playFab,
        IIdempotencyStore idempotencyStore,
        ICorrelationContext correlationContext,
        ILogger<ClaimDailyGiftFunction> logger)
        : base(idempotencyStore, correlationContext, logger)
    {
        _playFab = playFab;
    }

    [Function("ClaimDailyGift")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData request)
    {
        var (envelope, errorResponse) = await ParseRequestAsync<ClaimDailyGiftRequestDto>(request);
        if (errorResponse != null) return errorResponse;

        var playerId = GetPlayerId(request, envelope);
        var playerValidation = await EnsurePlayerIdAsync<ClaimDailyGiftResultDto>(
            request,
            playerId,
            envelope!.CorrelationId);
        if (playerValidation != null) return playerValidation;

        Logger.LogInformation(
            "[{CorrelationId}] ClaimDailyGift for {PlayerId}",
            envelope!.CorrelationId,
            playerId);

        return await ExecuteIdempotentAsync<ClaimDailyGiftRequestDto, ClaimDailyGiftResultDto>(
            request,
            envelope,
            playerId,
            async _ => await ProcessClaimAsync(playerId, envelope.CorrelationId));
    }

    private async Task<(ClaimDailyGiftResultDto? Result, ErrorPayload? Error)> ProcessClaimAsync(
        string playerId,
        string correlationId)
    {
        var gameplayBalance = await GameplayBalanceProvider.GetAsync(_playFab, Logger, correlationId);
        var crownEventConfig = await CrownEventConfigProvider.GetAsync(_playFab, Logger, correlationId);
        var dailyGiftConfig = await DailyGiftConfigProvider.GetAsync(_playFab, Logger, correlationId);
        var getResult = await _playFab.GetPlayerProgressAsync(playerId, gameplayBalance: gameplayBalance, crownEventConfig: crownEventConfig);
        if (!getResult.IsSuccess)
        {
            return (null, ErrorPayload.Create(
                getResult.ErrorCode ?? ErrorCodes.PlayFabError,
                getResult.ErrorMessage ?? "Failed to get current progress",
                getResult.IsRetryable));
        }

        var currentProgress = getResult.Data ?? PlayerProgressMerger.CreateDefaultProgress(playerId, gameplayBalance, crownEventConfig);
        var normalizedCurrent = PlayerProgressMerger.NormalizeProgress(currentProgress, playerId, gameplayBalance, crownEventConfig);
        var claimResult = PlayerProgressMerger.ClaimDailyGift(normalizedCurrent, gameplayBalance, crownEventConfig, dailyGiftConfig);

        var newProgress = claimResult.Progress;
        newProgress.PlayerId = playerId;

        if (claimResult.WasDuplicate)
        {
            return (new ClaimDailyGiftResultDto
            {
                Success = true,
                WasDuplicate = true,
                StreakReset = false,
                ClaimedDay = claimResult.ClaimedDay,
                Reward = new DailyGiftRewardDto { Day = claimResult.ClaimedDay },
                UpdatedProgress = newProgress
            }, null);
        }

        if (newProgress.StateVersion != normalizedCurrent.StateVersion)
        {
            var saveResult = await _playFab.SavePlayerProgressAsync(playerId, newProgress, gameplayBalance: gameplayBalance, crownEventConfig: crownEventConfig);
            if (!saveResult.IsSuccess)
            {
                return (null, ErrorPayload.Create(
                    saveResult.ErrorCode ?? ErrorCodes.PlayFabError,
                    saveResult.ErrorMessage ?? "Failed to save daily-gift claim",
                    saveResult.IsRetryable));
            }

            var latestResult = await _playFab.GetPlayerProgressAsync(playerId, gameplayBalance: gameplayBalance, crownEventConfig: crownEventConfig);
            if (latestResult.IsSuccess && latestResult.Data != null)
            {
                newProgress = latestResult.Data;
            }
        }

        return (new ClaimDailyGiftResultDto
        {
            Success = true,
            WasDuplicate = false,
            StreakReset = claimResult.StreakReset,
            ClaimedDay = claimResult.ClaimedDay,
            Reward = claimResult.Reward,
            UpdatedProgress = newProgress
        }, null);
    }
}
