using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serhat.Forge.CloudScript.Domain;
using Serhat.Forge.CloudScript.Domain.DTOs;
using Microsoft.Extensions.Logging;
using PlayFab;
using PlayFab.AuthenticationModels;
using PlayFab.ServerModels;

namespace Serhat.Forge.CloudScript.Infrastructure.PlayFab;

public sealed partial class PlayFabServerGateway
{
    private const string DefaultWorldStatisticName = "STARS_TOTAL_V2";
    private const string DefaultWorldLeaderboardName = "LB_STARS_WORLD";
    private const string DefaultCountryLeaderboardPrefix = "LB_STARS_CC_";
    private const string DefaultCountryCode = "ZZ";
    private const string StatisticColumnName = "Stars";
    private const string LeaderboardColumnName = "Stars";
    private const string TitlePlayerEntityType = "title_player_account";
    private const int DefaultLeaderboardSizeLimit = 200000;
    private const int DefaultMaxQueryableVersions = 1;
    private const int MaxLeaderboardPageSize = 1000;
    private const int MaxSafeResponsePageSize = 100;
    private const int ProgressionApiMaxLeaderboardPageSize = 100;
    private const int MaxLeaderboardMetadataLength = 50;

    private static readonly HttpClient ProgressionApiClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private static readonly JsonSerializerOptions ProgressionJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly JsonSerializerOptions LeaderboardMetadataJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly SemaphoreSlim _entityTokenLock = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _ensuredCountryLeaderboards = new(StringComparer.OrdinalIgnoreCase);
    private string _entityToken = string.Empty;
    private DateTime _entityTokenExpiresUtc = DateTime.MinValue;
    private int _worldDefinitionsReady;

    public async Task<PlayFabResult<bool>> SyncStarsLeaderboardAsync(
        string playFabId,
        int stars,
        int currentLevel,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(playFabId))
        {
            return PlayFabResult<bool>.Failure(ErrorCodes.ValidationFailed, "PlayerId is required.");
        }

        var normalizedPlayerId = playFabId.Trim();
        var normalizedStars = Math.Max(0, stars);
        var normalizedLevel = Math.Max(1, currentLevel);
        var worldStatisticName = GetWorldStatisticName();
        var worldLeaderboardName = GetWorldLeaderboardName();

        var worldEnsureResult = await EnsureWorldDefinitionsAsync(worldStatisticName, worldLeaderboardName, ct);
        if (!worldEnsureResult.IsSuccess)
        {
            _logger.LogWarning(
                "Could not ensure world leaderboard definitions for {PlayFabId}: {ErrorCode} - {ErrorMessage}",
                normalizedPlayerId,
                worldEnsureResult.ErrorCode,
                worldEnsureResult.ErrorMessage);
        }

        var accountDisplayName = await TryResolveDisplayNameFromAccountInfoAsync(normalizedPlayerId, ct);

        var countryCode = await ResolveCountryCodeAsync(normalizedPlayerId, ct);
        var metadata = BuildLeaderboardMetadata(normalizedLevel, accountDisplayName);

        var worldUpdateResult = await UpdateStatisticAsync(normalizedPlayerId, worldStatisticName, normalizedStars, metadata, ct);
        if (!worldUpdateResult.IsSuccess)
        {
            return worldUpdateResult;
        }

        var countryLeaderboardName = BuildCountryLeaderboardName(countryCode);
        var ensureCountryResult = await EnsureCountryLeaderboardDefinitionAsync(countryLeaderboardName, ct);
        if (!ensureCountryResult.IsSuccess)
        {
            _logger.LogWarning(
                "Could not ensure country leaderboard definition {LeaderboardName}: {ErrorCode} - {ErrorMessage}",
                countryLeaderboardName,
                ensureCountryResult.ErrorCode,
                ensureCountryResult.ErrorMessage);
            return PlayFabResult<bool>.Success(true);
        }

        var countryUpdateResult = await UpdateCountryLeaderboardEntryAsync(
            countryLeaderboardName,
            normalizedPlayerId,
            normalizedStars,
            metadata,
            ct);

        if (!countryUpdateResult.IsSuccess)
        {
            _logger.LogWarning(
                "Country leaderboard update failed for {PlayFabId} on {LeaderboardName}: {ErrorCode} - {ErrorMessage}",
                normalizedPlayerId,
                countryLeaderboardName,
                countryUpdateResult.ErrorCode,
                countryUpdateResult.ErrorMessage);
        }

