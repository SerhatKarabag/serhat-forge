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
/// Function to grant rewarded-ad coins.
/// </summary>
public sealed class GrantAdRewardCoinsFunction : FunctionBase
{
    private const int RewardCoins = 15;

    private readonly IPlayFabServerGateway _playFab;

    public GrantAdRewardCoinsFunction(
        IPlayFabServerGateway playFab,
        IIdempotencyStore idempotencyStore,
        ICorrelationContext correlationContext,
        ILogger<GrantAdRewardCoinsFunction> logger)
        : base(idempotencyStore, correlationContext, logger)
    {
        _playFab = playFab;
    }

    [Function("GrantAdRewardCoins")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData request)
    {
        var (envelope, errorResponse) = await ParseRequestAsync<GrantAdRewardCoinsRequestDto>(request);
        if (errorResponse != null) return errorResponse;

        var playerId = GetPlayerId(request, envelope);
        var playerValidation = await EnsurePlayerIdAsync<GrantAdRewardCoinsResultDto>(
            request,
            playerId,
            envelope!.CorrelationId);
        if (playerValidation != null) return playerValidation;

        Logger.LogInformation(
            "[{CorrelationId}] GrantAdRewardCoins for {PlayerId}",
            envelope!.CorrelationId,
            playerId);

        return await ExecuteIdempotentAsync<GrantAdRewardCoinsRequestDto, GrantAdRewardCoinsResultDto>(
            request,
            envelope,
            playerId,
            async runRequest => await ProcessGrantAsync(playerId, envelope.CorrelationId, runRequest));
    }

    private async Task<(GrantAdRewardCoinsResultDto? Result, ErrorPayload? Error)> ProcessGrantAsync(
        string playerId,
        string correlationId,
        GrantAdRewardCoinsRequestDto? runRequest)
    {
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

        var currentProgress = getResult.Data ?? PlayerProgressMerger.CreateDefaultProgress(playerId, gameplayBalance, crownEventConfig);
        var normalizedCurrent = PlayerProgressMerger.NormalizeProgress(currentProgress, playerId, gameplayBalance, crownEventConfig);
        var mergeResult = PlayerProgressMerger.GrantAdRewardCoins(
            normalizedCurrent,
            runRequest ?? new GrantAdRewardCoinsRequestDto(),
            gameplayBalance,
            crownEventConfig,
            RewardCoins);

        if (!mergeResult.IsSuccess)
        {
            return (null, ErrorPayload.Create(
                mergeResult.ErrorCode ?? ErrorCodes.ValidationFailed,
                mergeResult.ErrorMessage ?? "Failed to grant ad reward coins"));
        }

        var newProgress = mergeResult.NewProgress!;
        newProgress.PlayerId = playerId;

        if (newProgress.StateVersion != normalizedCurrent.StateVersion)
        {
            var saveResult = await _playFab.SavePlayerProgressAsync(playerId, newProgress, gameplayBalance: gameplayBalance, crownEventConfig: crownEventConfig);
            if (!saveResult.IsSuccess)
            {
                return (null, ErrorPayload.Create(
                    saveResult.ErrorCode ?? ErrorCodes.PlayFabError,
                    saveResult.ErrorMessage ?? "Failed to save player state",
                    saveResult.IsRetryable));
            }

            var latestResult = await _playFab.GetPlayerProgressAsync(playerId, gameplayBalance: gameplayBalance, crownEventConfig: crownEventConfig);
            if (latestResult.IsSuccess && latestResult.Data != null)
            {
                newProgress = latestResult.Data;
            }
        }

        return (new GrantAdRewardCoinsResultDto
        {
            Success = true,
            CoinsGranted = Math.Max(0, newProgress.Coins - normalizedCurrent.Coins),
            UpdatedProgress = newProgress
        }, null);
    }
}
