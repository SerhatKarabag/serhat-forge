using Serhat.Forge.CloudScript.Domain.DTOs;
using Serhat.Forge.CloudScript.Infrastructure.PlayFab;
using Microsoft.Extensions.Logging;

namespace Serhat.Forge.CloudScript.Domain;

/// <summary>
/// Loads gameplay balance from PlayFab Title Data with safe fallback behavior.
/// </summary>
public static class GameplayBalanceProvider
{
    public static async Task<GameplayBalanceConfigDto> GetAsync(
        IPlayFabServerGateway playFab,
        ILogger logger,
        string correlationId,
        CancellationToken ct = default)
    {
        var titleDataResult = await playFab.GetTitleDataAsync(GameplayBalanceTitleDataParser.TitleDataKey, ct);
        if (!titleDataResult.IsSuccess)
        {
            logger.LogWarning(
                "[{CorrelationId}] Failed to read gameplay balance title data '{TitleDataKey}': {ErrorCode} - {ErrorMessage}",
                correlationId,
                GameplayBalanceTitleDataParser.TitleDataKey,
                titleDataResult.ErrorCode,
                titleDataResult.ErrorMessage);
            return new GameplayBalanceConfigDto();
        }

        if (!GameplayBalanceTitleDataParser.TryParse(titleDataResult.Data, out var balance, out var error))
        {
            logger.LogWarning(
                "[{CorrelationId}] Invalid gameplay balance title data '{TitleDataKey}': {Error}",
                correlationId,
                GameplayBalanceTitleDataParser.TitleDataKey,
                error);
            return new GameplayBalanceConfigDto();
        }

        return balance;
    }
}
