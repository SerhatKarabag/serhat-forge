using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;
using Serhat.Forge.CloudScript.Infrastructure.Security;

namespace Serhat.Forge.CloudScript.Framework.Monetization.PlayFab;

/// <summary>
/// PlayFab Economy v2 implementation of entitlement granter.
/// Uses the Entity API for inventory management.
/// </summary>
public sealed class PlayFabEconomyV2Granter : IEntitlementGranter, IDisposable
{
    private const int InventoryPageSize = 50;
    private const int MaxInventoryPages = 100;
    private const int MaxInventoryItems = InventoryPageSize * MaxInventoryPages;
    private static readonly TimeSpan EntityTokenRefreshBuffer = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan EntityTokenFallbackLifetime = TimeSpan.FromMinutes(10);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ILogger<PlayFabEconomyV2Granter> _logger;
    private readonly string _titleId;
    private readonly string _secretKey;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _entityTokenLock = new(1, 1);

    private string? _entityToken;
    private DateTimeOffset _entityTokenExpiresAtUtc;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public PlayFabEconomyV2Granter(
        string titleId,
        string secretKey,
        ILogger<PlayFabEconomyV2Granter> logger,
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null)
    {
        _titleId = titleId;
        _secretKey = secretKey;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient == null;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<GrantResult> GrantItemsAsync(GrantRequest request, CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug("Granting items to player {PlayerToken}: {Items}",
                SensitiveLogValue.Fingerprint(request.PlayerId), string.Join(", ", request.ItemIds));

            // Build the request body for AddInventoryItems
            var operations = new List<object>();

            for (var i = 0; i < request.ItemIds.Count; i++)
            {
                var itemId = request.ItemIds[i];
                var quantity = request.Quantities?.Count > i ? request.Quantities[i] : 1;

                operations.Add(new
                {
                    Item = new
                    {
                        Id = itemId,
                        StackId = "default"
                    },
                    Amount = quantity,
                    NewStackValues = new
                    {
                        DisplayProperties = request.Metadata
                    }
                });
            }

            var body = new
            {
                Entity = new
                {
                    Id = request.PlayerId,
                    Type = "title_player_account"
                },
                IdempotencyId = request.IdempotencyKey,
                Operations = operations.Select(op => new
                {
                    Add = op
                }).ToList()
            };

            var response = await SendInventoryRequestAsync(
                "Inventory/ExecuteInventoryOperations",
                body,
                ct).ConfigureAwait(false);
            var responseBody = response.Content;

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PlayFab Economy grant failed: {Status}",
                    response.StatusCode);

                // Check for idempotency duplicate
                if (responseBody.Contains("IdempotencyConflict") ||
                    responseBody.Contains("already been processed"))
                {
                    _logger.LogInformation("Grant was duplicate (idempotent): {IdempotencyFingerprint}",
                        SensitiveLogValue.Fingerprint(request.IdempotencyKey));
                    return GrantResult.Success(request.ItemIds, wasDuplicate: true);
                }

                // Parse PlayFab error
                var errorCode = ExtractErrorCode(responseBody);
                var errorMessage = ExtractErrorMessage(responseBody);

                return GrantResult.Failure(errorCode, errorMessage);
            }

            _logger.LogInformation("Successfully granted {Count} items to player {PlayerToken}",
                request.ItemIds.Count, SensitiveLogValue.Fingerprint(request.PlayerId));

