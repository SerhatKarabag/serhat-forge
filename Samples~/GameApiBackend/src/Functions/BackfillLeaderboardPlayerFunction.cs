using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Serhat.Forge.CloudScript.Domain;
using Serhat.Forge.CloudScript.Domain.DTOs;
using Serhat.Forge.CloudScript.Infrastructure.Idempotency;
using Serhat.Forge.CloudScript.Infrastructure.Logging;
using Serhat.Forge.CloudScript.Infrastructure.PlayFab;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using PlayFab;
using PlayFab.AdminModels;
using PlayFab.ServerModels;

namespace Serhat.Forge.CloudScript.Functions;

/// <summary>
/// Backfills one player's display name and leaderboard stats from PlayerProgress.
/// Designed to be invoked from a PlayFab segment CloudScript action (one call per player).
/// </summary>
public sealed class BackfillLeaderboardPlayerFunction : FunctionBase
{
    private const int MaxDisplayNameLength = 25;
    private const int MinDisplayNameLength = 3;
    private const int DefaultRenameAttempts = 5;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IPlayFabServerGateway _playFab;

    public BackfillLeaderboardPlayerFunction(
        IPlayFabServerGateway playFab,
        IIdempotencyStore idempotencyStore,
        ICorrelationContext correlationContext,
        ILogger<BackfillLeaderboardPlayerFunction> logger)
        : base(idempotencyStore, correlationContext, logger)
    {
        _playFab = playFab;
    }

    [Function("BackfillLeaderboardPlayer")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData request)
    {
        var stopwatch = Stopwatch.StartNew();

        var (payload, parseError) = await ParsePayloadAsync(request);
        if (parseError != null)
        {
            return await CreateErrorResponseAsync<BackfillLeaderboardPlayerResultDto>(
                request,
                ErrorCodes.InvalidRequest,
                parseError,
                HttpStatusCode.BadRequest,
                string.Empty,
                stopwatch.ElapsedMilliseconds);
        }

        var correlationId = string.IsNullOrWhiteSpace(payload!.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : payload.CorrelationId.Trim();
        CorrelationContext.SetCorrelationId(correlationId);

        var playerId = payload.PlayFabId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return await CreateErrorResponseAsync<BackfillLeaderboardPlayerResultDto>(
                request,
                ErrorCodes.ValidationFailed,
                "playFabId is required.",
                HttpStatusCode.BadRequest,
                correlationId,
                stopwatch.ElapsedMilliseconds);
        }

        Logger.LogInformation(
            "[{CorrelationId}] BackfillLeaderboardPlayer start for {PlayFabId}",
            correlationId,
            playerId);

        var progressResult = await _playFab.GetPlayerProgressAsync(playerId, autoRepair: false);
        if (!progressResult.IsSuccess || progressResult.Data == null)
        {
            return await CreateErrorResponseAsync<BackfillLeaderboardPlayerResultDto>(
                request,
                progressResult.ErrorCode ?? ErrorCodes.PlayFabError,
                progressResult.ErrorMessage ?? "Failed to read PlayerProgress.",
                HttpStatusCode.InternalServerError,
                correlationId,
                stopwatch.ElapsedMilliseconds,
                progressResult.IsRetryable);
        }

        var progress = progressResult.Data;
        var stars = Math.Max(0, progress.Stars);
        var level = Math.Max(1, progress.CurrentLevel);

        var displayNameResult = await GetDisplayNameAsync(playerId);
        var previousDisplayName = displayNameResult.IsSuccess ? displayNameResult.Data ?? string.Empty : string.Empty;
        if (!displayNameResult.IsSuccess)
        {
            Logger.LogWarning(
                "[{CorrelationId}] Failed to read display name for {PlayFabId}: {ErrorCode} - {ErrorMessage}",
                correlationId,
                playerId,
                displayNameResult.ErrorCode,
                displayNameResult.ErrorMessage);
        }

        var finalDisplayName = previousDisplayName;
        var displayNameChanged = false;

        if (payload.AssignRandomDisplayName &&
            (payload.OverwriteDisplayName || string.IsNullOrWhiteSpace(previousDisplayName)))
        {
            var renameResult = await TryAssignRandomDisplayNameAsync(
                playerId,
                payload.DisplayNamePrefix,
                payload.RandomDigits,
                DefaultRenameAttempts);

            if (renameResult.IsSuccess && !string.IsNullOrWhiteSpace(renameResult.Data))
            {
                finalDisplayName = renameResult.Data!;
                displayNameChanged = !string.Equals(
                    previousDisplayName,
                    finalDisplayName,
                    StringComparison.Ordinal);
            }
            else
            {
                Logger.LogWarning(
                    "[{CorrelationId}] Display name randomization skipped/failed for {PlayFabId}: {ErrorCode} - {ErrorMessage}",
                    correlationId,
                    playerId,
                    renameResult.ErrorCode,
                    renameResult.ErrorMessage);
            }
        }

        var leaderboardSyncResult = await _playFab.SyncStarsLeaderboardAsync(playerId, stars, level);
        if (!leaderboardSyncResult.IsSuccess)
        {
            return await CreateErrorResponseAsync<BackfillLeaderboardPlayerResultDto>(
                request,
                leaderboardSyncResult.ErrorCode ?? ErrorCodes.PlayFabError,
                leaderboardSyncResult.ErrorMessage ?? "Failed to sync leaderboard.",
                HttpStatusCode.InternalServerError,
                correlationId,
                stopwatch.ElapsedMilliseconds,
                leaderboardSyncResult.IsRetryable);
        }

        var result = new BackfillLeaderboardPlayerResultDto
        {
            PlayFabId = playerId,
            Stars = stars,
            Level = level,
            PreviousDisplayName = previousDisplayName,
            DisplayName = finalDisplayName,
            DisplayNameChanged = displayNameChanged,
            LeaderboardSynced = true
        };

        Logger.LogInformation(
            "[{CorrelationId}] BackfillLeaderboardPlayer completed for {PlayFabId} (stars={Stars}, level={Level}, renamed={Renamed})",
            correlationId,
            playerId,
            stars,
            level,
            displayNameChanged);

        return await CreateSuccessResponseAsync(
            request,
            result,
            correlationId,
            stopwatch.ElapsedMilliseconds);
    }

