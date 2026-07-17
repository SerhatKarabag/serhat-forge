#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Serhat.Backend.Core.Resilience
{
    /// <summary>
    /// Concurrency limiter with separate limits for reads and writes.
    /// </summary>
    public sealed class ConcurrencyLimiter : IDisposable
    {
        private readonly SemaphoreSlim _readSemaphore;
        private readonly SemaphoreSlim _writeSemaphore;
        private readonly IBackendLogger _logger;
        private bool _disposed;

        public int CurrentReads => _readSemaphore.CurrentCount;
        public int CurrentWrites => _writeSemaphore.CurrentCount;

        public ConcurrencyLimiter(ConcurrencyOptions options, IBackendLogger logger)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _readSemaphore = new SemaphoreSlim(options.MaxConcurrentReads, options.MaxConcurrentReads);
            _writeSemaphore = new SemaphoreSlim(options.MaxConcurrentWrites, options.MaxConcurrentWrites);
        }

        /// <summary>
        /// Executes a read operation with concurrency limiting.
        /// </summary>
        public async Task<CloudResult<T>> ExecuteReadAsync<T>(
            Func<CancellationToken, Task<CloudResult<T>>> operation,
            string correlationId,
            TimeSpan timeout,
            CancellationToken ct = default)
        {
            return await ExecuteWithLimitAsync(operation, _readSemaphore, "read", correlationId, timeout, ct);
        }

        /// <summary>
        /// Executes a write operation with concurrency limiting.
        /// </summary>
        public async Task<CloudResult<T>> ExecuteWriteAsync<T>(
            Func<CancellationToken, Task<CloudResult<T>>> operation,
            string correlationId,
            TimeSpan timeout,
            CancellationToken ct = default)
        {
            return await ExecuteWithLimitAsync(operation, _writeSemaphore, "write", correlationId, timeout, ct);
        }

        private async Task<CloudResult<T>> ExecuteWithLimitAsync<T>(
            Func<CancellationToken, Task<CloudResult<T>>> operation,
            SemaphoreSlim semaphore,
            string operationType,
            string correlationId,
            TimeSpan timeout,
            CancellationToken ct)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ConcurrencyLimiter));

            bool acquired = false;
            try
            {
                acquired = await semaphore.WaitAsync(timeout, ct);

                if (!acquired)
                {
                    _logger.Warning("[{0}] Concurrency limit reached for {1} operations",
                        correlationId, operationType);

                    return CloudResult<T>.Failure(new BackendError(
                        ErrorCodes.RateLimited,
                        $"Too many concurrent {operationType} operations",
                        retryable: true,
                        correlationId: correlationId));
                }

                return await operation(ct);
            }
            finally
            {
                if (acquired)
                {
                    semaphore.Release();
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _readSemaphore.Dispose();
            _writeSemaphore.Dispose();
        }
    }
}