            return GrantResult.Success(request.ItemIds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error granting items to player {PlayerToken}", SensitiveLogValue.Fingerprint(request.PlayerId));
            return GrantResult.Failure("NETWORK_ERROR", "PlayFab is temporarily unavailable");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error granting items to player {PlayerToken}", SensitiveLogValue.Fingerprint(request.PlayerId));
            return GrantResult.Failure("INTERNAL_ERROR", "PlayFab operation failed");
        }
    }

    public async Task<GrantResult> RevokeItemsAsync(RevokeRequest request, CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug("Revoking items from player {PlayerToken}: {Items}",
                SensitiveLogValue.Fingerprint(request.PlayerId), string.Join(", ", request.ItemIds));

            // For Economy v2, we need to either:
            // 1. Delete inventory items (permanent)
            // 2. Subtract quantity (for stackables)
            // We'll use SubtractInventoryItems

            var operations = request.ItemIds.Select(itemId => new
            {
                Subtract = new
                {
                    Item = new
                    {
                        Id = itemId,
                        StackId = "default"
                    },
                    Amount = 1
                }
            }).ToList();

            var body = new
            {
                Entity = new
                {
                    Id = request.PlayerId,
                    Type = "title_player_account"
                },
                IdempotencyId = request.IdempotencyKey,
                Operations = operations
            };

            var response = await SendInventoryRequestAsync(
                "Inventory/ExecuteInventoryOperations",
                body,
                ct).ConfigureAwait(false);
            var responseBody = response.Content;

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PlayFab Economy revoke failed: {Status}",
                    response.StatusCode);

                // Check for idempotency duplicate
                if (responseBody.Contains("IdempotencyConflict"))
                {
                    return GrantResult.Success(request.ItemIds, wasDuplicate: true);
                }

                var errorCode = ExtractErrorCode(responseBody);
                var errorMessage = ExtractErrorMessage(responseBody);

                return GrantResult.Failure(errorCode, errorMessage);
            }

            _logger.LogInformation("Successfully revoked {Count} items from player {PlayerToken}",
                request.ItemIds.Count, SensitiveLogValue.Fingerprint(request.PlayerId));

            return GrantResult.Success(request.ItemIds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking items from player {PlayerToken}", SensitiveLogValue.Fingerprint(request.PlayerId));
            return GrantResult.Failure("INTERNAL_ERROR", "PlayFab operation failed");
        }
    }

    public async Task<InventoryQueryResult> GetPlayerItemsAsync(
        string playerId,
        CancellationToken ct = default)
    {
        try
        {
            var items = new List<InventoryItem>();
            string? continuationToken = null;

            for (var page = 0; page < MaxInventoryPages; page++)
            {
                var body = new
                {
                    Entity = new
                    {
                        Id = playerId,
                        Type = "title_player_account"
                    },
                    Count = InventoryPageSize,
                    ContinuationToken = continuationToken
                };

                var response = await SendInventoryRequestAsync(
                    "Inventory/GetInventoryItems",
                    body,
                    ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var errorCode = ExtractErrorCode(response.Content);
                    _logger.LogWarning(
                        "PlayFab get inventory failed: {Status}, Error={ErrorCode}",
                        response.StatusCode,
                        errorCode);
                    return InventoryQueryResult.Failure(
                        errorCode,
                        "PlayFab inventory is temporarily unavailable",
                        IsRetryableStatus(response.StatusCode));
                }

                var pageResult = ParseInventoryPage(response.Content);
                if (!pageResult.IsSuccess)
                {
                    _logger.LogError(
                        "PlayFab inventory returned a malformed response for player {PlayerToken}",
                        SensitiveLogValue.Fingerprint(playerId));
                    return InventoryQueryResult.Failure(
                        "INVALID_PROVIDER_RESPONSE",
                        "PlayFab inventory returned an invalid response",
                        true);
                }

                items.AddRange(pageResult.Items);
                if (items.Count > MaxInventoryItems)
                {
                    return InventoryQueryResult.Failure(
                        "INVENTORY_LIMIT_EXCEEDED",
                        "Player inventory exceeds the configured safety limit",
                        false);
                }

                continuationToken = pageResult.ContinuationToken;
                if (string.IsNullOrWhiteSpace(continuationToken))
                {
                    return InventoryQueryResult.Success(items);
                }
            }

            _logger.LogWarning(
                "PlayFab inventory pagination exceeded {MaxPages} pages for player {PlayerToken}",
                MaxInventoryPages,
                SensitiveLogValue.Fingerprint(playerId));
            return InventoryQueryResult.Failure(
                "INVENTORY_LIMIT_EXCEEDED",
                "Player inventory exceeds the configured safety limit",
                false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "Network error getting inventory for player {PlayerToken}",
                SensitiveLogValue.Fingerprint(playerId));
            return InventoryQueryResult.Failure(
                "NETWORK_ERROR",
                "PlayFab inventory is temporarily unavailable",
                true);
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Invalid inventory response for player {PlayerToken}",
                SensitiveLogValue.Fingerprint(playerId));
            return InventoryQueryResult.Failure(
                "INVALID_PROVIDER_RESPONSE",
                "PlayFab inventory returned an invalid response",
                true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting inventory for player {PlayerToken}", SensitiveLogValue.Fingerprint(playerId));
            return InventoryQueryResult.Failure(
                "INTERNAL_ERROR",
                "PlayFab inventory operation failed",
                true);
        }
    }

    private async Task<PlayFabApiResponse> SendInventoryRequestAsync(
        string relativePath,
        object body,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var tokenResult = await GetEntityTokenAsync(ct).ConfigureAwait(false);
            if (!tokenResult.IsSuccess)
            {
                return new PlayFabApiResponse(tokenResult.StatusCode, tokenResult.ErrorPayload);
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://{_titleId}.playfabapi.com/{relativePath}");
            request.Headers.Add("X-EntityToken", tokenResult.Token);
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (attempt == 0 &&
                (response.StatusCode == HttpStatusCode.Unauthorized ||
                 response.StatusCode == HttpStatusCode.Forbidden))
            {
                InvalidateEntityToken(tokenResult.Token!);
                continue;
            }

            return new PlayFabApiResponse(response.StatusCode, content);
        }

        return new PlayFabApiResponse(
            HttpStatusCode.Unauthorized,
            CreateLocalErrorPayload(
                "ENTITY_TOKEN_REJECTED",
                "PlayFab rejected the refreshed entity token"));
    }

    private async Task<EntityTokenResult> GetEntityTokenAsync(CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();
        if (IsCachedEntityTokenValid(now))
        {
            return EntityTokenResult.Success(_entityToken!);
        }

        await _entityTokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            now = _timeProvider.GetUtcNow();
            if (IsCachedEntityTokenValid(now))
            {
                return EntityTokenResult.Success(_entityToken!);
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://{_titleId}.playfabapi.com/Authentication/GetEntityToken");
            request.Headers.Add("X-SecretKey", _secretKey);
            request.Content = new StringContent(
                "{\"CustomTags\":{\"source\":\"serhat-forge-monetization\"}}",
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "PlayFab entity-token exchange failed: {Status}, Error={ErrorCode}",
                    response.StatusCode,
                    ExtractErrorCode(content));
                return EntityTokenResult.Failure(response.StatusCode, content);
            }

            if (!TryParseEntityToken(content, now, out var token, out var expiresAtUtc))
            {
                _logger.LogError("PlayFab entity-token exchange returned a malformed response");
                return EntityTokenResult.Failure(
                    HttpStatusCode.BadGateway,
                    CreateLocalErrorPayload(
                        "INVALID_ENTITY_TOKEN_RESPONSE",
                        "PlayFab returned an invalid entity-token response"));
            }

            _entityToken = token;
            _entityTokenExpiresAtUtc = expiresAtUtc;
            return EntityTokenResult.Success(token);
        }
        finally
        {
            _entityTokenLock.Release();
        }
    }

    private bool IsCachedEntityTokenValid(DateTimeOffset now) =>
        !string.IsNullOrWhiteSpace(_entityToken) &&
        _entityTokenExpiresAtUtc > now.Add(EntityTokenRefreshBuffer);

    private void InvalidateEntityToken(string rejectedToken)
    {
        if (string.Equals(_entityToken, rejectedToken, StringComparison.Ordinal))
        {
            _entityToken = null;
            _entityTokenExpiresAtUtc = default;
        }
    }

    private static bool TryParseEntityToken(
        string responseBody,
        DateTimeOffset now,
        out string token,
        out DateTimeOffset expiresAtUtc)
    {
        token = string.Empty;
        expiresAtUtc = default;

        using var document = JsonDocument.Parse(responseBody);
        if (!TryGetPropertyIgnoreCase(document.RootElement, "data", out var data) ||
            data.ValueKind != JsonValueKind.Object ||
            !TryGetPropertyIgnoreCase(data, "EntityToken", out var tokenElement) ||
            tokenElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        token = tokenElement.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        expiresAtUtc = now.Add(EntityTokenFallbackLifetime);
        if (TryGetPropertyIgnoreCase(data, "TokenExpiration", out var expirationElement) &&
            expirationElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            if (expirationElement.ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParse(
                    expirationElement.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out expiresAtUtc) ||
                expiresAtUtc <= now)
            {
                token = string.Empty;
                expiresAtUtc = default;
                return false;
            }
        }

        return true;
    }

    private InventoryPageResult ParseInventoryPage(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        if (!TryGetPropertyIgnoreCase(document.RootElement, "data", out var data) ||
            data.ValueKind != JsonValueKind.Object ||
            !TryGetPropertyIgnoreCase(data, "Items", out var itemElements) ||
            itemElements.ValueKind != JsonValueKind.Array)
        {
            return InventoryPageResult.Failure();
        }

        var now = _timeProvider.GetUtcNow();
        var items = new List<InventoryItem>();
        foreach (var itemElement in itemElements.EnumerateArray())
        {
            if (itemElement.ValueKind != JsonValueKind.Object ||
                !TryGetPropertyIgnoreCase(itemElement, "Id", out var idElement) ||
                idElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(idElement.GetString()) ||
                !TryGetPropertyIgnoreCase(itemElement, "Amount", out var amountElement) ||
                !amountElement.TryGetInt64(out var amount) ||
                amount < 0)
            {
                return InventoryPageResult.Failure();
            }

            var stackId = "default";
            if (TryGetPropertyIgnoreCase(itemElement, "StackId", out var stackElement) &&
                stackElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                if (stackElement.ValueKind != JsonValueKind.String)
                {
                    return InventoryPageResult.Failure();
                }

                stackId = stackElement.GetString() ?? "default";
                if (string.IsNullOrWhiteSpace(stackId))
                {
                    stackId = "default";
                }
            }

            DateTimeOffset? expiration = null;
            if (TryGetPropertyIgnoreCase(itemElement, "ExpirationDate", out var expirationElement) &&
                expirationElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                if (expirationElement.ValueKind != JsonValueKind.String ||
                    !DateTimeOffset.TryParse(
                        expirationElement.GetString(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var parsedExpiration))
                {
                    return InventoryPageResult.Failure();
                }

                expiration = parsedExpiration;
            }

            // Empty and expired stacks are not active entitlements.
            if (amount == 0 || (expiration.HasValue && expiration.Value <= now))
            {
                continue;
            }

            items.Add(new InventoryItem
            {
                ItemId = idElement.GetString()!,
                StackId = stackId,
                Amount = amount,
                ExpiresAtUtc = expiration?.UtcDateTime
            });
        }

        string? continuationToken = null;
        if (TryGetPropertyIgnoreCase(data, "ContinuationToken", out var continuationElement) &&
            continuationElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            if (continuationElement.ValueKind != JsonValueKind.String)
            {
                return InventoryPageResult.Failure();
            }

            continuationToken = continuationElement.GetString();
        }

        return InventoryPageResult.Success(items, continuationToken);
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
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

    private static bool IsRetryableStatus(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout ||
        statusCode == HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private static string CreateLocalErrorPayload(string errorCode, string errorMessage) =>
        JsonSerializer.Serialize(new { error = errorCode, errorMessage }, JsonOptions);

    private static string ExtractErrorCode(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (TryGetPropertyIgnoreCase(doc.RootElement, "error", out var error) &&
                error.ValueKind == JsonValueKind.String)
            {
                return error.GetString() ?? "UNKNOWN";
            }
            if (TryGetPropertyIgnoreCase(doc.RootElement, "errorCode", out var errorCode))
            {
                return errorCode.ValueKind == JsonValueKind.String
                    ? errorCode.GetString() ?? "UNKNOWN"
                    : errorCode.ToString();
            }
        }
        catch
        {
            // Ignore parse errors
        }

        return "PLAYFAB_ERROR";
    }

    private static string ExtractErrorMessage(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (TryGetPropertyIgnoreCase(doc.RootElement, "errorMessage", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? "Unknown error";
            }
            if (TryGetPropertyIgnoreCase(doc.RootElement, "status", out var status) &&
                status.ValueKind == JsonValueKind.String)
            {
                return status.GetString() ?? "Unknown error";
            }
        }
        catch
        {
            // Ignore parse errors
        }

        return "PlayFab operation failed";
    }

    private sealed class PlayFabApiResponse
    {
        public PlayFabApiResponse(HttpStatusCode statusCode, string content)
        {
            StatusCode = statusCode;
            Content = content;
        }

        public HttpStatusCode StatusCode { get; }
        public string Content { get; }
        public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;
    }

    private sealed class EntityTokenResult
    {
        private EntityTokenResult(
            bool isSuccess,
            string? token,
            HttpStatusCode statusCode,
            string errorPayload)
        {
            IsSuccess = isSuccess;
            Token = token;
            StatusCode = statusCode;
            ErrorPayload = errorPayload;
        }

        public bool IsSuccess { get; }
        public string? Token { get; }
        public HttpStatusCode StatusCode { get; }
        public string ErrorPayload { get; }

        public static EntityTokenResult Success(string token) =>
            new(true, token, HttpStatusCode.OK, string.Empty);

        public static EntityTokenResult Failure(HttpStatusCode statusCode, string errorPayload) =>
            new(false, null, statusCode, errorPayload);
    }

    private sealed class InventoryPageResult
    {
        private InventoryPageResult(
            bool isSuccess,
            IReadOnlyList<InventoryItem>? items,
            string? continuationToken)
        {
            IsSuccess = isSuccess;
            Items = items ?? Array.Empty<InventoryItem>();
            ContinuationToken = continuationToken;
        }

        public bool IsSuccess { get; }
        public IReadOnlyList<InventoryItem> Items { get; }
        public string? ContinuationToken { get; }

        public static InventoryPageResult Success(
            IReadOnlyList<InventoryItem> items,
            string? continuationToken) =>
            new(true, items, continuationToken);

        public static InventoryPageResult Failure() =>
            new(false, null, null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _entityTokenLock.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