    private async Task<(BackfillLeaderboardPlayerRequestDto? Payload, string? Error)> ParsePayloadAsync(HttpRequestData request)
    {
        var body = await request.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, "Request body is required.");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var payloadElement = root;
            if (TryGetPropertyIgnoreCase(root, "FunctionArgument", out var functionArgument) ||
                TryGetPropertyIgnoreCase(root, "FunctionParameter", out functionArgument))
            {
                payloadElement = functionArgument;
            }

            if (payloadElement.ValueKind == JsonValueKind.String)
            {
                var rawInner = payloadElement.GetString();
                if (string.IsNullOrWhiteSpace(rawInner))
                {
                    return (null, "FunctionArgument is empty.");
                }

                using var innerDocument = JsonDocument.Parse(rawInner);
                payloadElement = innerDocument.RootElement.Clone();
            }
            else
            {
                payloadElement = payloadElement.Clone();
            }

            if (payloadElement.ValueKind == JsonValueKind.Object &&
                TryGetPropertyIgnoreCase(payloadElement, "Payload", out var envelopePayload))
            {
                payloadElement = envelopePayload.Clone();
            }

            var payload = JsonSerializer.Deserialize<BackfillLeaderboardPlayerRequestDto>(
                payloadElement.GetRawText(),
                JsonOptions);

            if (payload != null && string.IsNullOrWhiteSpace(payload.PlayFabId))
            {
                if (TryResolveCallerPlayerId(root, out var callerPlayerId))
                {
                    payload.PlayFabId = callerPlayerId;
                }
                else if (TryResolveCallerPlayerId(payloadElement, out var callerPlayerIdFromPayload))
                {
                    payload.PlayFabId = callerPlayerIdFromPayload;
                }
            }

