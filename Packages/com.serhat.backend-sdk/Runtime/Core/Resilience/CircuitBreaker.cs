#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Serhat.Backend.Core.Resilience
{
    /// <summary>
    /// Circuit breaker states.
    /// </summary>
    public enum CircuitState
    {
        Closed,     // Normal operation
        Open,       // Failing fast
        HalfOpen    // Testing if service recovered
    }

    /// <summary>
    /// Circuit breaker implementation to prevent cascading failures.
    /// </summary>
    public sealed class CircuitBreaker
    {
        private readonly CircuitBreakerOptions _options;
        private readonly IClock _clock;
        private readonly IBackendLogger _logger;
        private readonly object _lock = new();

        private CircuitState _state = CircuitState.Closed;
        private int _consecutiveFailures;
        private int _successesInHalfOpen;
        private DateTime _openedAtUtc;
        private DateTime _lastFailureAtUtc;

        public CircuitState State
        {
            get { lock (_lock) return _state; }
        }

        public int ConsecutiveFailures
        {
            get { lock (_lock) return _consecutiveFailures; }
        }

        public CircuitBreaker(
            CircuitBreakerOptions options,
            IClock clock,
            IBackendLogger logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Executes an operation through the circuit breaker.
        /// </summary>
        public async Task<CloudResult<T>> ExecuteAsync<T>(
            Func<CancellationToken, Task<CloudResult<T>>> operation,
            string correlationId,
            CancellationToken ct = default)
        {
            if (!_options.Enabled)
            {
                return await operation(ct);
            }

            // Check if circuit allows execution
            if (!CanExecute(out var reason))
            {
                _logger.Debug("[{0}] Circuit breaker rejected request: {1}", correlationId, reason);
                return CloudResult<T>.Failure(new BackendError(
                    ErrorCodes.CircuitOpen,
                    $"Circuit breaker is open: {reason}",
                    retryable: true,
                    correlationId: correlationId));
            }

            try
            {
                var result = await operation(ct);

                if (result.IsSuccess)
                {
                    OnSuccess();
                }
                else if (result.Error!.Retryable)
                {
                    OnFailure();
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                OnFailure();
                throw;
            }
        }

        /// <summary>
        /// Checks if circuit allows execution.
        /// </summary>
        private bool CanExecute(out string reason)
        {
            lock (_lock)
            {
                switch (_state)
                {
                    case CircuitState.Closed:
                        reason = string.Empty;
                        return true;

                    case CircuitState.Open:
                        var elapsed = _clock.UtcNow - _openedAtUtc;
                        if (elapsed >= _options.OpenDuration)
                        {
                            _state = CircuitState.HalfOpen;
                            _successesInHalfOpen = 0;
                            _logger.Info("Circuit breaker transitioning to HalfOpen");
                            reason = string.Empty;
                            return true;
                        }
                        reason = $"Circuit open, {(_options.OpenDuration - elapsed).TotalSeconds:F1}s until half-open";
                        return false;

                    case CircuitState.HalfOpen:
                        reason = string.Empty;
                        return true;

                    default:
                        reason = "Unknown state";
                        return false;
                }
            }
        }

        /// <summary>
        /// Records a successful operation.
        /// </summary>
        private void OnSuccess()
        {
            lock (_lock)
            {
                switch (_state)
                {
                    case CircuitState.Closed:
                        _consecutiveFailures = 0;
                        break;

                    case CircuitState.HalfOpen:
                        _successesInHalfOpen++;
                        if (_successesInHalfOpen >= _options.SuccessThresholdInHalfOpen)
                        {
                            _state = CircuitState.Closed;
                            _consecutiveFailures = 0;
                            _successesInHalfOpen = 0;
                            _logger.Info("Circuit breaker closed after successful half-open tests");
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Records a failed operation.
        /// </summary>
        private void OnFailure()
        {
            lock (_lock)
            {
                _lastFailureAtUtc = _clock.UtcNow;
                _consecutiveFailures++;

                switch (_state)
                {
                    case CircuitState.Closed:
                        if (_consecutiveFailures >= _options.FailureThreshold)
                        {
                            _state = CircuitState.Open;
                            _openedAtUtc = _clock.UtcNow;
                            _logger.Warning("Circuit breaker opened after {0} consecutive failures",
                                _consecutiveFailures);
                        }
                        break;

                    case CircuitState.HalfOpen:
                        _state = CircuitState.Open;
                        _openedAtUtc = _clock.UtcNow;
                        _successesInHalfOpen = 0;
                        _logger.Warning("Circuit breaker re-opened from half-open state");
                        break;
                }
            }
        }

        /// <summary>
        /// Manually resets the circuit breaker to closed state.
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _state = CircuitState.Closed;
                _consecutiveFailures = 0;
                _successesInHalfOpen = 0;
                _logger.Info("Circuit breaker manually reset");
            }
        }
    }
}
