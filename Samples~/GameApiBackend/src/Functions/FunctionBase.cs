using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Serhat.Forge.CloudScript.Domain;
using Serhat.Forge.CloudScript.Domain.DTOs;
using Serhat.Forge.CloudScript.Infrastructure.GameApiSecurity;
using Serhat.Forge.CloudScript.Infrastructure.Idempotency;
using Serhat.Forge.CloudScript.Infrastructure.Logging;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Serhat.Forge.CloudScript.Functions;

/// <summary>
/// Base class for all Azure Functions with common functionality.
/// </summary>
public abstract class FunctionBase
{
    private const int MaxRequestBodyBytes = 512 * 1024;
    protected readonly IIdempotencyStore IdempotencyStore;
    protected readonly ICorrelationContext CorrelationContext;
    protected readonly ILogger Logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 32
    };

    protected FunctionBase(
        IIdempotencyStore idempotencyStore,
        ICorrelationContext correlationContext,
        ILogger logger)
    {
        IdempotencyStore = idempotencyStore;
        CorrelationContext = correlationContext;
        Logger = logger;
    }

    /// <summary>
    /// Parses request body into strongly typed envelope.
    /// </summary>
    protected async Task<(RequestEnvelope<TPayload>? Envelope, HttpResponseData? ErrorResponse)>
        ParseRequestAsync<TPayload>(HttpRequestData request) where TPayload : class
    {
        try
        {
            var body = await GameApiHttpRequestSecurity.ReadUtf8BodyAsync(
                request.Body,
                MaxRequestBodyBytes);
            if (string.IsNullOrWhiteSpace(body))
            {
                var errorResponse = await CreateErrorResponseAsync<object>(
                    request,
                    ErrorCodes.InvalidRequest,
                    "Request body is required",
                    HttpStatusCode.BadRequest,
                    string.Empty,
                    0);
                return (null, errorResponse);
            }

            var environmentName = FirstNonEmpty(
                Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT"),
                Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
                "Production");
            var expectedTitleId = Environment.GetEnvironmentVariable("PLAYFAB_TITLE_ID")?.Trim()
                ?? string.Empty;
            var actualFunctionName = request.FunctionContext.FunctionDefinition.Name;
            var parseResult = GameApiPlayFabRequestSecurity.ParseEnvelope<TPayload>(
                body,
                JsonOptions,
                expectedTitleId,
                environmentName,
                actualFunctionName);
            if (!parseResult.IsSuccess)
            {
                var errorResponse = await CreateErrorResponseAsync<object>(
                    request,
                    parseResult.ErrorCode ?? ErrorCodes.InvalidRequest,
                    parseResult.IsUnauthorized
                        ? "Authenticated PlayFab context is required"
                        : "Failed to parse request",
                    parseResult.IsUnauthorized
                        ? HttpStatusCode.Unauthorized
                        : HttpStatusCode.BadRequest,
                    string.Empty,
                    0);
                return (null, errorResponse);
            }

            var envelope = parseResult.Envelope!;
            CorrelationContext.SetCorrelationId(envelope.CorrelationId);
            return (envelope, null);
        }
        catch (GameApiRequestBodyTooLargeException)
        {
            var response = await CreateErrorResponseAsync<object>(
                request,
                "REQUEST_TOO_LARGE",
                "Request body is too large",
                HttpStatusCode.RequestEntityTooLarge,
                string.Empty,
                0);
            return (null, response);
        }
        catch (InvalidDataException)
        {
            var response = await CreateErrorResponseAsync<object>(
                request,
                "INVALID_ENCODING",
                "Request body must be valid UTF-8",
                HttpStatusCode.BadRequest,
                string.Empty,
                0);
            return (null, response);
        }
        catch (JsonException ex)
        {
            Logger.LogWarning("JSON request rejected: {ErrorType}", ex.GetType().Name);
            var errorResponse = await CreateErrorResponseAsync<object>(
                request,
                ErrorCodes.SerializationError,
                "Invalid JSON format",
                HttpStatusCode.BadRequest,
                string.Empty,
                0);
            return (null, errorResponse);
        }
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
        ?? string.Empty;
    /// <summary>
    /// Extracts player ID from the request context (PlayFab sets this).
    /// </summary>
    protected string GetPlayerId<TPayload>(HttpRequestData request, RequestEnvelope<TPayload>? envelope)
        where TPayload : class
    {
        // In production, PlayFab CloudScript provides the caller identity
        // For local dev, use the caller context from envelope
        var playerId = envelope?.Caller?.PlayerId;
        if (!string.IsNullOrWhiteSpace(playerId))
        {
            return playerId;
        }

        var userId = envelope?.Caller?.UserId;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            return userId;
        }

        Logger.LogWarning("Caller context did not include playerId/userId; rejecting request");
        return string.Empty;
    }

    /// <summary>
    /// Ensures player id is present for authenticated calls.
    /// </summary>
    protected async Task<HttpResponseData?> EnsurePlayerIdAsync<TResponse>(
        HttpRequestData request,
        string playerId,
        string correlationId,
        long processingTimeMs = 0)
        where TResponse : class
    {
        if (!string.IsNullOrWhiteSpace(playerId))
        {
            return null;
        }

        return await CreateErrorResponseAsync<TResponse>(
            request,
            ErrorCodes.Unauthorized,
            "Authenticated player id is required",
            HttpStatusCode.Unauthorized,
            correlationId,
            processingTimeMs);
    }

    /// <summary>
    /// Executes an idempotent write operation.
    /// </summary>
    protected async Task<HttpResponseData> ExecuteIdempotentAsync<TPayload, TResult>(
        HttpRequestData request,
        RequestEnvelope<TPayload> envelope,
        string playerId,
        Func<TPayload, Task<(TResult? Result, ErrorPayload? Error)>> operation)
        where TPayload : class
        where TResult : class
    {
        var stopwatch = Stopwatch.StartNew();
        var correlationId = envelope.CorrelationId;
        var functionName = envelope.FunctionName;

        // Validate idempotency key
        if (string.IsNullOrEmpty(envelope.IdempotencyKey))
        {
            return await CreateErrorResponseAsync<TResult>(
                request,
                ErrorCodes.MissingIdempotencyKey,
                "Idempotency key is required for write operations",
                HttpStatusCode.BadRequest,
                correlationId,
                stopwatch.ElapsedMilliseconds);
        }

        // Check idempotency store
        var beginResult = await IdempotencyStore.TryBeginAsync(
            playerId, functionName, envelope.IdempotencyKey);

        if (!beginResult.Success)
        {
            // Operation already exists
            switch (beginResult.ExistingStatus)
            {
                case IdempotencyStatus.Completed:
                    Logger.LogInformation(
                        "[{CorrelationId}] Returning cached result for idempotent request",
                        correlationId);

                    if (!string.IsNullOrEmpty(beginResult.ExistingResponsePayload))
                    {
                        var cachedResult = JsonSerializer.Deserialize<TResult>(
                            beginResult.ExistingResponsePayload, JsonOptions);
                        return await CreateSuccessResponseAsync(
                            request, cachedResult!, correlationId, stopwatch.ElapsedMilliseconds);
                    }
                    break;

                case IdempotencyStatus.InProgress:
                    return await CreateErrorResponseAsync<TResult>(
                        request,
                        ErrorCodes.IdempotencyConflict,
                        "Request is already being processed",
                        HttpStatusCode.Conflict,
                        correlationId,
                        stopwatch.ElapsedMilliseconds,
                        retryable: true);

                case IdempotencyStatus.Failed:
                    return await CreateErrorResponseAsync<TResult>(
                        request,
                        beginResult.ErrorCode ?? ErrorCodes.InternalError,
                        beginResult.ErrorMessage ?? "Previous request failed",
                        HttpStatusCode.InternalServerError,
                        correlationId,
                        stopwatch.ElapsedMilliseconds);
            }
        }

        try
        {
            // Execute the operation
            var (result, error) = await operation(envelope.Payload!);

            if (error != null)
            {
                await IdempotencyStore.FailAsync(
                    playerId, functionName, envelope.IdempotencyKey,
                    error.Code, error.Message);

                return await CreateErrorResponseAsync<TResult>(
                    request,
                    error.Code,
                    error.Message,
                    HttpStatusCode.BadRequest,
                    correlationId,
                    stopwatch.ElapsedMilliseconds,
                    error.Retryable);
            }

            // Store successful result
            var responseJson = JsonSerializer.Serialize(result, JsonOptions);
            await IdempotencyStore.CompleteAsync(
                playerId, functionName, envelope.IdempotencyKey, responseJson);

            return await CreateSuccessResponseAsync(
                request, result!, correlationId, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{CorrelationId}] Error executing {Function}", correlationId, functionName);

            await IdempotencyStore.FailAsync(
                playerId, functionName, envelope.IdempotencyKey,
                ErrorCodes.InternalError, "An unexpected error occurred");

            return await CreateErrorResponseAsync<TResult>(
                request,
                ErrorCodes.InternalError,
                "An unexpected error occurred",
                HttpStatusCode.InternalServerError,
                correlationId,
                stopwatch.ElapsedMilliseconds,
                retryable: true);
        }
    }

    /// <summary>
    /// Creates a success response.
    /// </summary>
    protected async Task<HttpResponseData> CreateSuccessResponseAsync<T>(
        HttpRequestData request,
        T data,
        string correlationId,
        long processingTimeMs) where T : class
    {
        var envelope = ResponseEnvelope<T>.Ok(data, correlationId, processingTimeMs);

        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(envelope);

        Logger.LogInformation(
            "[{CorrelationId}] Request completed successfully in {Duration}ms",
            correlationId, processingTimeMs);

        return response;
    }

    /// <summary>
    /// Creates an error response.
    /// </summary>
    protected async Task<HttpResponseData> CreateErrorResponseAsync<T>(
        HttpRequestData request,
        string errorCode,
        string message,
        HttpStatusCode statusCode,
        string correlationId,
        long processingTimeMs,
        bool retryable = false,
        Dictionary<string, string>? details = null) where T : class
    {
        var error = ErrorPayload.Create(errorCode, message, retryable, details);
        var envelope = ResponseEnvelope<T>.Fail(error, correlationId, processingTimeMs);

        var response = request.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(envelope);

        Logger.LogWarning(
            "[{CorrelationId}] Request failed: {ErrorCode} ({Duration}ms)",
            correlationId, errorCode, processingTimeMs);

        return response;
    }
}
