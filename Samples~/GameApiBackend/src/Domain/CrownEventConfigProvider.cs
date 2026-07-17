using Serhat.Forge.CloudScript.Domain.DTOs;
using Serhat.Forge.CloudScript.Infrastructure.PlayFab;
using Microsoft.Extensions.Logging;

namespace Serhat.Forge.CloudScript.Domain;

/// <summary>
/// Loads crown-event configuration from PlayFab Title Data with safe fallback behavior.
/// </summary>
public static class CrownEventConfigProvider
{
    public static async Task<CrownEventConfigDto> GetAsync(
        IPlayFabServerGateway playFab,
        ILogger logger,
        string correlationId,
        CancellationToken ct = default)
    {
        var fallback = PlayerProgressMerger.GetCurrentCrownEventConfig();
        var titleDataResult = await playFab.GetTitleDataAsync(CrownEventTitleDataParser.TitleDataKey, ct);
        if (!titleDataResult.IsSuccess)
        {
            logger.LogWarning(
                "[{CorrelationId}] Failed to read crown event title data '{TitleDataKey}': {ErrorCode} - {ErrorMessage}",
                correlationId,
                CrownEventTitleDataParser.TitleDataKey,
                titleDataResult.ErrorCode,
                titleDataResult.ErrorMessage);
            return fallback;
        }

        if (!CrownEventTitleDataParser.TryParse(titleDataResult.Data, out var crownEventConfig, out var error))
        {
            logger.LogWarning(
                "[{CorrelationId}] Invalid crown event title data '{TitleDataKey}': {Error}",
                correlationId,
                CrownEventTitleDataParser.TitleDataKey,
                error);
            return fallback;
        }

        if (crownEventConfig.Milestones == null || crownEventConfig.Milestones.Count == 0)
        {
            return fallback;
        }

        return PlayerProgressMerger.GetCurrentCrownEventConfig(crownEventConfig);
    }
}
