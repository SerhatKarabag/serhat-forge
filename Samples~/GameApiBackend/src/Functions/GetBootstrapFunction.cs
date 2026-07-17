using System.Diagnostics;
using System.Net;
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
/// Function to get bootstrap data (player progress).
/// </summary>
public sealed class GetBootstrapFunction : FunctionBase
{
    private readonly IPlayFabServerGateway _playFab;

    public GetBootstrapFunction(
        IPlayFabServerGateway playFab,
        IIdempotencyStore idempotencyStore,
        ICorrelationContext correlationContext,
        ILogger<GetBootstrapFunction> logger)
        : base(idempotencyStore, correlationContext, logger)
    {
        _playFab = playFab;
    }

    [Function("GetBootstrap")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData request)
    {
        var stopwatch = Stopwatch.StartNew();

        // Parse request
        var (envelope, errorResponse) = await ParseRequestAsync<EmptyPayload>(request);
        if (errorResponse != null) return errorResponse;

        var correlationId = envelope!.CorrelationId;
        var playerId = GetPlayerId(request, envelope);
        var playerValidation = await EnsurePlayerIdAsync<BootstrapDto>(request, playerId, correlationId, stopwatch.ElapsedMilliseconds);
        if (playerValidation != null) return playerValidation;

        Logger.LogInformation("[{CorrelationId}] GetBootstrap for {PlayerId}", correlationId, playerId);

        var versionRequirement = await TryGetVersionRequirementAsync(envelope);
        if (versionRequirement != null)
        {
            Logger.LogWarning(
                "[{CorrelationId}] Force update required for {PlayerId}. Current={CurrentVersion}, Min={MinVersion}, Platform={Platform}",
                correlationId,
                playerId,
                versionRequirement.CurrentVersion,
                versionRequirement.MinimumSupportedVersion,
                versionRequirement.Platform);

            return await CreateErrorResponseAsync<BootstrapDto>(
                request,
                ErrorCodes.VersionMismatch,
                versionRequirement.Message,
                HttpStatusCode.Conflict,
                correlationId,
                stopwatch.ElapsedMilliseconds,
                retryable: false,
                details: versionRequirement.ToErrorDetails());
        }

        var gameplayBalance = await GetGameplayBalanceAsync(correlationId);
        var crownEventConfig = await CrownEventConfigProvider.GetAsync(_playFab, Logger, correlationId);
        var dailyGiftConfig = await DailyGiftConfigProvider.GetAsync(_playFab, Logger, correlationId);

        // Get player progress from PlayFab
        var progressResult = await _playFab.GetPlayerProgressAsync(
            playerId,
            gameplayBalance: gameplayBalance, crownEventConfig: crownEventConfig);

        if (!progressResult.IsSuccess)
        {
            return await CreateErrorResponseAsync<BootstrapDto>(
                request,
                progressResult.ErrorCode ?? ErrorCodes.PlayFabError,
                progressResult.ErrorMessage ?? "Failed to get player progress",
                System.Net.HttpStatusCode.InternalServerError,
                correlationId,
                stopwatch.ElapsedMilliseconds,
                progressResult.IsRetryable);
        }

        var bootstrap = new BootstrapDto
        {
            Progress = progressResult.Data!,
            Economy = new EconomyConfigDto
            {
                CoinCostPerLife = PlayerProgressMerger.CurrentCoinCostPerLife,
                BoosterOffers = PlayerProgressMerger.GetCurrentBoosterOffers(),
                StartBoosterOffers = PlayerProgressMerger.GetCurrentStartBoosterOffers()
            },
            CrownEvent = PlayerProgressMerger.GetCurrentCrownEventConfig(crownEventConfig),
            GameplayBalance = gameplayBalance,
            DailyGift = dailyGiftConfig
        };

        return await CreateSuccessResponseAsync(
            request, bootstrap, correlationId, stopwatch.ElapsedMilliseconds);
    }

    private async Task<GameplayBalanceConfigDto> GetGameplayBalanceAsync(string correlationId)
    {
        return await GameplayBalanceProvider.GetAsync(_playFab, Logger, correlationId);
    }

    private async Task<ClientVersionRequirement?> TryGetVersionRequirementAsync(RequestEnvelope<EmptyPayload> envelope)
    {
        if (envelope?.Caller == null)
        {
            Logger.LogWarning("[{CorrelationId}] Force update check skipped because caller context is missing.", envelope?.CorrelationId);
            return null;
        }

        if (!ClientVersionPolicyEvaluator.IsManagedPlatform(envelope.Caller.Platform))
        {
            return null;
        }

        var titleDataResult = await _playFab.GetTitleDataAsync(ClientVersionPolicyEvaluator.TitleDataKey);
        if (!titleDataResult.IsSuccess)
        {
            Logger.LogWarning(
                "[{CorrelationId}] Failed to read title data key {TitleDataKey}: {ErrorCode} - {ErrorMessage}",
                envelope.CorrelationId,
                ClientVersionPolicyEvaluator.TitleDataKey,
                titleDataResult.ErrorCode,
                titleDataResult.ErrorMessage);
            return null;
        }

        if (string.IsNullOrWhiteSpace(titleDataResult.Data))
        {
            Logger.LogWarning(
                "[{CorrelationId}] Force update check skipped because title data key '{TitleDataKey}' is missing or empty.",
                envelope.CorrelationId,
                ClientVersionPolicyEvaluator.TitleDataKey);
            return null;
        }

        if (ClientVersionPolicyEvaluator.TryEvaluate(
                titleDataResult.Data,
                envelope.Caller.Platform,
                envelope.Caller.AppVersion,
                out var requirement,
                out var validationError))
        {
            Logger.LogWarning(
                "[{CorrelationId}] Force update check matched. Current={CurrentVersion}, Min={MinVersion}, Platform={Platform}",
                envelope.CorrelationId,
                requirement!.CurrentVersion,
                requirement.MinimumSupportedVersion,
                requirement.Platform);
            return requirement;
        }

        return null;
    }
}

/// <summary>
/// Empty payload for parameterless operations.
/// </summary>
public sealed class EmptyPayload { }
