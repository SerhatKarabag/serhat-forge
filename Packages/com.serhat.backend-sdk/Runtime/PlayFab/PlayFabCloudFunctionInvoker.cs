#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Backend.Core;
using Serhat.Backend.Core.Transport;
using PlayFab;
using PlayFab.CloudScriptModels;
using UnityEngine;

namespace Serhat.Backend.PlayFab
{
    /// <summary>
    /// PlayFab implementation of cloud function invoker.
    /// Uses ExecuteFunction to call Azure Functions registered with PlayFab.
    /// </summary>
    public sealed class PlayFabCloudFunctionInvoker : ICloudFunctionInvoker
    {
        private readonly BackendSdkOptions _options;
        private readonly ISerializer _serializer;
        private readonly IBackendLogger _logger;
        private readonly IClock _clock;
        private readonly Func<string>? _playerIdProvider;

        public PlayFabCloudFunctionInvoker(
            BackendSdkOptions options,
            ISerializer serializer,
            IBackendLogger logger,
            IClock clock,
            Func<string>? playerIdProvider = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _playerIdProvider = playerIdProvider;
        }

        public async Task<CloudResult<TResponse>> ExecuteAsync<TRequest, TResponse>(
            string functionName,
            TRequest request,
            CloudCallOptions options,
            CancellationToken ct = default)
            where TRequest : class
            where TResponse : class
        {
            if (string.IsNullOrEmpty(functionName))
                throw new ArgumentException("Function name cannot be null or empty", nameof(functionName));

            var correlationId = string.IsNullOrEmpty(options.CorrelationId)
                ? Guid.NewGuid().ToString("N")[..8]
                : options.CorrelationId;

            _logger.Debug("[{0}] Invoking function: {1}", correlationId, functionName);

            try
            {
                // Build request envelope
                var envelope = new RequestEnvelope<TRequest>
                {
                    FunctionName = functionName,
                    CorrelationId = correlationId,
                    IdempotencyKey = options.IdempotencyKey?.ToString(),
                    Payload = request,
                    TimestampMs = _clock.TimestampMs,
                    Caller = BuildCallerContext()
                };

                var envelopeJson = _serializer.Serialize(envelope);

                // Create PlayFab request
                var playFabRequest = new ExecuteFunctionRequest
                {
                    FunctionName = functionName,
                    FunctionParameter = envelope,
                    GeneratePlayStreamEvent = false
                };

                // Execute with cancellation support
                var tcs = new TaskCompletionSource<CloudResult<TResponse>>();
                ct.Register(() => tcs.TrySetCanceled());

                PlayFabCloudScriptAPI.ExecuteFunction(
                    playFabRequest,
                    result => OnSuccess(result, correlationId, tcs),
                    error => OnError(error, correlationId, tcs));

                return await tcs.Task;
            }
            catch (OperationCanceledException)
            {
                _logger.Debug("[{0}] Request cancelled", correlationId);
                return CloudResult<TResponse>.Failure(new BackendError(
                    ErrorCodes.Timeout,
                    "Request was cancelled",
                    retryable: true,
                    correlationId: correlationId));
            }
            catch (Exception ex)
            {
                _logger.Error("[{0}] Unexpected error invoking function", ex, correlationId);
                return CloudResult<TResponse>.Failure(new BackendError(
                    ErrorCodes.InternalError,
                    "An unexpected error occurred",
                    retryable: false,
                    correlationId: correlationId));
            }
        }