            return payload == null
                ? (null, "Failed to deserialize payload.")
                : (payload, null);
        }
        catch (JsonException)
        {
            return (null, "Invalid JSON payload.");
        }
    }

    private async Task<Serhat.Forge.CloudScript.Infrastructure.PlayFab.PlayFabResult<string>> GetDisplayNameAsync(string playFabId)
    {
        var request = new PlayFab.ServerModels.GetPlayerProfileRequest
        {
            PlayFabId = playFabId,
            ProfileConstraints = new PlayFab.ServerModels.PlayerProfileViewConstraints
            {
                ShowDisplayName = true
            }
        };

        var result = await PlayFabServerAPI.GetPlayerProfileAsync(request);
        if (result.Error != null)
        {
            var errorCode = result.Error.Error.ToString();
            var retryable = result.Error.HttpCode == 429 || result.Error.HttpCode >= 500;
            return Serhat.Forge.CloudScript.Infrastructure.PlayFab.PlayFabResult<string>.Failure(
                errorCode,
                result.Error.ErrorMessage ?? "GetPlayerProfile failed",
                retryable);
        }

        return Serhat.Forge.CloudScript.Infrastructure.PlayFab.PlayFabResult<string>.Success(result.Result?.PlayerProfile?.DisplayName ?? string.Empty);
    }

    private async Task<Serhat.Forge.CloudScript.Infrastructure.PlayFab.PlayFabResult<string>> TryAssignRandomDisplayNameAsync(
        string playFabId,
        string? prefix,
        int requestedDigits,
        int maxAttempts)
    {
        var digits = Math.Clamp(requestedDigits <= 0 ? 6 : requestedDigits, 3, 10);
        var safePrefix = NormalizeDisplayNamePrefix(prefix, MaxDisplayNameLength - digits);
        var attempts = Math.Max(1, maxAttempts);

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var candidate = BuildRandomDisplayName(safePrefix, digits);
            var request = new UpdateUserTitleDisplayNameRequest
            {
                PlayFabId = playFabId,
                DisplayName = candidate
            };

            var result = await PlayFabAdminAPI.UpdateUserTitleDisplayNameAsync(request);
            if (result.Error == null)
            {
                return Serhat.Forge.CloudScript.Infrastructure.PlayFab.PlayFabResult<string>.Success(result.Result?.DisplayName ?? candidate);
            }

            var errorCode = result.Error.Error.ToString();
            var retryable = result.Error.HttpCode == 429 || result.Error.HttpCode >= 500;
            if (!string.Equals(errorCode, "NameNotAvailable", StringComparison.OrdinalIgnoreCase) ||
                attempt >= attempts)
            {
                return Serhat.Forge.CloudScript.Infrastructure.PlayFab.PlayFabResult<string>.Failure(
                    errorCode,
                    result.Error.ErrorMessage ?? "UpdateUserTitleDisplayName failed",
                    retryable);
            }
        }

        return Serhat.Forge.CloudScript.Infrastructure.PlayFab.PlayFabResult<string>.Failure(
            ErrorCodes.PlayFabError,
            "Could not generate a unique display name.",
            isRetryable: true);
    }

    private static string NormalizeDisplayNamePrefix(string? prefix, int maxLength)
    {
        var safeMaxLength = Math.Clamp(maxLength, MinDisplayNameLength, MaxDisplayNameLength);
        var input = string.IsNullOrWhiteSpace(prefix) ? "Player" : prefix.Trim();
        var builder = new StringBuilder(Math.Min(input.Length, safeMaxLength));

        // Restrict to ASCII letters/digits only. char.IsLetterOrDigit would accept
        // Turkish characters (ş, ğ, ı, etc.) which render as tofu glyphs on the
        // leaderboard UI because our bundled font atlas only covers ASCII.
        for (var i = 0; i < input.Length && builder.Length < safeMaxLength; i++)
        {
            var c = input[i];
            if ((c >= 'a' && c <= 'z') ||
                (c >= 'A' && c <= 'Z') ||
                (c >= '0' && c <= '9'))
            {
                builder.Append(c);
            }
        }

        if (builder.Length >= MinDisplayNameLength)
        {
            return builder.ToString();
        }

        const string fallback = "Player";
        builder.Clear();
        for (var i = 0; i < fallback.Length && builder.Length < safeMaxLength; i++)
        {
            builder.Append(fallback[i]);
        }

        while (builder.Length < MinDisplayNameLength)
        {
            builder.Append('X');
        }

        return builder.ToString();
    }

    private static string BuildRandomDisplayName(string prefix, int digits)
    {
        var safeDigits = Math.Clamp(digits, 3, 10);
        var length = prefix.Length + safeDigits;
        var chars = new char[length];

        prefix.AsSpan().CopyTo(chars);
        for (var i = prefix.Length; i < length; i++)
        {
            chars[i] = (char)('0' + Random.Shared.Next(0, 10));
        }

        return new string(chars);
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static bool TryResolveCallerPlayerId(JsonElement root, out string playerId)
    {
        playerId = string.Empty;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (TryGetPropertyIgnoreCase(root, "Caller", out var callerElement) &&
            callerElement.ValueKind == JsonValueKind.Object)
        {
            if (TryGetPropertyIgnoreCase(callerElement, "PlayerId", out var callerPlayerIdElement) &&
                callerPlayerIdElement.ValueKind == JsonValueKind.String)
            {
                var value = callerPlayerIdElement.GetString()?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    playerId = value;
                    return true;
                }
            }

            if (TryGetPropertyIgnoreCase(callerElement, "UserId", out var callerUserIdElement) &&
                callerUserIdElement.ValueKind == JsonValueKind.String)
            {
                var value = callerUserIdElement.GetString()?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    playerId = value;
                    return true;
                }
            }
        }

        return false;
    }
}