        return PlayFabResult<bool>.Success(true);
    }

    public async Task<PlayFabResult<GetLeaderboardResultDto>> GetStarsLeaderboardAsync(
        string playFabId,
        bool countryOnly,
        int pageSize,
        int startingPosition,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(playFabId))
        {
            return PlayFabResult<GetLeaderboardResultDto>.Failure(ErrorCodes.ValidationFailed, "PlayerId is required.");
        }

        if (pageSize < 1 || pageSize > MaxLeaderboardPageSize)
        {
            return PlayFabResult<GetLeaderboardResultDto>.Failure(
                ErrorCodes.ValidationFailed,
                $"PageSize must be between 1 and {MaxLeaderboardPageSize}.");
        }

        if (startingPosition < 1)
        {
            return PlayFabResult<GetLeaderboardResultDto>.Failure(ErrorCodes.ValidationFailed, "StartingPosition must be >= 1.");
        }

        var effectivePageSize = Math.Min(pageSize, MaxSafeResponsePageSize);
        var normalizedPlayerId = playFabId.Trim();
        if (effectivePageSize != pageSize)
        {
            _logger.LogWarning(
                "Requested leaderboard page size {RequestedPageSize} is above safe response limit {SafeLimit}. Capping to {EffectivePageSize}.",
                pageSize,
                MaxSafeResponsePageSize,
                effectivePageSize);
        }

        var worldStatisticName = GetWorldStatisticName();
        var worldLeaderboardName = GetWorldLeaderboardName();
        var countryCode = await ResolveCountryCodeAsync(normalizedPlayerId, ct);
        var leaderboardName = countryOnly ? BuildCountryLeaderboardName(countryCode) : worldLeaderboardName;

        if (countryOnly)
        {
            var ensureCountryResult = await EnsureCountryLeaderboardDefinitionAsync(leaderboardName, ct);
            if (!ensureCountryResult.IsSuccess)
            {
                _logger.LogWarning(
                    "Unable to ensure country leaderboard {LeaderboardName}: {ErrorCode} - {ErrorMessage}",
                    leaderboardName,
                    ensureCountryResult.ErrorCode,
                    ensureCountryResult.ErrorMessage);
            }
        }
        else
        {
            var ensureWorldResult = await EnsureWorldDefinitionsAsync(worldStatisticName, worldLeaderboardName, ct);
            if (!ensureWorldResult.IsSuccess)
            {
                _logger.LogWarning(
                    "Unable to ensure world leaderboard definitions: {ErrorCode} - {ErrorMessage}",
                    ensureWorldResult.ErrorCode,
                    ensureWorldResult.ErrorMessage);
            }
        }

        var leaderboardResponse = await GetLeaderboardRangeAsync(leaderboardName, effectivePageSize, startingPosition, ct);
        if (!leaderboardResponse.IsSuccess)
        {
            return PlayFabResult<GetLeaderboardResultDto>.Failure(
                leaderboardResponse.ErrorCode ?? ErrorCodes.PlayFabError,
                leaderboardResponse.ErrorMessage ?? "Failed to get leaderboard page",
                leaderboardResponse.IsRetryable);
        }

        var meResponse = await GetLeaderboardForEntityAsync(leaderboardName, normalizedPlayerId, ct);
        if (!meResponse.IsSuccess)
        {
            _logger.LogWarning(
                "GetLeaderboardForEntities failed for {PlayFabId} on {LeaderboardName}: {ErrorCode} - {ErrorMessage}",
                normalizedPlayerId,
                leaderboardName,
                meResponse.ErrorCode,
                meResponse.ErrorMessage);
        }

        var responsePayload = leaderboardResponse.Data ?? new GetEntityLeaderboardResponsePayload();
        var responseRankings = responsePayload.Rankings ?? new List<EntityLeaderboardEntryPayload>();
        var topEntries = BuildLeaderboardRows(
            responseRankings,
            normalizedPlayerId,
            fallbackRankStart: startingPosition);

        var meRankings = meResponse.Data?.Rankings ?? new List<EntityLeaderboardEntryPayload>();
        var meEntry = BuildCurrentPlayerRow(
            meRankings,
            normalizedPlayerId);

        var latestProgressResult = await GetPlayerProgressAsync(normalizedPlayerId, ct, autoRepair: false);
        if (latestProgressResult.IsSuccess && latestProgressResult.Data != null)
        {
            var progress = latestProgressResult.Data;
            if (meEntry == null)
            {
                meEntry = new LeaderboardRowDto
                {
                    PlayerId = normalizedPlayerId,
                    DisplayName = string.Empty,
                    Rank = 0,
                    Stars = Math.Max(0, progress.Stars),
                    Level = Math.Max(1, progress.CurrentLevel),
                    IsMe = true
                };
            }
            else
            {
                meEntry.Stars = meEntry.Stars <= 0 ? Math.Max(0, progress.Stars) : meEntry.Stars;
                meEntry.Level = meEntry.Level <= 0 ? Math.Max(1, progress.CurrentLevel) : meEntry.Level;
            }
        }

        return PlayFabResult<GetLeaderboardResultDto>.Success(new GetLeaderboardResultDto
        {
            Scope = countryOnly ? LeaderboardScopes.Country : LeaderboardScopes.World,
            CountryCode = countryCode,
            StartingPosition = startingPosition,
            PageSize = effectivePageSize,
            EntryCount = responsePayload.EntryCount,
            TopEntries = topEntries,
            MeEntry = meEntry
        });
    }

    private async Task<PlayFabResult<GetEntityLeaderboardResponsePayload>> GetLeaderboardPageAsync(
        string leaderboardName,
        int pageSize,
        int startingPosition,
        CancellationToken ct)
    {
        var request = new GetEntityLeaderboardRequestPayload
        {
            LeaderboardName = leaderboardName,
            PageSize = pageSize,
            StartingPosition = startingPosition
        };

        return await CallProgressionApiAsync<GetEntityLeaderboardRequestPayload, GetEntityLeaderboardResponsePayload>(
            "/Leaderboard/GetLeaderboard",
            request,
            ct);
    }

    private async Task<PlayFabResult<GetEntityLeaderboardResponsePayload>> GetLeaderboardRangeAsync(
        string leaderboardName,
        int pageSize,
        int startingPosition,
        CancellationToken ct)
    {
        if (pageSize < 1 || pageSize > MaxLeaderboardPageSize)
        {
            return PlayFabResult<GetEntityLeaderboardResponsePayload>.Failure(
                ErrorCodes.ValidationFailed,
                $"PageSize must be between 1 and {MaxLeaderboardPageSize}.");
        }

        var remaining = pageSize;
        var currentStartingPosition = startingPosition;
        var entryCount = 0;
        var rankings = new List<EntityLeaderboardEntryPayload>(pageSize);

        while (remaining > 0)
        {
            ct.ThrowIfCancellationRequested();

            var chunkSize = Math.Min(remaining, ProgressionApiMaxLeaderboardPageSize);
            var chunkResponse = await GetLeaderboardPageAsync(leaderboardName, chunkSize, currentStartingPosition, ct);
            if (!chunkResponse.IsSuccess)
            {
                if (rankings.Count == 0)
                {
                    return PlayFabResult<GetEntityLeaderboardResponsePayload>.Failure(
                        chunkResponse.ErrorCode ?? ErrorCodes.PlayFabError,
                        chunkResponse.ErrorMessage ?? "Failed to get leaderboard page",
                        chunkResponse.IsRetryable);
                }

                _logger.LogWarning(
                    "Partial leaderboard response for {LeaderboardName}. Requested {RequestedPageSize} entries from {StartingPosition}, loaded {LoadedCount} before failure: {ErrorCode} - {ErrorMessage}",
                    leaderboardName,
                    pageSize,
                    startingPosition,
                    rankings.Count,
                    chunkResponse.ErrorCode,
                    chunkResponse.ErrorMessage);
                break;
            }

            var payload = chunkResponse.Data ?? new GetEntityLeaderboardResponsePayload();
            if (entryCount <= 0)
            {
                entryCount = payload.EntryCount;
            }

            var chunkRankings = payload.Rankings ?? new List<EntityLeaderboardEntryPayload>();
            if (chunkRankings.Count == 0)
            {
                break;
            }

            rankings.AddRange(chunkRankings);
            remaining -= chunkRankings.Count;

            if (chunkRankings.Count < chunkSize)
            {
                break;
            }

            currentStartingPosition += chunkRankings.Count;
        }

        return PlayFabResult<GetEntityLeaderboardResponsePayload>.Success(new GetEntityLeaderboardResponsePayload
        {
            EntryCount = entryCount,
            Rankings = rankings
        });
    }

    private async Task<PlayFabResult<GetEntityLeaderboardResponsePayload>> GetLeaderboardForEntityAsync(
        string leaderboardName,
        string playFabId,
        CancellationToken ct)
    {
        var request = new GetLeaderboardForEntitiesRequestPayload
        {
            LeaderboardName = leaderboardName,
            EntityIds = new List<string> { playFabId }
        };

        return await CallProgressionApiAsync<GetLeaderboardForEntitiesRequestPayload, GetEntityLeaderboardResponsePayload>(
            "/Leaderboard/GetLeaderboardForEntities",
            request,
            ct);
    }

    private async Task<PlayFabResult<bool>> UpdateStatisticAsync(
        string playFabId,
        string statisticName,
        int stars,
        string metadata,
        CancellationToken ct)
    {
        var request = new UpdateStatisticsRequestPayload
        {
            Entity = new ProgressionEntityKeyPayload
            {
                Id = playFabId,
                Type = TitlePlayerEntityType
            },
            Statistics = new List<StatisticUpdatePayload>
            {
                new()
                {
                    Name = statisticName,
                    Scores = new List<string> { stars.ToString(CultureInfo.InvariantCulture) },
                    Metadata = metadata
                }
            }
        };

        var result = await CallProgressionApiAsync<UpdateStatisticsRequestPayload, EmptyProgressionResponsePayload>(
            "/Statistic/UpdateStatistics",
            request,
            ct);

        return result.IsSuccess
            ? PlayFabResult<bool>.Success(true)
            : PlayFabResult<bool>.Failure(
                result.ErrorCode ?? ErrorCodes.PlayFabError,
                result.ErrorMessage ?? "Failed to update statistic",
                result.IsRetryable);
    }

    private async Task<PlayFabResult<bool>> UpdateCountryLeaderboardEntryAsync(
        string leaderboardName,
        string playFabId,
        int stars,
        string metadata,
        CancellationToken ct)
    {
        var request = new UpdateLeaderboardEntriesRequestPayload
        {
            LeaderboardName = leaderboardName,
            Entries = new List<LeaderboardEntryUpdatePayload>
            {
                new()
                {
                    EntityId = playFabId,
                    Scores = new List<string> { stars.ToString(CultureInfo.InvariantCulture) },
                    Metadata = metadata
                }
            }
        };

        var result = await CallProgressionApiAsync<UpdateLeaderboardEntriesRequestPayload, EmptyProgressionResponsePayload>(
            "/Leaderboard/UpdateLeaderboardEntries",
            request,
            ct);

        return result.IsSuccess
            ? PlayFabResult<bool>.Success(true)
            : PlayFabResult<bool>.Failure(
                result.ErrorCode ?? ErrorCodes.PlayFabError,
                result.ErrorMessage ?? "Failed to update leaderboard entry",
                result.IsRetryable);
    }

    private async Task<PlayFabResult<bool>> EnsureWorldDefinitionsAsync(
        string statisticName,
        string leaderboardName,
        CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _worldDefinitionsReady, 1, 1) == 1)
        {
            return PlayFabResult<bool>.Success(true);
        }

        var ensureStatisticResult = await EnsureStatisticDefinitionAsync(statisticName, ct);
        if (!ensureStatisticResult.IsSuccess)
        {
            return ensureStatisticResult;
        }

        var ensureLeaderboardResult = await EnsureWorldLeaderboardDefinitionAsync(leaderboardName, statisticName, ct);
        if (!ensureLeaderboardResult.IsSuccess)
        {
            return ensureLeaderboardResult;
        }

        Interlocked.Exchange(ref _worldDefinitionsReady, 1);
        return PlayFabResult<bool>.Success(true);
    }

    private async Task<PlayFabResult<bool>> EnsureStatisticDefinitionAsync(
        string statisticName,
        CancellationToken ct)
    {
        var getRequest = new NameRequestPayload { Name = statisticName };
        var getResult = await CallProgressionApiAsync<NameRequestPayload, StatisticDefinitionResponsePayload>(
            "/Statistic/GetStatisticDefinition",
            getRequest,
            ct);
        if (getResult.IsSuccess)
        {
            return PlayFabResult<bool>.Success(true);
        }

        if (!IsDefinitionMissingError(getResult))
        {
            return PlayFabResult<bool>.Failure(
                getResult.ErrorCode ?? ErrorCodes.PlayFabError,
                getResult.ErrorMessage ?? "Failed to get statistic definition",
                getResult.IsRetryable);
        }

        var createRequest = new CreateStatisticDefinitionRequestPayload
        {
            Name = statisticName,
            EntityType = TitlePlayerEntityType,
            VersionConfiguration = new VersionConfigurationPayload
            {
                MaxQueryableVersions = DefaultMaxQueryableVersions,
                ResetInterval = "Manual"
            },
            Columns = new List<StatisticColumnPayload>
            {
                new()
                {
                    Name = StatisticColumnName,
                    AggregationMethod = "Last"
                }
            }
        };

        var createResult = await CallProgressionApiAsync<CreateStatisticDefinitionRequestPayload, EmptyProgressionResponsePayload>(
            "/Statistic/CreateStatisticDefinition",
            createRequest,
            ct);

        return createResult.IsSuccess
            ? PlayFabResult<bool>.Success(true)
            : PlayFabResult<bool>.Failure(
                createResult.ErrorCode ?? ErrorCodes.PlayFabError,
                createResult.ErrorMessage ?? "Failed to create statistic definition",
                createResult.IsRetryable);
    }

    private async Task<PlayFabResult<bool>> EnsureWorldLeaderboardDefinitionAsync(
        string leaderboardName,
        string statisticName,
        CancellationToken ct)
    {
        var getRequest = new NameRequestPayload { Name = leaderboardName };
        var getResult = await CallProgressionApiAsync<NameRequestPayload, LeaderboardDefinitionResponsePayload>(
            "/Leaderboard/GetLeaderboardDefinition",
            getRequest,
            ct);
        if (getResult.IsSuccess)
        {
            return PlayFabResult<bool>.Success(true);
        }

        if (!IsDefinitionMissingError(getResult))
        {
            return PlayFabResult<bool>.Failure(
                getResult.ErrorCode ?? ErrorCodes.PlayFabError,
                getResult.ErrorMessage ?? "Failed to get leaderboard definition",
                getResult.IsRetryable);
        }

        var createRequest = new CreateLeaderboardDefinitionRequestPayload
        {
            Name = leaderboardName,
            EntityType = TitlePlayerEntityType,
            SizeLimit = GetLeaderboardSizeLimit(),
            VersionConfiguration = new VersionConfigurationPayload
            {
                MaxQueryableVersions = DefaultMaxQueryableVersions,
                ResetInterval = "Manual"
            },
            Columns = new List<LeaderboardColumnPayload>
            {
                new()
                {
                    Name = LeaderboardColumnName,
                    SortDirection = "Descending",
                    LinkedStatisticColumn = new LinkedStatisticColumnPayload
                    {
                        LinkedStatisticName = statisticName,
                        LinkedStatisticColumnName = StatisticColumnName
                    }
                }
            }
        };

        var createResult = await CallProgressionApiAsync<CreateLeaderboardDefinitionRequestPayload, EmptyProgressionResponsePayload>(
            "/Leaderboard/CreateLeaderboardDefinition",
            createRequest,
            ct);

        return createResult.IsSuccess
            ? PlayFabResult<bool>.Success(true)
            : PlayFabResult<bool>.Failure(
                createResult.ErrorCode ?? ErrorCodes.PlayFabError,
                createResult.ErrorMessage ?? "Failed to create world leaderboard definition",
                createResult.IsRetryable);
    }

    private async Task<PlayFabResult<bool>> EnsureCountryLeaderboardDefinitionAsync(
        string leaderboardName,
        CancellationToken ct)
    {
        if (_ensuredCountryLeaderboards.ContainsKey(leaderboardName))
        {
            return PlayFabResult<bool>.Success(true);
        }

        var getRequest = new NameRequestPayload { Name = leaderboardName };
        var getResult = await CallProgressionApiAsync<NameRequestPayload, LeaderboardDefinitionResponsePayload>(
            "/Leaderboard/GetLeaderboardDefinition",
            getRequest,
            ct);
        if (getResult.IsSuccess)
        {
            _ensuredCountryLeaderboards.TryAdd(leaderboardName, 0);
            return PlayFabResult<bool>.Success(true);
        }

        if (!IsDefinitionMissingError(getResult))
        {
            return PlayFabResult<bool>.Failure(
                getResult.ErrorCode ?? ErrorCodes.PlayFabError,
                getResult.ErrorMessage ?? "Failed to read country leaderboard definition",
                getResult.IsRetryable);
        }

        var createRequest = new CreateLeaderboardDefinitionRequestPayload
        {
            Name = leaderboardName,
            EntityType = TitlePlayerEntityType,
            SizeLimit = GetLeaderboardSizeLimit(),
            VersionConfiguration = new VersionConfigurationPayload
            {
                MaxQueryableVersions = DefaultMaxQueryableVersions,
                ResetInterval = "Manual"
            },
            Columns = new List<LeaderboardColumnPayload>
            {
                new()
                {
                    Name = LeaderboardColumnName,
                    SortDirection = "Descending"
                }
            }
        };

        var createResult = await CallProgressionApiAsync<CreateLeaderboardDefinitionRequestPayload, EmptyProgressionResponsePayload>(
            "/Leaderboard/CreateLeaderboardDefinition",
            createRequest,
            ct);

        if (createResult.IsSuccess)
        {
            _ensuredCountryLeaderboards.TryAdd(leaderboardName, 0);
            return PlayFabResult<bool>.Success(true);
        }

        return PlayFabResult<bool>.Failure(
            createResult.ErrorCode ?? ErrorCodes.PlayFabError,
            createResult.ErrorMessage ?? "Failed to create country leaderboard definition",
            createResult.IsRetryable);
    }

    private async Task<string> ResolveCountryCodeAsync(string playFabId, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var request = new GetPlayerProfileRequest
            {
                PlayFabId = playFabId,
                ProfileConstraints = new PlayerProfileViewConstraints
                {
                    ShowLocations = true
                }
            };

            var result = await PlayFabServerAPI.GetPlayerProfileAsync(request);
            if (result.Error != null)
            {
                _logger.LogWarning(
                    "GetPlayerProfile failed while resolving country for {PlayFabId}: {Error} - {Message}",
                    playFabId,
                    result.Error.Error,
                    result.Error.ErrorMessage);
                return GetDefaultCountryCode();
            }

            var locations = result.Result?.PlayerProfile?.Locations;
            if (locations != null)
            {
                for (var i = 0; i < locations.Count; i++)
                {
                    var location = locations[i];
                    if (location?.CountryCode == null)
                    {
                        continue;
                    }

                    var raw = location.CountryCode.Value.ToString();
                    var normalized = NormalizeCountryCode(raw);
                    if (!string.IsNullOrEmpty(normalized))
                    {
                        return normalized;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected failure while resolving country for {PlayFabId}", playFabId);
        }

        return GetDefaultCountryCode();
    }

    private async Task<PlayFabResult<TResponse>> CallProgressionApiAsync<TRequest, TResponse>(
        string route,
        TRequest request,
        CancellationToken ct)
        where TRequest : class
        where TResponse : class, new()
    {
        var entityTokenResult = await GetEntityTokenAsync(ct);
        if (!entityTokenResult.IsSuccess || string.IsNullOrEmpty(entityTokenResult.Data))
        {
            return PlayFabResult<TResponse>.Failure(
                entityTokenResult.ErrorCode ?? ErrorCodes.PlayFabError,
                entityTokenResult.ErrorMessage ?? "Failed to get entity token",
                entityTokenResult.IsRetryable);
        }

        var endpoint = $"https://{_titleId}.playfabapi.com{route}";
        var payload = JsonSerializer.Serialize(request, ProgressionJsonOptions);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Add("X-EntityToken", entityTokenResult.Data);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await ProgressionApiClient.SendAsync(httpRequest, ct);
            var rawBody = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(rawBody))
                {
                    return PlayFabResult<TResponse>.Success(new TResponse());
                }

                var envelope = TryParseProgressionEnvelope<TResponse>(rawBody);
                if (envelope != null)
                {
                    if (envelope.Data != null)
                    {
                        return PlayFabResult<TResponse>.Success(envelope.Data);
                    }

                    if (!string.IsNullOrWhiteSpace(envelope.Error))
                    {
                        var mappedEnvelopeError = MapProgressionErrorCode(envelope.Error, response.StatusCode);
                        return PlayFabResult<TResponse>.Failure(
                            mappedEnvelopeError,
                            string.IsNullOrWhiteSpace(envelope.ErrorMessage)
                                ? "Progression API returned an error envelope"
                                : envelope.ErrorMessage,
                            isRetryable: false);
                    }
                }

                var parsedDirect = JsonSerializer.Deserialize<TResponse>(rawBody, ProgressionJsonOptions);
                if (parsedDirect != null)
                {
                    return PlayFabResult<TResponse>.Success(parsedDirect);
                }

                return PlayFabResult<TResponse>.Failure(
                    ErrorCodes.SerializationError,
                    "Failed to parse progression response");
            }

            var errorPayload = TryParseProgressionError(rawBody);
            var errorCode = errorPayload?.Error;
            var errorMessage = errorPayload?.ErrorMessage;
            var mappedErrorCode = MapProgressionErrorCode(errorCode, response.StatusCode);
            var message = string.IsNullOrWhiteSpace(errorMessage)
                ? $"Progression API request failed: {(int)response.StatusCode}"
                : errorMessage;
            var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;

            _logger.LogWarning(
                "Progression API call failed ({Route}): {ErrorCode} - {ErrorMessage}",
                route,
                mappedErrorCode,
                message);

            return PlayFabResult<TResponse>.Failure(mappedErrorCode, message, retryable);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Progression API call failed unexpectedly for route {Route}", route);
            return PlayFabResult<TResponse>.Failure(ErrorCodes.PlayFabError, "Progression API call failed", isRetryable: true);
        }
    }

    private async Task<PlayFabResult<string>> GetEntityTokenAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_entityToken) && _entityTokenExpiresUtc > DateTime.UtcNow.AddMinutes(1))
        {
            return PlayFabResult<string>.Success(_entityToken);
        }

        await _entityTokenLock.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrEmpty(_entityToken) && _entityTokenExpiresUtc > DateTime.UtcNow.AddMinutes(1))
            {
                return PlayFabResult<string>.Success(_entityToken);
            }

            var result = await PlayFabAuthenticationAPI.GetEntityTokenAsync(new GetEntityTokenRequest());
            if (result.Error != null || result.Result == null || string.IsNullOrWhiteSpace(result.Result.EntityToken))
            {
                var errorCode = result.Error?.Error.ToString() ?? ErrorCodes.PlayFabError;
                var errorMessage = result.Error?.ErrorMessage ?? "Failed to acquire entity token";
                var retryable = result.Error?.HttpCode >= 500;
                return PlayFabResult<string>.Failure(errorCode, errorMessage, retryable);
            }

            _entityToken = result.Result.EntityToken;
            _entityTokenExpiresUtc = result.Result.TokenExpiration ?? DateTime.UtcNow.AddMinutes(5);
            return PlayFabResult<string>.Success(_entityToken);
        }
        finally
        {
            _entityTokenLock.Release();
        }
    }

    private static ProgressionErrorPayload? TryParseProgressionError(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return null;
        }

        try
        {
            var direct = JsonSerializer.Deserialize<ProgressionErrorPayload>(rawBody, ProgressionJsonOptions);
            if (!string.IsNullOrWhiteSpace(direct?.Error))
            {
                return direct;
            }

            var envelope = JsonSerializer.Deserialize<ProgressionEnvelope<JsonElement>>(rawBody, ProgressionJsonOptions);
            if (envelope == null)
            {
                return null;
            }

            return new ProgressionErrorPayload
            {
                Error = envelope.Error ?? string.Empty,
                ErrorMessage = envelope.ErrorMessage ?? string.Empty
            };
        }
        catch
        {
            return null;
        }
    }

    private static ProgressionEnvelope<T>? TryParseProgressionEnvelope<T>(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProgressionEnvelope<T>>(rawBody, ProgressionJsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string MapProgressionErrorCode(string? rawCode, HttpStatusCode statusCode)
    {
        if (!string.IsNullOrWhiteSpace(rawCode))
        {
            return rawCode;
        }

        return statusCode switch
        {
            HttpStatusCode.TooManyRequests => ErrorCodes.RateLimited,
            HttpStatusCode.Unauthorized => ErrorCodes.Unauthorized,
            HttpStatusCode.Forbidden => ErrorCodes.Forbidden,
            HttpStatusCode.NotFound => ErrorCodes.NotFound,
            _ => ErrorCodes.PlayFabError
        };
    }

    private static bool IsDefinitionMissingError<T>(PlayFabResult<T> result)
    {
        if (result.IsSuccess)
        {
            return false;
        }

        return string.Equals(result.ErrorCode, "StatisticNotFound", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(result.ErrorCode, "LeaderboardNotFound", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(result.ErrorCode, ErrorCodes.NotFound, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> TryResolveDisplayNameFromAccountInfoAsync(string playFabId, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var result = await PlayFabServerAPI.GetUserAccountInfoAsync(new GetUserAccountInfoRequest
            {
                PlayFabId = playFabId
            });

            if (result.Error != null)
            {
                return string.Empty;
            }

            return SanitizeDisplayNameForLeaderboard(result.Result?.UserInfo?.TitleInfo?.DisplayName);
        }
        catch
        {
            return string.Empty;
        }
    }

    private List<LeaderboardRowDto> BuildLeaderboardRows(
        List<EntityLeaderboardEntryPayload> rankings,
        string currentPlayerId,
        int fallbackRankStart)
    {
        var rows = new List<LeaderboardRowDto>(rankings.Count);
        for (var i = 0; i < rankings.Count; i++)
        {
            var ranking = rankings[i];
            var playerId = ResolveRankingEntityId(ranking);
            var rank = ranking.Rank > 0 ? ranking.Rank : (fallbackRankStart + i);
            rows.Add(new LeaderboardRowDto
            {
                PlayerId = playerId,
                DisplayName = ResolveRankingDisplayName(ranking),
                Rank = rank,
                Stars = ParseStars(ranking.Scores),
                Level = ParseLevelFromMetadata(ranking.Metadata),
                IsMe = string.Equals(playerId, currentPlayerId, StringComparison.Ordinal)
            });
        }

        return rows;
    }

    private static LeaderboardRowDto? BuildCurrentPlayerRow(
        List<EntityLeaderboardEntryPayload> rankings,
        string currentPlayerId)
    {
        for (var i = 0; i < rankings.Count; i++)
        {
            var ranking = rankings[i];
            var playerId = ResolveRankingEntityId(ranking);
            if (!string.Equals(playerId, currentPlayerId, StringComparison.Ordinal))
            {
                continue;
            }

            return new LeaderboardRowDto
            {
                PlayerId = playerId,
                DisplayName = ResolveRankingDisplayName(ranking),
                Rank = Math.Max(0, ranking.Rank),
                Stars = ParseStars(ranking.Scores),
                Level = ParseLevelFromMetadata(ranking.Metadata),
                IsMe = true
            };
        }

        return null;
    }

    private static string ResolveRankingEntityId(EntityLeaderboardEntryPayload? ranking)
    {
        if (ranking == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(ranking.Entity?.Id))
        {
            return ranking.Entity.Id.Trim();
        }

        return ranking.EntityId?.Trim() ?? string.Empty;
    }

    private static string ResolveRankingDisplayName(EntityLeaderboardEntryPayload? ranking)
    {
        if (!string.IsNullOrWhiteSpace(ranking?.DisplayName))
        {
            return SanitizeDisplayNameForLeaderboard(ranking.DisplayName);
        }

        return ParseDisplayNameFromMetadata(ranking?.Metadata);
    }

    private static string BuildLeaderboardMetadata(int level, string? displayName)
    {
        var normalizedLevel = Math.Max(1, level);
        var sanitized = SanitizeDisplayNameForLeaderboard(displayName);
        var normalizedDisplayName = string.IsNullOrEmpty(sanitized) ? null : sanitized;

        while (true)
        {
            var payload = new LeaderboardMetadataPayload
            {
                L = normalizedLevel,
                D = normalizedDisplayName
            };

            var metadata = JsonSerializer.Serialize(payload, LeaderboardMetadataJsonOptions);
            if (metadata.Length <= MaxLeaderboardMetadataLength ||
                string.IsNullOrEmpty(normalizedDisplayName))
            {
                return metadata;
            }

            normalizedDisplayName = normalizedDisplayName[..^1].TrimEnd();
            if (normalizedDisplayName.Length == 0)
            {
                normalizedDisplayName = null;
            }
        }
    }

    /// <summary>
    /// Transliterates Turkish / accented characters to ASCII ("Barış Avşar" ->
    /// "BarisAvsar") then strips anything outside [A-Za-z0-9] so the leaderboard
    /// UI renders a single clean word with the bundled ASCII-only font atlas.
    /// Applied at both the write path (metadata stamping) and the read path
    /// (fallback for legacy / account-info sourced names).
    /// </summary>
    private static string SanitizeDisplayNameForLeaderboard(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return string.Empty;
        }

        var transliterated = TransliterateToAscii(displayName.Trim());
        var builder = new StringBuilder(transliterated.Length);
        for (var i = 0; i < transliterated.Length; i++)
        {
            var c = transliterated[i];
            if ((c >= 'a' && c <= 'z') ||
                (c >= 'A' && c <= 'Z') ||
                (c >= '0' && c <= '9'))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    // Pre-replacement table for characters that Unicode NFD does not decompose
    // into an ASCII base + combining mark. Everything else (ş, ğ, ü, ö, ç, é, ñ,
    // â, …) is handled by the NFD+strip pass in TransliterateToAscii.
    private static readonly Dictionary<char, string> DisplayNameTransliterationMap = new()
    {
        { 'ı', "i" },
        { 'İ', "I" },
        { 'ß', "ss" },
        { 'æ', "ae" }, { 'Æ', "AE" },
        { 'œ', "oe" }, { 'Œ', "OE" },
        { 'ø', "o" }, { 'Ø', "O" },
        { 'đ', "d" }, { 'Đ', "D" },
        { 'ð', "d" }, { 'Ð', "D" },
        { 'ł', "l" }, { 'Ł', "L" },
        { 'þ', "th" }, { 'Þ', "Th" }
    };

    private static string TransliterateToAscii(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var preReplaced = new StringBuilder(input.Length);
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (DisplayNameTransliterationMap.TryGetValue(c, out var replacement))
            {
                preReplaced.Append(replacement);
            }
            else
            {
                preReplaced.Append(c);
            }
        }

        var decomposed = preReplaced.ToString().Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        for (var i = 0; i < decomposed.Length; i++)
        {
            var c = decomposed[i];
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }


    private static int ParseStars(List<string> scores)
    {
        if (scores == null || scores.Count == 0)
        {
            return 0;
        }

        return long.TryParse(scores[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed <= 0
                ? 0
                : parsed >= int.MaxValue
                    ? int.MaxValue
                    : (int)parsed
            : 0;
    }

    private static int ParseLevelFromMetadata(string metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("level", out var levelNode) && levelNode.TryGetInt32(out var level))
                {
                    return Math.Max(0, level);
                }

                if (root.TryGetProperty("Level", out var levelNodePascal) && levelNodePascal.TryGetInt32(out var levelPascal))
                {
                    return Math.Max(0, levelPascal);
                }

                if (root.TryGetProperty("L", out var compactLevelNode) && compactLevelNode.TryGetInt32(out var compactLevel))
                {
                    return Math.Max(0, compactLevel);
                }
            }
        }
        catch
        {
            // Ignore malformed metadata and fallback to 0.
        }

        return 0;
    }

    private static string ParseDisplayNameFromMetadata(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            if (root.TryGetProperty("displayName", out var displayNameNode) &&
                displayNameNode.ValueKind == JsonValueKind.String)
            {
                return SanitizeDisplayNameForLeaderboard(displayNameNode.GetString());
            }

            if (root.TryGetProperty("DisplayName", out var displayNameNodePascal) &&
                displayNameNodePascal.ValueKind == JsonValueKind.String)
            {
                return SanitizeDisplayNameForLeaderboard(displayNameNodePascal.GetString());
            }

            if (root.TryGetProperty("D", out var compactDisplayNameNode) &&
                compactDisplayNameNode.ValueKind == JsonValueKind.String)
            {
                return SanitizeDisplayNameForLeaderboard(compactDisplayNameNode.GetString());
            }
        }
        catch
        {
            // Ignore malformed metadata and fallback to empty display name.
        }

        return string.Empty;
    }

    private static string NormalizeCountryCode(string? rawCountryCode)
    {
        if (string.IsNullOrWhiteSpace(rawCountryCode))
        {
            return string.Empty;
        }

        var cleaned = new string(rawCountryCode.Trim().ToUpperInvariant().Where(char.IsLetter).ToArray());
        if (cleaned.Length >= 2)
        {
            return cleaned[..2];
        }

        return string.Empty;
    }

    private string BuildCountryLeaderboardName(string countryCode)
    {
        return $"{GetCountryLeaderboardPrefix()}{countryCode}";
    }

    private static int GetLeaderboardSizeLimit()
    {
        var configured = Environment.GetEnvironmentVariable("LEADERBOARD_SIZE_LIMIT");
        return int.TryParse(configured, out var parsed) && parsed > 0 ? parsed : DefaultLeaderboardSizeLimit;
    }

    private static string GetWorldStatisticName()
    {
        var configured = Environment.GetEnvironmentVariable("LEADERBOARD_WORLD_STAT_NAME");
        return string.IsNullOrWhiteSpace(configured) ? DefaultWorldStatisticName : configured.Trim();
    }

    private static string GetWorldLeaderboardName()
    {
        var configured = Environment.GetEnvironmentVariable("LEADERBOARD_WORLD_NAME");
        return string.IsNullOrWhiteSpace(configured) ? DefaultWorldLeaderboardName : configured.Trim();
    }

    private static string GetCountryLeaderboardPrefix()
    {
        var configured = Environment.GetEnvironmentVariable("LEADERBOARD_COUNTRY_PREFIX");
        return string.IsNullOrWhiteSpace(configured) ? DefaultCountryLeaderboardPrefix : configured.Trim();
    }

    private static string GetDefaultCountryCode()
    {
        var configured = Environment.GetEnvironmentVariable("LEADERBOARD_DEFAULT_COUNTRY");
        var normalized = NormalizeCountryCode(configured);
        return string.IsNullOrWhiteSpace(normalized) ? DefaultCountryCode : normalized;
    }

    private sealed class LeaderboardMetadataPayload
    {
        public int L { get; set; }
        public string? D { get; set; }
    }

    private sealed class ProgressionEntityKeyPayload
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }

    private sealed class StatisticUpdatePayload
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Scores { get; set; } = new();
        public string Metadata { get; set; } = string.Empty;
    }

    private sealed class UpdateStatisticsRequestPayload
    {
        public ProgressionEntityKeyPayload Entity { get; set; } = new();
        public List<StatisticUpdatePayload> Statistics { get; set; } = new();
    }

    private sealed class LeaderboardEntryUpdatePayload
    {
        public string EntityId { get; set; } = string.Empty;
        public List<string> Scores { get; set; } = new();
        public string Metadata { get; set; } = string.Empty;
    }

    private sealed class UpdateLeaderboardEntriesRequestPayload
    {
        public string LeaderboardName { get; set; } = string.Empty;
        public List<LeaderboardEntryUpdatePayload> Entries { get; set; } = new();
    }

    private sealed class NameRequestPayload
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class VersionConfigurationPayload
    {
        public int MaxQueryableVersions { get; set; }
        public string ResetInterval { get; set; } = "Manual";
    }

    private sealed class StatisticColumnPayload
    {
        public string Name { get; set; } = string.Empty;
        public string AggregationMethod { get; set; } = "Last";
    }

    private sealed class CreateStatisticDefinitionRequestPayload
    {
        public string Name { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public VersionConfigurationPayload VersionConfiguration { get; set; } = new();
        public List<StatisticColumnPayload> Columns { get; set; } = new();
    }

    private sealed class LinkedStatisticColumnPayload
    {
        public string LinkedStatisticName { get; set; } = string.Empty;
        public string LinkedStatisticColumnName { get; set; } = string.Empty;
    }

    private sealed class LeaderboardColumnPayload
    {
        public string Name { get; set; } = string.Empty;
        public string SortDirection { get; set; } = "Descending";
        public LinkedStatisticColumnPayload LinkedStatisticColumn { get; set; } = new();
    }

    private sealed class CreateLeaderboardDefinitionRequestPayload
    {
        public string Name { get; set; } = string.Empty;
        public int SizeLimit { get; set; }
        public string EntityType { get; set; } = string.Empty;
        public VersionConfigurationPayload VersionConfiguration { get; set; } = new();
        public List<LeaderboardColumnPayload> Columns { get; set; } = new();
    }

    private sealed class GetEntityLeaderboardRequestPayload
    {
        public string LeaderboardName { get; set; } = string.Empty;
        public int PageSize { get; set; }
        public int StartingPosition { get; set; }
    }

    private sealed class GetLeaderboardForEntitiesRequestPayload
    {
        public string LeaderboardName { get; set; } = string.Empty;
        public List<string> EntityIds { get; set; } = new();
    }

    private sealed class GetEntityLeaderboardResponsePayload
    {
        public int EntryCount { get; set; }
        public List<EntityLeaderboardEntryPayload> Rankings { get; set; } = new();
    }

    private sealed class EntityLeaderboardEntryPayload
    {
        public string DisplayName { get; set; } = string.Empty;
        public ProgressionEntityKeyPayload Entity { get; set; } = new();
        public string EntityId { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public int Rank { get; set; }
        public List<string> Scores { get; set; } = new();
        public string Metadata { get; set; } = string.Empty;
    }

    private sealed class ProgressionErrorPayload
    {
        public string Error { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    private sealed class ProgressionEnvelope<T>
    {
        public int Code { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    private sealed class EmptyProgressionResponsePayload { }
    private sealed class StatisticDefinitionResponsePayload { }
    private sealed class LeaderboardDefinitionResponsePayload { }
}