        private void OnSuccess<TResponse>(
            ExecuteFunctionResult result,
            string correlationId,
            TaskCompletionSource<CloudResult<TResponse>> tcs)
            where TResponse : class
        {
            try
            {
                if (result.Error != null)
                {
                    _logger.Warning("[{0}] Function returned error: {1}", correlationId, result.Error.Message);

                    var error = new BackendError(
                        result.Error.Error ?? ErrorCodes.ProviderError,
                        result.Error.Message ?? "Unknown PlayFab error",
                        retryable: IsRetryablePlayFabError(result.Error.Error),
                        correlationId: correlationId);

                    tcs.TrySetResult(CloudResult<TResponse>.Failure(error));
                    return;
                }

                // Parse response envelope
                var responseJson = result.FunctionResult?.ToString();
                if (string.IsNullOrEmpty(responseJson))
                {
                    _logger.Warning("[{0}] Empty response from function", correlationId);
                    tcs.TrySetResult(CloudResult<TResponse>.Failure(new BackendError(
                        ErrorCodes.InternalError,
                        "Empty response from server",
                        retryable: true,
                        correlationId: correlationId)));
                    return;
                }

                var envelope = _serializer.Deserialize<ResponseEnvelope<TResponse>>(responseJson);

                // Anchor server time as early as possible — even on failure envelopes we get a
                // trustworthy server UTC, which keeps the client clock aligned across error
                // retries. Guarded by `default` so old servers (field missing / zeroed) don't
                // snap the clock back to 0001-01-01.
                if (envelope != null
                    && envelope.ServerUtcNow != default
                    && _clock is IServerTimeAnchor anchor)
                {
                    anchor.AnchorToServerTime(envelope.ServerUtcNow);
                }

                if (envelope == null)
                {
                    _logger.Warning(
                        "[{0}] Failed to deserialize response. Raw response: {1}",
                        correlationId,
                        responseJson);
                    tcs.TrySetResult(CloudResult<TResponse>.Failure(new BackendError(
                        ErrorCodes.SerializationError,
                        "Failed to parse server response",
                        retryable: false,
                        correlationId: correlationId)));
                    return;
                }

                if (!envelope.Success || envelope.Error != null)
                {
                    if (envelope.Error == null)
                    {
                        _logger.Warning(
                            "[{0}] Failure response without error payload. Raw response: {1}",
                            correlationId,
                            responseJson);
                    }

                    var errorPayload = envelope.Error;
                    var error = new BackendError(
                        errorPayload?.Code ?? ErrorCodes.InternalError,
                        errorPayload?.Message ?? "Unknown error",
                        retryable: errorPayload?.Retryable ?? false,
                        correlationId: correlationId,
                        details: errorPayload?.Details);

                    tcs.TrySetResult(CloudResult<TResponse>.Failure(error));
                    return;
                }

                if (envelope.Data == null)
                {
                    _logger.Warning("[{0}] Success response with null data", correlationId);
                    tcs.TrySetResult(CloudResult<TResponse>.Failure(new BackendError(
                        ErrorCodes.InternalError,
                        "Server returned success but no data",
                        retryable: false,
                        correlationId: correlationId)));
                    return;
                }

                _logger.Debug("[{0}] Function completed successfully in {1}ms",
                    correlationId, envelope.ProcessingTimeMs);

                tcs.TrySetResult(CloudResult<TResponse>.Success(envelope.Data));
            }
            catch (Exception ex)
            {
                _logger.Error("[{0}] Error processing response", ex, correlationId);
                tcs.TrySetResult(CloudResult<TResponse>.Failure(new BackendError(
                    ErrorCodes.SerializationError,
                    "Failed to process server response",
                    retryable: false,
                    correlationId: correlationId)));
            }
        }

        private void OnError<TResponse>(
            PlayFabError error,
            string correlationId,
            TaskCompletionSource<CloudResult<TResponse>> tcs)
            where TResponse : class
        {
            _logger.Warning("[{0}] PlayFab error: {1} ({2})", correlationId, error.ErrorMessage, error.Error);

            var backendError = MapPlayFabError(error, correlationId);
            tcs.TrySetResult(CloudResult<TResponse>.Failure(backendError));
        }

        private BackendError MapPlayFabError(PlayFabError error, string correlationId)
        {
            var (code, retryable) = error.Error switch
            {
                PlayFabErrorCode.ServiceUnavailable => (ErrorCodes.ServiceUnavailable, true),
                PlayFabErrorCode.ConnectionError => (ErrorCodes.NetworkError, true),
                PlayFabErrorCode.APIClientRequestRateLimitExceeded => (ErrorCodes.RateLimited, true),
                PlayFabErrorCode.DataUpdateRateExceeded => (ErrorCodes.RateLimited, true),
                PlayFabErrorCode.ConcurrentEditError => (ErrorCodes.Conflict, true),
                PlayFabErrorCode.NotAuthenticated => (ErrorCodes.Unauthorized, false),
                PlayFabErrorCode.InvalidParams => (ErrorCodes.ValidationFailed, false),
                PlayFabErrorCode.AccountNotFound => (ErrorCodes.NotFound, false),
                _ => (ErrorCodes.ProviderError, error.HttpCode >= 500)
            };

            return new BackendError(
                code,
                error.ErrorMessage ?? "PlayFab error",
                retryable,
                httpStatus: error.HttpCode,
                providerErrorCode: (int)error.Error,
                correlationId: correlationId);
        }

        private bool IsRetryablePlayFabError(string? errorCode)
        {
            if (string.IsNullOrEmpty(errorCode))
                return false;

            return errorCode.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
                   errorCode.Contains("Unavailable", StringComparison.OrdinalIgnoreCase) ||
                   errorCode.Contains("RateLimit", StringComparison.OrdinalIgnoreCase) ||
                   errorCode.Contains("RateExceeded", StringComparison.OrdinalIgnoreCase) ||
                   errorCode.Contains("DataUpdateRateExceeded", StringComparison.OrdinalIgnoreCase) ||
                   errorCode.Contains("Throttle", StringComparison.OrdinalIgnoreCase);
        }

        private CallerContext BuildCallerContext()
        {
            var playerId = _playerIdProvider?.Invoke();
            if (string.IsNullOrWhiteSpace(playerId))
            {
                playerId = PlayFabSettings.staticPlayer.PlayFabId ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(playerId))
            {
                _logger.Warning("No PlayFabId available for cloud call; caller context will be empty");
                playerId = string.Empty;
            }
            else
            {
                _logger.Info("Using playerId for cloud call: {0}", playerId);
            }

            return new CallerContext
            {
                UserId = playerId,
                PlayerId = playerId,
                TitleId = _options.TitleId,
                Platform = Application.platform.ToString(),
                AppVersion = Application.version
            };
        }
    }
}
