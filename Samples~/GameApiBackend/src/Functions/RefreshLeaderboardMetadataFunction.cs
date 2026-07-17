using System.Diagnostics;
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
/// Re-stamps the caller's leaderboard metadata ("D" field) with the latest
/// PlayFab title display name. Called after a client-side display name change
/// so the leaderboard reflects the new name immediately, without waiting for
/// the next level submission.
/// </summary>
public sealed class RefreshLeaderboardMetadataFunction : FunctionBase
{
    private readonly IPlayFabServerGateway _playFab;

    public RefreshLeaderboardMetadataFunction(
        IPlayFabServerGateway playFab,
        IIdempotencyStore idempotencyStore,
        ICorrelationContext correlationContext,
        ILogger<RefreshLeaderboardMetadataFunction> logger)
        : base(idempotencyStore, correlationContext, logger)
    {
        _playFab = playFab;
    }

    [Function("RefreshLeaderboardMetadata")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData request)
    {
        var stopwatch = Stopwatch.StartNew();

        var (envelope, errorResponse) = await ParseRequestAsync<RefreshLeaderboardMetadataRequestDto>(request);
        if (errorResponse != null) return errorResponse;

        var correlationId = envelope!.CorrelationId;
        var playerId = GetPlayerId(request, envelope);
        var playerValidation = await EnsurePlayerIdAsync<RefreshLeaderboardMetadataResultDto>(
            request,
            playerId,
            correlationId,
            stopwatch.ElapsedMilliseconds);
        if (playerValidation != null) return playerValidation;

        Logger.LogInformation(
            "[{CorrelationId}] RefreshLeaderboardMetadata for {PlayerId}",
            correlationId,
            playerId);

        var progressResult = await _playFab.GetPlayerProgressAsync(playerId, autoRepair: false);
        if (!progressResult.IsSuccess || progressResult.Data == null)
        {
            return await CreateErrorResponseAsync<RefreshLeaderboardMetadataResultDto>(
                request,
                progressResult.ErrorCode ?? ErrorCodes.PlayFabError,
                progressResult.ErrorMessage ?? "Failed to read player progress",
                System.Net.HttpStatusCode.InternalServerError,
                correlationId,
                stopwatch.ElapsedMilliseconds,
                retryable: progressResult.IsRetryable);
        }

        var progress = progressResult.Data;
        var stars = Math.Max(0, progress.Stars);
        var level = Math.Max(1, progress.CurrentLevel);

        var syncResult = await _playFab.SyncStarsLeaderboardAsync(playerId, stars, level);
        if (!syncResult.IsSuccess)
        {
            return await CreateErrorResponseAsync<RefreshLeaderboardMetadataResultDto>(
                request,
                syncResult.ErrorCode ?? ErrorCodes.PlayFabError,
                syncResult.ErrorMessage ?? "Failed to refresh leaderboard metadata",
                System.Net.HttpStatusCode.InternalServerError,
                correlationId,
                stopwatch.ElapsedMilliseconds,
                retryable: syncResult.IsRetryable);
        }

        return await CreateSuccessResponseAsync(
            request,
            new RefreshLeaderboardMetadataResultDto
            {
                Success = true,
                Stars = stars,
                Level = level,
                DisplayName = string.Empty
            },
            correlationId,
            stopwatch.ElapsedMilliseconds);
    }
}
