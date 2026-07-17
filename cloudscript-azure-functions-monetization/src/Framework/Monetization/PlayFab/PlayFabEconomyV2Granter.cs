using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
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
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ILogger<PlayFabEconomyV2Granter> _logger;
    private readonly string _titleId;
    private readonly string _secretKey;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public PlayFabEconomyV2Granter(
        string titleId,
        string secretKey,
        ILogger<PlayFabEconomyV2Granter> logger,
        HttpClient? httpClient = null)
    {
        _titleId = titleId;
        _secretKey = secretKey;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient == null;
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

            var url = $"https://{_titleId}.playfabapi.com/Inventory/ExecuteInventoryOperations";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Headers.Add("X-SecretKey", _secretKey);
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(httpRequest, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PlayFab Economy grant failed: {Status}",
                    response.StatusCode);

                // Check for idempotency duplicate
                if (responseBody.Contains("IdempotencyConflict") ||
                    responseBody.Contains("already been processed"))
                {
                    _logger.LogInformation("Grant was duplicate (idempotent): {IdempotencyKey}",
                        request.IdempotencyKey);
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

            var url = $"https://{_titleId}.playfabapi.com/Inventory/ExecuteInventoryOperations";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Headers.Add("X-SecretKey", _secretKey);
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(httpRequest, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking items from player {PlayerToken}", SensitiveLogValue.Fingerprint(request.PlayerId));
            return GrantResult.Failure("INTERNAL_ERROR", "PlayFab operation failed");
        }
    }

    public async Task<List<string>> GetPlayerItemsAsync(string playerId, CancellationToken ct = default)
    {
        try
        {
            var body = new
            {
                Entity = new
                {
                    Id = playerId,
                    Type = "title_player_account"
                },
                Count = 50 // Max items to return per page
            };

            var url = $"https://{_titleId}.playfabapi.com/Inventory/GetInventoryItems";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Headers.Add("X-SecretKey", _secretKey);
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(httpRequest, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PlayFab get inventory failed: {Status}",
                    response.StatusCode);
                return new List<string>();
            }

            var result = JsonDocument.Parse(responseBody);
            var items = new List<string>();

            if (result.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("Items", out var itemsArray))
            {
                foreach (var item in itemsArray.EnumerateArray())
                {
                    if (item.TryGetProperty("Id", out var id))
                    {
                        items.Add(id.GetString() ?? string.Empty);
                    }
                }
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting inventory for player {PlayerToken}", SensitiveLogValue.Fingerprint(playerId));
            return new List<string>();
        }
    }

    private static string ExtractErrorCode(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("errorCode", out var errorCode))
            {
                return errorCode.GetString() ?? "UNKNOWN";
            }
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                return error.GetString() ?? "UNKNOWN";
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
            if (doc.RootElement.TryGetProperty("errorMessage", out var message))
            {
                return message.GetString() ?? "Unknown error";
            }
            if (doc.RootElement.TryGetProperty("status", out var status))
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

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
