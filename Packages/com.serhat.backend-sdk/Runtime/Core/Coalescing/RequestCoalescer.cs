#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Serhat.Backend.Core.Coalescing
{
    /// <summary>
    /// Coalesces concurrent read requests for the same data into a single in-flight request.
    /// </summary>
    public sealed class RequestCoalescer : IDisposable
    {
        private readonly ConcurrentDictionary<string, CoalescedRequest> _inFlightRequests = new();
        private readonly IBackendLogger _logger;
        private readonly TimeSpan _requestTimeout;
        private bool _disposed;

        public RequestCoalescer(IBackendLogger logger, TimeSpan? requestTimeout = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        }

        /// <summary>
        /// Executes a request with coalescing. Multiple concurrent calls with the same key
        /// will share a single in-flight request.
        /// </summary>
        public async Task<CloudResult<T>> ExecuteAsync<T>(
            string coalescingKey,
            Func<CancellationToken, Task<CloudResult<T>>> requestFactory,
            string correlationId,
            CancellationToken ct = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RequestCoalescer));

            // Try to join an existing request
            if (_inFlightRequests.TryGetValue(coalescingKey, out var existing))
            {
                _logger.Debug("[{0}] Joining coalesced request for key: {1}", correlationId, coalescingKey);

                try
                {
                    var result = await WaitForResultAsync<T>(existing, ct);
                    return result;
                }
                catch (OperationCanceledException)
                {
                    return CloudResult<T>.Failure(new BackendError(
                        ErrorCodes.Timeout,
                        "Coalesced request was cancelled",
                        retryable: true,
                        correlationId: correlationId));
                }
            }

            // Create new coalesced request
            var newRequest = new CoalescedRequest();

            if (!_inFlightRequests.TryAdd(coalescingKey, newRequest))
            {
                // Another thread won the race, join their request
                if (_inFlightRequests.TryGetValue(coalescingKey, out existing))
                {
                    _logger.Debug("[{0}] Lost race, joining existing request for key: {1}",
                        correlationId, coalescingKey);
                    return await WaitForResultAsync<T>(existing, ct);
                }
            }

            // We are the leader - execute the request
            _logger.Debug("[{0}] Leading coalesced request for key: {1}", correlationId, coalescingKey);

            try
            {
                var result = await requestFactory(ct);

                // Store result and signal waiters
                newRequest.SetResult(result);

                return result;
            }
            catch (Exception ex)
            {
                var error = new BackendError(
                    ErrorCodes.InternalError,
                    ex.Message,
                    retryable: true,
                    correlationId: correlationId);

                newRequest.SetException(ex);

                return CloudResult<T>.Failure(error);
            }
            finally
            {
                // Remove from in-flight after a short delay to allow late joiners to get the result
                _ = RemoveAfterDelayAsync(coalescingKey, TimeSpan.FromMilliseconds(100));
            }
        }

        /// <summary>
        /// Gets the number of currently in-flight coalesced requests.
        /// </summary>
        public int InFlightCount => _inFlightRequests.Count;

        private async Task<CloudResult<T>> WaitForResultAsync<T>(CoalescedRequest request, CancellationToken ct)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_requestTimeout);

            try
            {
                await request.Completion.WaitAsync(timeoutCts.Token);

                if (request.Exception != null)
                {
                    throw request.Exception;
                }

                if (request.Result is CloudResult<T> typedResult)
                {
                    return typedResult;
                }

                // Type mismatch - shouldn't happen in normal usage
                return CloudResult<T>.Failure(new BackendError(
                    ErrorCodes.InternalError,
                    "Coalesced request type mismatch",
                    retryable: false));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return CloudResult<T>.Failure(new BackendError(
                    ErrorCodes.Timeout,
                    "Coalesced request timed out",
                    retryable: true));
            }
        }

        private async Task RemoveAfterDelayAsync(string key, TimeSpan delay)
        {
            await Task.Delay(delay);
            _inFlightRequests.TryRemove(key, out _);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var kvp in _inFlightRequests)
            {
                kvp.Value.Dispose();
            }
            _inFlightRequests.Clear();
        }

        /// <summary>
        /// Represents a single coalesced request that multiple callers can wait on.
        /// </summary>
        private sealed class CoalescedRequest : IDisposable
        {
            private readonly SemaphoreSlim _semaphore = new(0, int.MaxValue);
            private int _waiterCount;

            public object? Result { get; private set; }
            public Exception? Exception { get; private set; }
            public SemaphoreSlim Completion => _semaphore;

            public void SetResult(object result)
            {
                Result = result;
                _semaphore.Release(Math.Max(1, _waiterCount + 10)); // Release extra for late joiners
            }

            public void SetException(Exception ex)
            {
                Exception = ex;
                _semaphore.Release(Math.Max(1, _waiterCount + 10));
            }

            public void IncrementWaiters()
            {
                Interlocked.Increment(ref _waiterCount);
            }

            public void Dispose()
            {
                _semaphore.Dispose();
            }
        }
    }
}
