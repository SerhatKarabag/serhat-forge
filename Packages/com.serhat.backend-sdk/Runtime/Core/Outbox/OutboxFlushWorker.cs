#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Serhat.Backend.Core.Outbox
{
    /// <summary>
    /// Background worker that flushes the outbox queue.
    /// </summary>
    public sealed class OutboxFlushWorker : IDisposable
    {
        private readonly PersistentOutbox _outbox;
        private readonly ICloudFunctionInvoker _invoker;
        private readonly IConnectivity _connectivity;
        private readonly OutboxOptions _options;
        private readonly IBackendLogger _logger;
        private readonly IClock _clock;
        private readonly ISerializer _serializer;

        private CancellationTokenSource? _cts;
        private Task? _workerTask;
        private bool _disposed;

        public bool IsRunning => _workerTask != null && !_workerTask.IsCompleted;
        public DateTime? LastFlushAttemptUtc { get; private set; }

        public OutboxFlushWorker(
            PersistentOutbox outbox,
            ICloudFunctionInvoker invoker,
            IConnectivity connectivity,
            OutboxOptions options,
            IBackendLogger logger,
            IClock clock,
            ISerializer serializer)
        {
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
            _connectivity = connectivity ?? throw new ArgumentNullException(nameof(connectivity));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        /// <summary>
        /// Starts the background flush worker.
        /// </summary>
        public void Start()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OutboxFlushWorker));

            if (IsRunning)
            {
                _logger.Warning("Flush worker is already running");
                return;
            }

            _cts = new CancellationTokenSource();
            _workerTask = RunWorkerAsync(_cts.Token);
            _logger.Info("Outbox flush worker started");
        }

        /// <summary>
        /// Stops the background flush worker.
        /// </summary>
        public async Task StopAsync()
        {
            if (_cts == null || _workerTask == null)
                return;

            _cts.Cancel();

            try
            {
                await _workerTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

            _cts.Dispose();
            _cts = null;
            _workerTask = null;

            _logger.Info("Outbox flush worker stopped");
        }

        /// <summary>
        /// Triggers an immediate flush attempt.
        /// </summary>
        public async Task FlushNowAsync(CancellationToken ct = default)
        {
            _logger.Debug("Manual flush triggered");
            await ProcessQueueAsync(ct);
        }

        private async Task RunWorkerAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await ProcessQueueAsync(ct);
                    await Task.Delay(_options.FlushInterval, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error("Error in flush worker", ex);
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
            }
        }

        private async Task ProcessQueueAsync(CancellationToken ct)
        {
            LastFlushAttemptUtc = _clock.UtcNow;

            if (!_connectivity.IsOnline)
            {
                _logger.Debug("Skipping flush - offline");
                return;
            }

            var status = _outbox.GetStatus();
            if (status.PendingCount == 0)
            {
                return;
            }

            _logger.Debug("Processing outbox queue ({0} pending)", status.PendingCount);

            int processed = 0;
            int maxPerFlush = 10; // Process in batches to not block too long

            while (processed < maxPerFlush && !ct.IsCancellationRequested)
            {
                var command = await _outbox.DequeueAsync(ct);
                if (command == null)
                    break;

                await ProcessCommandAsync(command, ct);
                processed++;
            }

            if (processed > 0)
            {
                _logger.Info("Processed {0} outbox commands", processed);
            }

            // Cleanup old dead letters periodically
            await _outbox.CleanupDeadLettersAsync(ct);
        }

        private async Task ProcessCommandAsync(OutboxCommand command, CancellationToken ct)
        {
            _logger.Debug("[{0}] Processing queued command: {1} (attempt {2})",
                command.CorrelationId, command.FunctionName, command.AttemptCount);

            try
            {
                var options = new CloudCallOptions
                {
                    CorrelationId = command.CorrelationId,
                    IdempotencyKey = Guid.Parse(command.IdempotencyKey)
                };

                // We need to invoke with the original payload
                // Since we store JSON, we use a generic object approach
                var result = await InvokeCommandAsync(command, options, ct);

                if (result.IsSuccess)
                {
                    await _outbox.CompleteAsync(command.CommandId, ct);
                }
                else
                {
                    await _outbox.FailAsync(
                        command.CommandId,
                        result.Error!.Code,
                        result.Error.Message,
                        result.Error.Retryable,
                        ct);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("[{0}] Exception processing command", ex, command.CorrelationId);
                await _outbox.FailAsync(
                    command.CommandId,
                    ErrorCodes.InternalError,
                    ex.Message,
                    retryable: true,
                    ct);
            }
        }

        private async Task<CloudResult<object>> InvokeCommandAsync(
            OutboxCommand command,
            CloudCallOptions options,
            CancellationToken ct)
        {
            // Create a wrapper request that includes the raw JSON payload
            var wrappedRequest = new OutboxCommandRequest
            {
                PayloadJson = command.PayloadJson,
                PayloadTypeName = command.PayloadTypeName
            };

            var result = await _invoker.ExecuteAsync<OutboxCommandRequest, OutboxCommandResponse>(
                command.FunctionName,
                wrappedRequest,
                options,
                ct);

            return result.IsSuccess
                ? CloudResult<object>.Success(result.Data!)
                : CloudResult<object>.Failure(result.Error!);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cts?.Cancel();
            _cts?.Dispose();
        }
    }

    /// <summary>
    /// Wrapper for outbox command execution.
    /// </summary>
    [Serializable]
    internal sealed class OutboxCommandRequest
    {
        public string PayloadJson { get; set; } = string.Empty;
        public string PayloadTypeName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Generic response for outbox commands.
    /// </summary>
    [Serializable]
    internal sealed class OutboxCommandResponse
    {
        public bool Success { get; set; }
        public string? ResultJson { get; set; }
    }
}
