#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Backend.Core.Telemetry;

namespace Serhat.Backend.Core.Resilience
{
    /// <summary>
    /// Retry policy with exponential backoff and jitter.
    /// </summary>
    public sealed class RetryPolicy
    {
        private readonly RetryOptions _options;
        private readonly IClock _clock;
        private readonly IRandom _random;
        private readonly IBackendLogger _logger;
        private readonly IBackendTelemetrySink? _telemetry;

        public RetryPolicy(
            RetryOptions options,
            IClock clock,
            IRandom random,
            IBackendLogger logger,
            IBackendTelemetrySink? telemetry = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _telemetry = telemetry;
        }

        /// <summary>
        /// Executes an operation with retry logic.
        /// </summary>
        public async Task<CloudResult<T>> ExecuteAsync<T>(
            Func<CancellationToken, Task<CloudResult<T>>> operation,
            string operationName,
            string correlationId,
            CancellationToken ct = default)
        {
            CloudResult<T> lastResult = default;
            var attempt = 0;

            while (attempt < _options.MaxAttempts)
            {
                attempt++;
                ct.ThrowIfCancellationRequested();

                try
                {
                    lastResult = await operation(ct);

                    if (lastResult.IsSuccess)
                    {
                        if (attempt > 1)
                        {
                            _logger.Info("[{0}] Operation succeeded after {1} attempts", correlationId, attempt);
                        }
                        return lastResult;
                    }

                    // Check if error is retryable
                    if (!lastResult.Error!.Retryable)
                    {
                        _logger.Debug("[{0}] Non-retryable error, not retrying: {1}",
                            correlationId, lastResult.Error.Code);
                        return lastResult;
                    }

                    // Last attempt, don't wait
                    if (attempt >= _options.MaxAttempts)
                    {
                        break;
                    }

                    // Calculate and apply backoff
                    var delay = CalculateDelay(attempt);
                    _logger.Debug("[{0}] Attempt {1} failed, retrying in {2}ms: {3}",
                        correlationId, attempt, delay.TotalMilliseconds, lastResult.Error.Code);

                    _telemetry?.OnRetry(new RetryEvent
                    {
                        CorrelationId = correlationId,
                        OperationName = operationName,
                        Attempt = attempt,
                        DelayMs = (long)delay.TotalMilliseconds,
                        ErrorCode = lastResult.Error.Code
                    });

                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error("[{0}] Unexpected exception on attempt {1}", ex, correlationId, attempt);

                    if (attempt >= _options.MaxAttempts)
                    {
                        return CloudResult<T>.Failure(new BackendError(
                            ErrorCodes.InternalError,
                            "Operation failed after all retry attempts",
                            retryable: false,
                            correlationId: correlationId));
                    }

                    var delay = CalculateDelay(attempt);
                    await Task.Delay(delay, ct);
                }
            }

            _logger.Warning("[{0}] All {1} attempts exhausted for operation: {2}",
                correlationId, _options.MaxAttempts, operationName);

            return lastResult.IsSuccess ? lastResult : CloudResult<T>.Failure(new BackendError(
                lastResult.Error?.Code ?? ErrorCodes.InternalError,
                $"Operation failed after {_options.MaxAttempts} attempts: {lastResult.Error?.Message}",
                retryable: false,
                correlationId: correlationId));
        }

        /// <summary>
        /// Calculates delay with exponential backoff and jitter.
        /// </summary>
        public TimeSpan CalculateDelay(int attempt)
        {
            // Exponential backoff: initialDelay * (multiplier ^ (attempt - 1))
            var exponentialDelay = _options.InitialDelay.TotalMilliseconds *
                                   Math.Pow(_options.BackoffMultiplier, attempt - 1);

            // Cap at max delay
            exponentialDelay = Math.Min(exponentialDelay, _options.MaxDelay.TotalMilliseconds);

            // Add jitter: +/- jitterFactor
            var jitterRange = exponentialDelay * _options.JitterFactor;
            var jitter = (_random.NextDouble() * 2 - 1) * jitterRange;
            var finalDelay = exponentialDelay + jitter;

            // Ensure non-negative
            finalDelay = Math.Max(0, finalDelay);

            return TimeSpan.FromMilliseconds(finalDelay);
        }

        /// <summary>
        /// Determines if an error should be retried.
        /// </summary>
        public static bool ShouldRetry(BackendError error)
        {
            if (!error.Retryable)
                return false;

            // Additional classification based on error code
            return error.Code switch
            {
                ErrorCodes.NetworkError => true,
                ErrorCodes.Timeout => true,
                ErrorCodes.ServiceUnavailable => true,
                ErrorCodes.RateLimited => true,
                ErrorCodes.Conflict => true, // Optimistic concurrency, worth retrying

                ErrorCodes.Unauthorized => false,
                ErrorCodes.Forbidden => false,
                ErrorCodes.ValidationFailed => false,
                ErrorCodes.InvalidRequest => false,
                ErrorCodes.NotFound => false,
                ErrorCodes.InsufficientFunds => false,

                _ => error.Retryable
            };
        }
    }
}
