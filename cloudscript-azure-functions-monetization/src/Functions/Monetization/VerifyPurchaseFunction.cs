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
    /// <summary>
    /// Google Play purchase token. Must be empty for Apple, which is verified by transaction ID.
    /// </summary>
    public string ReceiptPayload { get; set; } = string.Empty;
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
    public bool WasDuplicate { get; set; }
}

public sealed class SubscriptionResponseDto
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

    /// <summary>
    /// Validates only transport-required fields. Platform support remains the service's
    /// responsibility so unknown values receive the canonical INVALID_PLATFORM response.
    /// Google identity is the purchase token; its client transaction/order ID is optional.
    /// </summary>
    public static string? ValidateRequiredFields(VerifyPurchaseRequestDto? payload)
    {
        if (payload == null ||
            string.IsNullOrWhiteSpace(payload.Platform) ||
            string.IsNullOrWhiteSpace(payload.ProductId))
        {
            return "Platform and ProductId are required";
        }

        if (string.Equals(payload.Platform, "apple", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(payload.TransactionId))
        {
            return "TransactionId is required for Apple purchases";
        }

        if (string.Equals(payload.Platform, "google", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(payload.ReceiptPayload))
        {
            return "ReceiptPayload purchase token is required for Google purchases";
        }

        return null;
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

            if ((string.Equals(payload.Platform, "apple", StringComparison.OrdinalIgnoreCase) &&
                 !_config.Apple.Enabled) ||
                (string.Equals(payload.Platform, "google", StringComparison.OrdinalIgnoreCase) &&
                 !_config.Google.Enabled))
            {
                return await CreateErrorResponse(
                    req,
                    "STORE_DISABLED",
                    "The requested store is disabled for this deployment",
                    HttpStatusCode.BadRequest,
                    correlationId,
                    stopwatch.ElapsedMilliseconds);
            }

            // Validate request
            var requiredFieldError = ValidateRequiredFields(payload);
            if (requiredFieldError != null)
            {
                return await CreateErrorResponse(req, "MISSING_FIELDS",
                    requiredFieldError,
                    HttpStatusCode.BadRequest, correlationId, stopwatch.ElapsedMilliseconds);
            }

            // Verify purchase
            var serviceRequest = new VerifyPurchaseServiceRequest
            {
                PlayerId = playerId,
                Platform = payload.Platform.ToLowerInvariant(),
                ProductId = payload.ProductId,
                TransactionId = payload.TransactionId,
                // Never pass a legacy/raw Apple receipt into the verification pipeline. Apple's
                // authoritative lookup uses TransactionId; Google requires ReceiptPayload.
                ReceiptPayload = string.Equals(
                    payload.Platform,
                    "apple",
                    StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : payload.ReceiptPayload
            };

            var result = await _verificationService.VerifyAndGrantAsync(serviceRequest, ct);

            // Map response
            var responseDto = new VerifyPurchaseResponseDto
            {
                Success = result.Success,
                TransactionKey = result.TransactionKey,
                GrantedItemIds = result.GrantedItemIds,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage,
                WasDuplicate = result.IsDuplicate
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
                    PeriodEndUtc = result.Subscription.PeriodEndUtc,
                    OriginalPurchaseDateUtc = result.Subscription.OriginalPurchaseDateUtc,
                    Platform = result.Subscription.Platform,
                    GrantedItemId = result.Subscription.GrantedItemId,
                    GracePeriodDaysRemaining = result.Subscription.GracePeriodDaysRemaining
                };
            }

            if (!result.Success)
            {
                _logger.LogWarning(
                    "[{CorrelationId}] Verification failed: {ErrorCode} - {ErrorMessage}",
                    correlationId, result.ErrorCode, result.ErrorMessage);

                return await CreateErrorResponse(req, result.ErrorCode ?? "VERIFICATION_FAILED",
                    result.ErrorMessage ?? "Verification failed",
                    result.Retryable
                        ? HttpStatusCode.ServiceUnavailable
                        : HttpStatusCode.BadRequest,
                    correlationId,
                    stopwatch.ElapsedMilliseconds,
                    result.Retryable);
            }

            _logger.LogInformation(
                "[{CorrelationId}] Verification succeeded: TransactionFingerprint={TransactionFingerprint}, ItemCount={ItemCount}",
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
        long processingTimeMs,
        bool retryable = false)
    {
        var error = ErrorPayload.Create(errorCode, message, retryable);
        var envelope = ResponseEnvelope<VerifyPurchaseResponseDto>.Fail(error, correlationId, processingTimeMs);
        var response = req.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(envelope);
        return response;
    }
}
