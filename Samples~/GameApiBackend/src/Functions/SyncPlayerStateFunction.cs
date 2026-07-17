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
/// Function to synchronize mutable player state (lives, coins, boosters, etc.).
/// </summary>
public sealed class SyncPlayerStateFunction : FunctionBase
{
    private readonly IPlayFabServerGateway _playFab;

    public SyncPlayerStateFunction(
        IPlayFabServerGateway playFab,
        IIdempotencyStore idempotencyStore,
        ICorrelationContext correlationContext,
        ILogger<SyncPlayerStateFunction> logger)
        : base(idempotencyStore, correlationContext, logger)
    {
        _playFab = playFab;
    }

    [Function("SyncPlayerState")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData request)
    {
        var (envelope, errorResponse) = await ParseRequestAsync<SyncPlayerStateRequestDto>(request);
        if (errorResponse != null) return errorResponse;

        var playerId = GetPlayerId(request, envelope);
        var playerValidation = await EnsurePlayerIdAsync<SyncPlayerStateResultDto>(
            request,
            playerId,
            envelope!.CorrelationId);
        if (playerValidation != null) return playerValidation;

        Logger.LogInformation(
            "[{CorrelationId}] SyncPlayerState for {PlayerId}",
            envelope!.CorrelationId, playerId);

        return await ExecuteIdempotentAsync<SyncPlayerStateRequestDto, SyncPlayerStateResultDto>(
            request,
            envelope,
            playerId,
            async runRequest => await ProcessSyncAsync(playerId, envelope.CorrelationId, runRequest));
    }

    private async Task<(SyncPlayerStateResultDto? Result, ErrorPayload? Error)> ProcessSyncAsync(
        string playerId,
        string correlationId,
        SyncPlayerStateRequestDto runRequest)
    {
        if (runRequest?.Progress == null)
        {
            return (null, ErrorPayload.Create(
                ErrorCodes.ValidationFailed,
                "Progress payload is required"));
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

        var currentProgress = getResult.Data ?? PlayerProgressMerger.CreateDefaultProgress(playerId, gameplayBalance, crownEventConfig);
        var mergeResult = PlayerProgressMerger.ApplyClientState(
            currentProgress,
            runRequest.Progress,
            gameplayBalance,
            crownEventConfig);
        if (!mergeResult.IsSuccess)
        {
            return (null, ErrorPayload.Create(
                mergeResult.ErrorCode ?? ErrorCodes.ValidationFailed,
                mergeResult.ErrorMessage ?? "Failed to apply player state"));
        }

        var newProgress = mergeResult.NewProgress!;
        newProgress.PlayerId = playerId;

        // Nothing changed: avoid unnecessary PlayFab writes (prevents rate-limit pressure).
        if (newProgress.StateVersion == currentProgress.StateVersion)
        {
            return (new SyncPlayerStateResultDto
            {
                Success = true,
                UpdatedProgress = currentProgress
            }, null);
        }

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

        return (new SyncPlayerStateResultDto
        {
            Success = true,
            UpdatedProgress = newProgress
        }, null);
    }
}
