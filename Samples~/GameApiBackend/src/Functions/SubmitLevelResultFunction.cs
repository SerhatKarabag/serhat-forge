using Serhat.Forge.CloudScript.Domain;
using Serhat.Forge.CloudScript.Domain.DTOs;
using Serhat.Forge.CloudScript.Domain.Validation;
using Serhat.Forge.CloudScript.Infrastructure.Idempotency;
using Serhat.Forge.CloudScript.Infrastructure.Logging;
using Serhat.Forge.CloudScript.Infrastructure.PlayFab;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Serhat.Forge.CloudScript.Functions;

/// <summary>
/// Function to submit a completed level result.
/// </summary>
public sealed class SubmitLevelResultFunction : FunctionBase
{
    private readonly IPlayFabServerGateway _playFab;

    public SubmitLevelResultFunction(
        IPlayFabServerGateway playFab,
        IIdempotencyStore idempotencyStore,
        ICorrelationContext correlationContext,
        ILogger<SubmitLevelResultFunction> logger)
        : base(idempotencyStore, correlationContext, logger)
    {
        _playFab = playFab;
    }

    [Function("SubmitLevelResult")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData request)
    {
        // Parse request
        var (envelope, errorResponse) = await ParseRequestAsync<SubmitLevelResultRequestDto>(request);
        if (errorResponse != null) return errorResponse;

        var playerId = GetPlayerId(request, envelope);
        var playerValidation = await EnsurePlayerIdAsync<SubmitLevelResultResultDto>(
            request,
            playerId,
            envelope!.CorrelationId);
        if (playerValidation != null) return playerValidation;

        Logger.LogInformation(
            "[{CorrelationId}] SubmitLevelResult for {PlayerId}, level={LevelId}",
            envelope!.CorrelationId, playerId, envelope.Payload?.LevelId);

        return await ExecuteIdempotentAsync<SubmitLevelResultRequestDto, SubmitLevelResultResultDto>(
            request,
            envelope,
            playerId,
            async runRequest => await ProcessLevelResultAsync(playerId, envelope.CorrelationId, runRequest));
    }

    private async Task<(SubmitLevelResultResultDto? Result, ErrorPayload? Error)> ProcessLevelResultAsync(
        string playerId,
        string correlationId,
        SubmitLevelResultRequestDto runRequest)
    {
        // Validate request
        var validation = RequestValidator.ValidateSubmitLevelResult(runRequest);
        if (!validation.IsValid)
        {
            return (null, ErrorPayload.Create(
                ErrorCodes.ValidationFailed,
                "Invalid run data",
                details: validation.ToDetailsDictionary()));
        }

        // Get current progress
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

        var currentProgress = getResult.Data!;

        // Apply level result
        var mergeResult = PlayerProgressMerger.ApplyLevelResult(
            currentProgress,
            runRequest,
            gameplayBalance,
            crownEventConfig);
        if (!mergeResult.IsSuccess)
        {
            return (null, ErrorPayload.Create(
                mergeResult.ErrorCode ?? ErrorCodes.InternalError,
                mergeResult.ErrorMessage ?? "Failed to apply level result"));
        }

        var newProgress = mergeResult.NewProgress!;

        // Save progress
        var saveResult = await _playFab.SavePlayerProgressAsync(playerId, newProgress, gameplayBalance: gameplayBalance, crownEventConfig: crownEventConfig);
        if (!saveResult.IsSuccess)
        {
            return (null, ErrorPayload.Create(
                saveResult.ErrorCode ?? ErrorCodes.PlayFabError,
                saveResult.ErrorMessage ?? "Failed to save progress",
                saveResult.IsRetryable));
        }

        var latestResult = await _playFab.GetPlayerProgressAsync(playerId, gameplayBalance: gameplayBalance, crownEventConfig: crownEventConfig);
        if (latestResult.IsSuccess && latestResult.Data != null)
        {
            newProgress = latestResult.Data;
        }

        var leaderboardSyncResult = await _playFab.SyncStarsLeaderboardAsync(
            playerId,
            newProgress.Stars,
            newProgress.CurrentLevel);
        if (!leaderboardSyncResult.IsSuccess)
        {
            Logger.LogWarning(
                "[{CorrelationId}] Leaderboard sync failed for {PlayerId}: {ErrorCode} - {ErrorMessage}",
                correlationId,
                playerId,
                leaderboardSyncResult.ErrorCode,
                leaderboardSyncResult.ErrorMessage);
        }

        return (new SubmitLevelResultResultDto
        {
            Success = true,
            NewCurrentLevel = newProgress.CurrentLevel,
            UpdatedProgress = newProgress
        }, null);
    }
}
