using Serhat.Forge.CloudScript.Domain.DTOs;
using Serhat.Forge.CloudScript.Infrastructure.PlayFab;
using Microsoft.Extensions.Logging;

namespace Serhat.Forge.CloudScript.Domain;

/// <summary>
/// Loads daily-gift configuration from PlayFab Title Data with safe fallback behavior.
/// </summary>
public static class DailyGiftConfigProvider
{
    public static async Task<DailyGiftConfigDto> GetAsync(
        IPlayFabServerGateway playFab,
        ILogger logger,
        string correlationId,
        CancellationToken ct = default)
    {
        var fallback = PlayerProgressMerger.GetCurrentDailyGiftConfig();
        var titleDataResult = await playFab.GetTitleDataAsync(DailyGiftTitleDataParser.TitleDataKey, ct);
        if (!titleDataResult.IsSuccess)
        {
            logger.LogWarning(
                "[{CorrelationId}] Failed to read daily-gift title data '{TitleDataKey}': {ErrorCode} - {ErrorMessage}",
                correlationId,
                DailyGiftTitleDataParser.TitleDataKey,
                titleDataResult.ErrorCode,
                titleDataResult.ErrorMessage);
            return fallback;
        }

        if (!DailyGiftTitleDataParser.TryParse(titleDataResult.Data, out var parsedConfig, out var error))
        {
            logger.LogWarning(
                "[{CorrelationId}] Invalid daily-gift title data '{TitleDataKey}': {Error}",
                correlationId,
                DailyGiftTitleDataParser.TitleDataKey,
                error);
            return fallback;
        }

        if (parsedConfig.Rewards == null || parsedConfig.Rewards.Count == 0)
        {
            return fallback;
        }

        return PlayerProgressMerger.GetCurrentDailyGiftConfig(parsedConfig);
    }
}
