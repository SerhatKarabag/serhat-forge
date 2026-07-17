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
/// Function to refill lives by spending coins on the backend.
/// </summary>
public sealed class BuyLivesWithCoinsFunction : FunctionBase
{
    private readonly IPlayFabServerGateway _playFab;

    public BuyLivesWithCoinsFunction(
        IPlayFabServerGateway playFab,
        IIdempotencyStore idempotencyStore,
        ICorrelationContext correlationContext,
        ILogger<BuyLivesWithCoinsFunction> logger)
        : base(idempotencyStore, correlationContext, logger)
    {
        _playFab = playFab;
    }

    [Function("BuyLivesWithCoins")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData request)
    {
        var (envelope, errorResponse) = await ParseRequestAsync<BuyLivesWithCoinsRequestDto>(request);
        if (errorResponse != null) return errorResponse;

        var playerId = GetPlayerId(request, envelope);
        var playerValidation = await EnsurePlayerIdAsync<BuyLivesWithCoinsResultDto>(
            request,
            playerId,
            envelope!.CorrelationId);
        if (playerValidation != null) return playerValidation;

        Logger.LogInformation(
            "[{CorrelationId}] BuyLivesWithCoins for {PlayerId}, requestedLives={RequestedLives}",
            envelope!.CorrelationId,
            playerId,
            envelope.Payload?.LivesToBuy);

        return await ExecuteIdempotentAsync<BuyLivesWithCoinsRequestDto, BuyLivesWithCoinsResultDto>(
            request,
            envelope,
            playerId,
            async runRequest => await ProcessBuyLivesAsync(playerId, envelope.CorrelationId, runRequest));
    }

    private async Task<(BuyLivesWithCoinsResultDto? Result, ErrorPayload? Error)> ProcessBuyLivesAsync(
        string playerId,
        string correlationId,
        BuyLivesWithCoinsRequestDto? runRequest)
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
        var mergeResult = PlayerProgressMerger.BuyLivesWithCoins(
            normalizedCurrent,
            runRequest ?? new BuyLivesWithCoinsRequestDto(),
            gameplayBalance,
            crownEventConfig);
        if (!mergeResult.IsSuccess)
        {
            return (null, ErrorPayload.Create(
                mergeResult.ErrorCode ?? ErrorCodes.ValidationFailed,
                mergeResult.ErrorMessage ?? "Failed to buy lives with coins"));
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

        return (new BuyLivesWithCoinsResultDto
        {
            Success = true,
            CoinsSpent = Math.Max(0, normalizedCurrent.Coins - newProgress.Coins),
            LivesGranted = Math.Max(0, newProgress.Lives - normalizedCurrent.Lives),
            UpdatedProgress = newProgress
        }, null);
    }
}
