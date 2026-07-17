using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Serhat.Forge.CloudScript.Domain;
using Serhat.Forge.CloudScript.Domain.DTOs;
using Serhat.Forge.CloudScript.Framework.Monetization.Configuration;
using Serhat.Forge.CloudScript.Framework.Monetization.Services;
using Serhat.Forge.CloudScript.Infrastructure.Logging;
using Serhat.Forge.CloudScript.Infrastructure.Security;

namespace Serhat.Forge.CloudScript.Functions.Monetization;

/// <summary>
/// Request DTO for purchase verification.
/// </summary>
public sealed class VerifyPurchaseRequestDto
{
    public string Platform { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public string ReceiptPayload { get; set; } = string.Empty;
    public string? PackageName { get; set; }
    public string? ProductType { get; set; }
    public string? TierKey { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// Response DTO for purchase verification.
/// </summary>
public sealed class VerifyPurchaseResponseDto
{
    public bool Success { get; set; }
    public string? TransactionKey { get; set; }
    public List<string> GrantedItemIds { get; set; } = new();
    public SubscriptionResponseDto? Subscription { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class SubscriptionResponseDto
{
    public string ProductId { get; set; } = string.Empty;
    public string TierKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool AutoRenew { get; set; }
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
}

/// <summary>
/// Azure Function for verifying in-app purchases.
/// </summary>
public sealed class VerifyPurchaseFunction
{
    private const int MaxRequestBodyBytes = 512 * 1024;

    private readonly PurchaseVerificationService _verificationService;
    private readonly MonetizationConfig _config;
    private readonly ICorrelationContext _correlationContext;
    private readonly ILogger<VerifyPurchaseFunction> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public VerifyPurchaseFunction(
        PurchaseVerificationService verificationService,
        MonetizationConfig config,
        ICorrelationContext correlationContext,
        ILogger<VerifyPurchaseFunction> logger)
    {
        _verificationService = verificationService;
        _config = config;
        _correlationContext = correlationContext;
        _logger = logger;
    }

    [Function("VerifyPurchase")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "monetization/verify")] HttpRequestData req,
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

            var parseResult = PlayFabRequestSecurity.ParseEnvelope<VerifyPurchaseRequestDto>(
                body,
                JsonOptions,
                _config.PlayFabTitleId,
                _config.EnvironmentName,
                "VerifyPurchase");
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

            var payload = envelope.Payload!;
            var playerId = envelope.Caller.PlayerId;

            _logger.LogInformation(
                "[{CorrelationId}] VerifyPurchase: PlayerToken={PlayerToken}, Platform={Platform}, Product={ProductId}",
                correlationId, SensitiveLogValue.Fingerprint(playerId), payload.Platform, payload.ProductId);

            // Validate request
            if (string.IsNullOrEmpty(payload.Platform) ||
                string.IsNullOrEmpty(payload.ProductId) ||
                string.IsNullOrEmpty(payload.TransactionId) ||
                string.IsNullOrEmpty(payload.ReceiptPayload))
            {
                return await CreateErrorResponse(req, "MISSING_FIELDS",
                    "Platform, ProductId, TransactionId, and ReceiptPayload are required",
                    HttpStatusCode.BadRequest, correlationId, stopwatch.ElapsedMilliseconds);
            }

            // Verify purchase
            var serviceRequest = new VerifyPurchaseServiceRequest
            {
                PlayerId = playerId,
                Platform = payload.Platform.ToLowerInvariant(),
                ProductId = payload.ProductId,
                TransactionId = payload.TransactionId,
                ReceiptPayload = payload.ReceiptPayload,
                PackageName = payload.PackageName,
                Metadata = payload.Metadata
            };

            var result = await _verificationService.VerifyAndGrantAsync(serviceRequest);

            // Map response
            var responseDto = new VerifyPurchaseResponseDto
            {
                Success = result.Success,
                TransactionKey = result.TransactionKey,
                GrantedItemIds = result.GrantedItemIds,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage
            };

            if (result.Subscription != null)
            {
                responseDto.Subscription = new SubscriptionResponseDto
                {
                    ProductId = result.Subscription.ProductId,
                    TierKey = result.Subscription.TierKey,
                    Status = result.Subscription.Status.ToString(),
                    AutoRenew = result.Subscription.AutoRenew,
                    PeriodStartUtc = result.Subscription.PeriodStartUtc,
                    PeriodEndUtc = result.Subscription.PeriodEndUtc
                };
            }

            if (!result.Success)
            {
                _logger.LogWarning(
                    "[{CorrelationId}] Verification failed: {ErrorCode} - {ErrorMessage}",
                    correlationId, result.ErrorCode, result.ErrorMessage);

                return await CreateErrorResponse(req, result.ErrorCode ?? "VERIFICATION_FAILED",
                    result.ErrorMessage ?? "Verification failed",
                    HttpStatusCode.BadRequest, correlationId, stopwatch.ElapsedMilliseconds);
            }

            _logger.LogInformation(
                "[{CorrelationId}] Verification succeeded: TransactionToken={TransactionToken}, ItemCount={ItemCount}",
                correlationId,
                SensitiveLogValue.Fingerprint(result.TransactionKey),
                result.GrantedItemIds.Count);

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
            _logger.LogError(ex, "[{CorrelationId}] Unexpected error in VerifyPurchase", correlationId);
            return await CreateErrorResponse(req, "INTERNAL_ERROR", "An unexpected error occurred",
                HttpStatusCode.InternalServerError, correlationId, stopwatch.ElapsedMilliseconds);
        }
    }

    private static async Task<HttpResponseData> CreateSuccessResponse(
        HttpRequestData req,
        VerifyPurchaseResponseDto data,
        string correlationId,
        long processingTimeMs)
    {
        var envelope = ResponseEnvelope<VerifyPurchaseResponseDto>.Ok(data, correlationId, processingTimeMs);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(envelope);
        return response;
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
        var envelope = ResponseEnvelope<VerifyPurchaseResponseDto>.Fail(error, correlationId, processingTimeMs);
        var response = req.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(envelope);
        return response;
    }
}
