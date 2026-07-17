using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Serhat.Forge.CloudScript.Domain;
using Serhat.Forge.CloudScript.Domain.DTOs;
using Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Configuration;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;
using Serhat.Forge.CloudScript.Infrastructure.Logging;
using Serhat.Forge.CloudScript.Infrastructure.Security;

namespace Serhat.Forge.CloudScript.Functions.Monetization;

/// <summary>
/// Request DTO for getting entitlements.
/// </summary>
public sealed class GetEntitlementsRequestDto
{
    public bool ForceRefresh { get; set; }
}

/// <summary>
/// Response DTO for entitlements.
/// </summary>
public sealed class GetEntitlementsResponseDto
{
    public List<EntitlementItemDto> Entitlements { get; set; } = new();
    public ActiveSubscriptionDto? ActiveSubscription { get; set; }
    public DateTime ServerTimestampUtc { get; set; }
}

public sealed class EntitlementItemDto
{
    public string ItemId { get; set; } = string.Empty;
    public string StackId { get; set; } = "default";
    public long Quantity { get; set; } = 1;
    public DateTime? ExpiresAtUtc { get; set; }
}

public sealed class ActiveSubscriptionDto
{
    public string ProductId { get; set; } = string.Empty;
    public string TierKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool AutoRenew { get; set; }
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public DateTime OriginalPurchaseDateUtc { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string? GrantedItemId { get; set; }
    public int? GracePeriodDaysRemaining { get; set; }
}

/// <summary>
/// Azure Function for getting player entitlements.
/// </summary>
public sealed class GetEntitlementsFunction
{
    private const int MaxRequestBodyBytes = 128 * 1024;

    private readonly IPurchaseRepository _repository;
    private readonly MonetizationConfig _config;
    private readonly IEntitlementGranter _granter;
    private readonly ICorrelationContext _correlationContext;
    private readonly ILogger<GetEntitlementsFunction> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public GetEntitlementsFunction(
        IPurchaseRepository repository,
        IEntitlementGranter granter,
        MonetizationConfig config,
        ICorrelationContext correlationContext,
        ILogger<GetEntitlementsFunction> logger)
    {
        _repository = repository;
        _granter = granter;
        _config = config;
        _correlationContext = correlationContext;
        _logger = logger;
    }

    [Function("GetEntitlements")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "monetization/entitlements")] HttpRequestData req,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var correlationId = string.Empty;

