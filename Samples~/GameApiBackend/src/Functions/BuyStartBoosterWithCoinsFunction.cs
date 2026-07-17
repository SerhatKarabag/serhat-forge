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
/// Function to buy a configured start booster offer by spending coins on the backend.
/// </summary>
public sealed class BuyStartBoosterWithCoinsFunction : FunctionBase
{
    private readonly IPlayFabServerGateway _playFab;

    public BuyStartBoosterWithCoinsFunction(
        IPlayFabServerGateway playFab,
        IIdempotencyStore idempotencyStore,
        ICorrelationContext correlationContext,
        ILogger<BuyStartBoosterWithCoinsFunction> logger)
        : base(idempotencyStore, correlationContext, logger)
    {
        _playFab = playFab;
    }

    [Function("BuyStartBoosterWithCoins")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData request)
    {
        var (envelope, errorResponse) = await ParseRequestAsync<BuyStartBoosterWithCoinsRequestDto>(request);
        if (errorResponse != null) return errorResponse;

        var playerId = GetPlayerId(request, envelope);
        var playerValidation = await EnsurePlayerIdAsync<BuyStartBoosterWithCoinsResultDto>(
            request,
            playerId,
            envelope!.CorrelationId);
        if (playerValidation != null) return playerValidation;

        Logger.LogInformation(
            "[{CorrelationId}] BuyStartBoosterWithCoins for {PlayerId}, boosterType={BoosterType}",
            envelope!.CorrelationId,
            playerId,
            envelope.Payload?.BoosterType);

        return await ExecuteIdempotentAsync<BuyStartBoosterWithCoinsRequestDto, BuyStartBoosterWithCoinsResultDto>(
            request,
            envelope,
            playerId,
            async runRequest => await ProcessPurchaseAsync(playerId, envelope.CorrelationId, runRequest));
    }

    private async Task<(BuyStartBoosterWithCoinsResultDto? Result, ErrorPayload? Error)> ProcessPurchaseAsync(
        string playerId,
        string correlationId,
        BuyStartBoosterWithCoinsRequestDto? runRequest)
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
        var mergeResult = PlayerProgressMerger.BuyStartBoosterWithCoins(
            normalizedCurrent,
            runRequest ?? new BuyStartBoosterWithCoinsRequestDto(),
            gameplayBalance,
            crownEventConfig);
        if (!mergeResult.IsSuccess)
        {
            return (null, ErrorPayload.Create(
                mergeResult.ErrorCode ?? ErrorCodes.ValidationFailed,
                mergeResult.ErrorMessage ?? "Failed to buy start booster with coins"));
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

        var requestedBoosterType = runRequest?.BoosterType ?? string.Empty;
        var previousCount = GetStartBoosterCount(normalizedCurrent, requestedBoosterType);
        var newCount = GetStartBoosterCount(newProgress, requestedBoosterType);

        return (new BuyStartBoosterWithCoinsResultDto
        {
            Success = true,
            BoosterType = requestedBoosterType,
            CoinsSpent = Math.Max(0, normalizedCurrent.Coins - newProgress.Coins),
            BoostersGranted = Math.Max(0, newCount - previousCount),
            UpdatedProgress = newProgress
        }, null);
    }

    private static int GetStartBoosterCount(PlayerProgressDto progress, string boosterType)
    {
        if (progress.BoostersOwned == null || string.IsNullOrWhiteSpace(boosterType))
            return 0;

        var inventoryKey = boosterType.Trim() switch
        {
            "XpBoost" => "StartXpBooster",
            "PowerBoost" => "StartPowerBooster",
            "TimeBoost" => "StartTimeBooster",
            _ => boosterType.Trim()
        };

        return progress.BoostersOwned.TryGetValue(inventoryKey, out var count)
            ? Math.Max(0, count)
            : 0;
    }
}
