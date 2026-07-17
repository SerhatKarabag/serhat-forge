using System.Diagnostics;
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
/// Returns leaderboard page + current player row for World/Country scopes.
/// </summary>
public sealed class GetLeaderboardFunction : FunctionBase
{
    private readonly IPlayFabServerGateway _playFab;

    public GetLeaderboardFunction(
        IPlayFabServerGateway playFab,
        IIdempotencyStore idempotencyStore,
        ICorrelationContext correlationContext,
        ILogger<GetLeaderboardFunction> logger)
        : base(idempotencyStore, correlationContext, logger)
    {
        _playFab = playFab;
    }

    [Function("GetLeaderboard")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData request)
    {
        var stopwatch = Stopwatch.StartNew();

        var (envelope, errorResponse) = await ParseRequestAsync<GetLeaderboardRequestDto>(request);
        if (errorResponse != null) return errorResponse;

        var correlationId = envelope!.CorrelationId;
        var playerId = GetPlayerId(request, envelope);
        var playerValidation = await EnsurePlayerIdAsync<GetLeaderboardResultDto>(
            request,
            playerId,
            correlationId,
            stopwatch.ElapsedMilliseconds);
        if (playerValidation != null) return playerValidation;

        var payload = envelope.Payload ?? new GetLeaderboardRequestDto();
        var validation = RequestValidator.ValidateGetLeaderboard(payload);
        if (!validation.IsValid)
        {
            return await CreateErrorResponseAsync<GetLeaderboardResultDto>(
                request,
                ErrorCodes.ValidationFailed,
                "Invalid leaderboard request",
                System.Net.HttpStatusCode.BadRequest,
                correlationId,
                stopwatch.ElapsedMilliseconds,
                details: validation.ToDetailsDictionary());
        }

        var countryOnly = string.Equals(
            payload.Scope,
            LeaderboardScopes.Country,
            StringComparison.OrdinalIgnoreCase);

        Logger.LogInformation(
            "[{CorrelationId}] GetLeaderboard for {PlayerId}, scope={Scope}, pageSize={PageSize}, start={Start}",
            correlationId,
            playerId,
            countryOnly ? LeaderboardScopes.Country : LeaderboardScopes.World,
            payload.PageSize,
            payload.StartingPosition);

        var result = await _playFab.GetStarsLeaderboardAsync(
            playerId,
            countryOnly,
            payload.PageSize,
            payload.StartingPosition);

        if (!result.IsSuccess || result.Data == null)
        {
            return await CreateErrorResponseAsync<GetLeaderboardResultDto>(
                request,
                result.ErrorCode ?? ErrorCodes.PlayFabError,
                result.ErrorMessage ?? "Failed to get leaderboard",
                System.Net.HttpStatusCode.InternalServerError,
                correlationId,
                stopwatch.ElapsedMilliseconds,
                retryable: result.IsRetryable);
        }

        return await CreateSuccessResponseAsync(
            request,
            result.Data,
            correlationId,
            stopwatch.ElapsedMilliseconds);
    }
}