        try
        {
            var body = await HttpRequestSecurity.ReadUtf8BodyAsync(
                req.Body,
                MaxRequestBodyBytes,
                ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                return await CreateErrorResponse(req, "INVALID_REQUEST", "Request body is required",
                    HttpStatusCode.BadRequest, correlationId, stopwatch.ElapsedMilliseconds);
            }

            var parseResult = PlayFabRequestSecurity.ParseEnvelope<GetEntitlementsRequestDto>(
                body,
                JsonOptions,
                _config.PlayFabTitleId,
                _config.EnvironmentName,
                "GetEntitlements");
            if (!parseResult.IsSuccess)
            {
                return await CreateErrorResponse(
                    req,
                    parseResult.ErrorCode ?? "INVALID_REQUEST",
                    parseResult.IsUnauthorized ? "Authenticated PlayFab context is required" : "Invalid request format",
                    parseResult.IsUnauthorized ? HttpStatusCode.Unauthorized : HttpStatusCode.BadRequest,
                    correlationId,
                    stopwatch.ElapsedMilliseconds);
            }

            var envelope = parseResult.Envelope!;
            correlationId = envelope.CorrelationId;
            _correlationContext.SetCorrelationId(correlationId);

            var playerId = envelope.Caller.PlayerId;

            _logger.LogInformation(
                "[{CorrelationId}] GetEntitlements: PlayerToken={PlayerToken}",
                correlationId, SensitiveLogValue.Fingerprint(playerId));

            // Get entitlements from PlayFab inventory
            var inventoryResult = await _granter.GetPlayerItemsAsync(playerId, ct);
            if (!inventoryResult.IsSuccess)
            {
                _logger.LogWarning(
                    "[{CorrelationId}] Inventory query failed: Error={ErrorCode}, Retryable={Retryable}",
                    correlationId,
                    inventoryResult.ErrorCode,
                    inventoryResult.IsRetryable);
                return await CreateErrorResponse(
                    req,
                    "INVENTORY_UNAVAILABLE",
                    "Entitlements are temporarily unavailable",
                    HttpStatusCode.ServiceUnavailable,
                    correlationId,
                    stopwatch.ElapsedMilliseconds);
            }

            // Get active subscription from repository
            var activeSubscription = await _repository.GetActiveSubscriptionAsync(playerId, ct);

            // Build response
            var responseDto = new GetEntitlementsResponseDto
            {
                Entitlements = inventoryResult.Items.Select(item => new EntitlementItemDto
                {
                    ItemId = item.ItemId,
                    StackId = item.StackId,
                    Quantity = item.Amount,
                    ExpiresAtUtc = item.ExpiresAtUtc
                }).ToList(),
                ServerTimestampUtc = DateTime.UtcNow
            };

            if (activeSubscription != null && activeSubscription.IsActive)
            {
                responseDto.ActiveSubscription = new ActiveSubscriptionDto
                {
                    ProductId = activeSubscription.ProductId,
                    TierKey = activeSubscription.TierKey,
                    Status = activeSubscription.Status.ToString(),
                    AutoRenew = activeSubscription.AutoRenew,
                    PeriodStartUtc = activeSubscription.PeriodStartUtc,
                    PeriodEndUtc = activeSubscription.PeriodEndUtc,
                    OriginalPurchaseDateUtc = activeSubscription.OriginalPurchaseDateUtc,
                    Platform = activeSubscription.Platform,
                    GrantedItemId = activeSubscription.ActiveEconomyItemId,
                    GracePeriodDaysRemaining = CalculateGracePeriodDaysRemaining(activeSubscription)
                };
            }

            _logger.LogInformation(
                "[{CorrelationId}] Entitlements retrieved: {Count} items, Subscription={HasSub}",
                correlationId, responseDto.Entitlements.Count, activeSubscription != null);

            return await CreateSuccessResponse(req, responseDto, correlationId, stopwatch.ElapsedMilliseconds);
        }
        catch (RequestBodyTooLargeException)
        {
            return await CreateErrorResponse(req, "REQUEST_TOO_LARGE", "Request body is too large",
                HttpStatusCode.RequestEntityTooLarge, correlationId, stopwatch.ElapsedMilliseconds);
        }
        catch (InvalidDataException)
        {
            return await CreateErrorResponse(req, "INVALID_ENCODING", "Request body must be valid UTF-8",
                HttpStatusCode.BadRequest, correlationId, stopwatch.ElapsedMilliseconds);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[{CorrelationId}] JSON parsing error", correlationId);
            return await CreateErrorResponse(req, "INVALID_JSON", "Invalid JSON format",
                HttpStatusCode.BadRequest, correlationId, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Unexpected error in GetEntitlements", correlationId);
            return await CreateErrorResponse(req, "INTERNAL_ERROR", "An unexpected error occurred",
                HttpStatusCode.InternalServerError, correlationId, stopwatch.ElapsedMilliseconds);
        }
    }

    private static async Task<HttpResponseData> CreateSuccessResponse(
        HttpRequestData req,
        GetEntitlementsResponseDto data,
        string correlationId,
        long processingTimeMs)
    {
        var envelope = ResponseEnvelope<GetEntitlementsResponseDto>.Ok(data, correlationId, processingTimeMs);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(envelope);
        return response;
    }

    private static int? CalculateGracePeriodDaysRemaining(SubscriptionRecord subscription)
    {
        if (!subscription.GracePeriodEndUtc.HasValue)
        {
            return null;
        }

        var remainingDays = (subscription.GracePeriodEndUtc.Value - DateTime.UtcNow).TotalDays;
        return Math.Max(0, (int)Math.Ceiling(remainingDays));
    }

    private static async Task<HttpResponseData> CreateErrorResponse(
        HttpRequestData req,
        string errorCode,
        string message,
        HttpStatusCode statusCode,
        string correlationId,
        long processingTimeMs)
    {
        var error = ErrorPayload.Create(errorCode, message);
        var envelope = ResponseEnvelope<GetEntitlementsResponseDto>.Fail(error, correlationId, processingTimeMs);
        var response = req.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(envelope);
        return response;
    }
}
