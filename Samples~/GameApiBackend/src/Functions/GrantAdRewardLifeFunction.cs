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
/// Function to grant one life for a rewarded ad.
/// </summary>
public sealed class GrantAdRewardLifeFunction : FunctionBase
{
    private readonly IPlayFabServerGateway _playFab;

    public GrantAdRewardLifeFunction(
        IPlayFabServerGateway playFab,
        IIdempotencyStore idempotencyStore,
        ICorrelationContext correlationContext,
        ILogger<GrantAdRewardLifeFunction> logger)
        : base(idempotencyStore, correlationContext, logger)
    {
        _playFab = playFab;
    }

    [Function("GrantAdRewardLife")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData request)
    {
        var (envelope, errorResponse) = await ParseRequestAsync<GrantAdRewardLifeRequestDto>(request);
        if (errorResponse != null) return errorResponse;

        var playerId = GetPlayerId(request, envelope);
        var playerValidation = await EnsurePlayerIdAsync<GrantAdRewardLifeResultDto>(
            request,
            playerId,
            envelope!.CorrelationId);
        if (playerValidation != null) return playerValidation;

        Logger.LogInformation(
            "[{CorrelationId}] GrantAdRewardLife for {PlayerId}",
            envelope!.CorrelationId,
            playerId);

        return await ExecuteIdempotentAsync<GrantAdRewardLifeRequestDto, GrantAdRewardLifeResultDto>(
            request,
            envelope,
            playerId,
            async runRequest => await ProcessGrantAsync(playerId, envelope.CorrelationId, runRequest));
    }

    private async Task<(GrantAdRewardLifeResultDto? Result, ErrorPayload? Error)> ProcessGrantAsync(
        string playerId,
        string correlationId,
        GrantAdRewardLifeRequestDto? runRequest)
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
        var mergeResult = PlayerProgressMerger.GrantAdRewardLife(
            normalizedCurrent,
            runRequest ?? new GrantAdRewardLifeRequestDto(),
            gameplayBalance,
            crownEventConfig);
        if (!mergeResult.IsSuccess)
        {
            return (null, ErrorPayload.Create(
                mergeResult.ErrorCode ?? ErrorCodes.ValidationFailed,
                mergeResult.ErrorMessage ?? "Failed to grant ad reward life"));
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

        return (new GrantAdRewardLifeResultDto
        {
            Success = true,
            LivesGranted = Math.Max(0, newProgress.Lives - normalizedCurrent.Lives),
            UpdatedProgress = newProgress
        }, null);
    }
}
