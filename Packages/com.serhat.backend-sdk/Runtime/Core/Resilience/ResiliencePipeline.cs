#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Backend.Core.Telemetry;

namespace Serhat.Backend.Core.Resilience
{
    /// <summary>
    /// Combines all resilience policies into a single pipeline.
    /// Order: ConcurrencyLimit -> CircuitBreaker -> Retry -> Timeout -> Operation
    /// </summary>
    public sealed class ResiliencePipeline : IDisposable
    {
        private readonly RetryPolicy _retryPolicy;
        private readonly CircuitBreaker _circuitBreaker;
        private readonly ConcurrencyLimiter _concurrencyLimiter;
        private readonly BackendSdkOptions _options;
        private readonly IBackendLogger _logger;
        private readonly IBackendTelemetrySink? _telemetry;
        private readonly IClock _clock;

        public ResiliencePipeline(
            RetryPolicy retryPolicy,
            CircuitBreaker circuitBreaker,
            ConcurrencyLimiter concurrencyLimiter,
            BackendSdkOptions options,
            IBackendLogger logger,
            IClock clock,
            IBackendTelemetrySink? telemetry = null)
        {
            _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
            _circuitBreaker = circuitBreaker ?? throw new ArgumentNullException(nameof(circuitBreaker));
            _concurrencyLimiter = concurrencyLimiter ?? throw new ArgumentNullException(nameof(concurrencyLimiter));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _telemetry = telemetry;
        }

        /// <summary>
        /// Executes a read operation through the full resilience pipeline.
        /// </summary>
        public Task<CloudResult<T>> ExecuteReadAsync<T>(
            Func<CancellationToken, Task<CloudResult<T>>> operation,
            string operationName,
            string correlationId,
            TimeSpan? timeout = null,
            CancellationToken ct = default)
        {
            return ExecuteAsync(operation, operationName, correlationId, isWrite: false, timeout, ct);
        }

        /// <summary>
        /// Executes a write operation through the full resilience pipeline.
        /// </summary>
        public Task<CloudResult<T>> ExecuteWriteAsync<T>(
            Func<CancellationToken, Task<CloudResult<T>>> operation,
            string operationName,
            string correlationId,
            TimeSpan? timeout = null,
            CancellationToken ct = default)
        {
            return ExecuteAsync(operation, operationName, correlationId, isWrite: true, timeout, ct);
        }

        private async Task<CloudResult<T>> ExecuteAsync<T>(
            Func<CancellationToken, Task<CloudResult<T>>> operation,
            string operationName,
            string correlationId,
            bool isWrite,
            TimeSpan? timeout,
            CancellationToken ct)
        {
            var effectiveTimeout = timeout ?? _options.DefaultTimeout;
            var startTime = _clock.TimestampMs;

            _telemetry?.OnRequestStart(new RequestStartEvent
            {
                CorrelationId = correlationId,
                OperationName = operationName,
                IsWrite = isWrite,
                TimestampMs = startTime
            });

            try
            {
                // Layer 1: Concurrency limit
                var concurrencyResult = isWrite
                    ? await _concurrencyLimiter.ExecuteWriteAsync(
                        ct2 => ExecuteWithCircuitBreakerAsync(operation, operationName, correlationId, effectiveTimeout, ct2),
                        correlationId,
                        effectiveTimeout,
                        ct)
                    : await _concurrencyLimiter.ExecuteReadAsync(
                        ct2 => ExecuteWithCircuitBreakerAsync(operation, operationName, correlationId, effectiveTimeout, ct2),
                        correlationId,
                        effectiveTimeout,
                        ct);

                var durationMs = _clock.TimestampMs - startTime;

                _telemetry?.OnRequestEnd(new RequestEndEvent
                {
                    CorrelationId = correlationId,
                    OperationName = operationName,
                    IsWrite = isWrite,
                    DurationMs = durationMs,
                    Success = concurrencyResult.IsSuccess,
                    ErrorCode = concurrencyResult.Error?.Code
                });

                return concurrencyResult;
            }
            catch (OperationCanceledException)
            {
                var durationMs = _clock.TimestampMs - startTime;
                _telemetry?.OnRequestEnd(new RequestEndEvent
                {
                    CorrelationId = correlationId,
                    OperationName = operationName,
                    IsWrite = isWrite,
                    DurationMs = durationMs,
                    Success = false,
                    ErrorCode = ErrorCodes.Timeout
                });

                return CloudResult<T>.Failure(new BackendError(
                    ErrorCodes.Timeout,
                    "Operation was cancelled or timed out",
                    retryable: true,
                    correlationId: correlationId));
            }
        }

        private async Task<CloudResult<T>> ExecuteWithCircuitBreakerAsync<T>(
            Func<CancellationToken, Task<CloudResult<T>>> operation,
            string operationName,
            string correlationId,
            TimeSpan timeout,
            CancellationToken ct)
        {
            // Layer 2: Circuit breaker
            return await _circuitBreaker.ExecuteAsync(
                ct2 => ExecuteWithRetryAsync(operation, operationName, correlationId, timeout, ct2),
                correlationId,
                ct);
        }

        private async Task<CloudResult<T>> ExecuteWithRetryAsync<T>(
            Func<CancellationToken, Task<CloudResult<T>>> operation,
            string operationName,
            string correlationId,
            TimeSpan timeout,
            CancellationToken ct)
        {
            // Layer 3: Retry
            return await _retryPolicy.ExecuteAsync(
                ct2 => ExecuteWithTimeoutAsync(operation, timeout, correlationId, ct2),
                operationName,
                correlationId,
                ct);
        }

        private async Task<CloudResult<T>> ExecuteWithTimeoutAsync<T>(
            Func<CancellationToken, Task<CloudResult<T>>> operation,
            TimeSpan timeout,
            string correlationId,
            CancellationToken ct)
        {
            // Layer 4: Timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            try
            {
                return await operation(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout occurred, not external cancellation
                _logger.Debug("[{0}] Operation timed out after {1}ms", correlationId, timeout.TotalMilliseconds);
                return CloudResult<T>.Failure(new BackendError(
                    ErrorCodes.Timeout,
                    $"Operation timed out after {timeout.TotalMilliseconds}ms",
                    retryable: true,
                    correlationId: correlationId));
            }
        }

        public void Dispose()
        {
            _concurrencyLimiter.Dispose();
        }
    }
}
