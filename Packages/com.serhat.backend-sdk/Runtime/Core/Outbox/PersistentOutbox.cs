#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Backend.Core.Telemetry;

namespace Serhat.Backend.Core.Outbox
{
    /// <summary>
    /// Persistent outbox for durable write command queueing.
    /// Survives app restarts and handles offline scenarios.
    /// </summary>
    public sealed class PersistentOutbox : IDisposable
    {
        private const string StorageKey = "outbox_state";

        private readonly OutboxOptions _options;
        private readonly IStorage _storage;
        private readonly ISerializer _serializer;
        private readonly IClock _clock;
        private readonly IRandom _random;
        private readonly IBackendLogger _logger;
        private readonly IBackendTelemetrySink? _telemetry;
        private readonly RetryOptions _retryOptions;

        private readonly SemaphoreSlim _lock = new(1, 1);
        private OutboxState _state = new();
        private bool _loaded;
        private bool _disposed;

        public PersistentOutbox(
            OutboxOptions options,
            RetryOptions retryOptions,
            IStorage storage,
            ISerializer serializer,
            IClock clock,
            IRandom random,
            IBackendLogger logger,
            IBackendTelemetrySink? telemetry = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _retryOptions = retryOptions ?? throw new ArgumentNullException(nameof(retryOptions));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _telemetry = telemetry;
        }

        /// <summary>
        /// Loads state from storage. Call before using the outbox.
        /// </summary>
        public async Task LoadAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                var json = await _storage.ReadAsync(StorageKey, ct);
                if (!string.IsNullOrEmpty(json))
                {
                    var loadedState = _serializer.Deserialize<OutboxState>(json);
                    if (loadedState != null)
                    {
                        _state = loadedState;
                        _logger.Info("Outbox loaded: {0} pending, {1} dead letters",
                            _state.PendingCommands.Count, _state.DeadLetters.Count);
                    }
                }
                _loaded = true;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Enqueues a write command for later processing.
        /// </summary>
        public async Task<CloudResult<string>> EnqueueAsync<TRequest>(
            string functionName,
            TRequest request,
            Guid idempotencyKey,
            string correlationId,
            int priority = 5,
            CancellationToken ct = default)
            where TRequest : class
        {
            EnsureLoaded();

            await _lock.WaitAsync(ct);
            try
            {
                if (_state.PendingCommands.Count >= _options.MaxQueueSize)
                {
                    _logger.Warning("[{0}] Outbox queue full ({1} commands)",
                        correlationId, _state.PendingCommands.Count);

                    return CloudResult<string>.Failure(new BackendError(
                        ErrorCodes.OutboxFull,
                        $"Outbox queue is full ({_options.MaxQueueSize} commands)",
                        retryable: false,
                        correlationId: correlationId));
                }

                var command = new OutboxCommand
                {
                    CommandId = Guid.NewGuid().ToString("N"),
                    IdempotencyKey = idempotencyKey.ToString("N"),
                    CorrelationId = correlationId,
                    CreatedAtUtc = _clock.UtcNow,
                    FunctionName = functionName,
                    PayloadJson = _serializer.Serialize(request),
                    PayloadTypeName = typeof(TRequest).FullName ?? typeof(TRequest).Name,
                    AttemptCount = 0,
                    NextAttemptAtUtc = _clock.UtcNow,
                    Priority = priority,
                    Status = OutboxCommandStatus.Pending
                };

                _state.PendingCommands.Add(command);
                _state.LastModifiedUtc = _clock.UtcNow;

                await SaveStateAsync(ct);

                _logger.Info("[{0}] Command enqueued: {1} (queue size: {2})",
                    correlationId, functionName, _state.PendingCommands.Count);

                _telemetry?.OnEnqueue(new EnqueueEvent
                {
                    CorrelationId = correlationId,
                    CommandId = command.CommandId,
                    FunctionName = functionName,
                    QueueSize = _state.PendingCommands.Count
                });

                return CloudResult<string>.Success(command.CommandId);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Gets the next command ready for processing.
        /// </summary>
        public async Task<OutboxCommand?> DequeueAsync(CancellationToken ct = default)
        {
            EnsureLoaded();

            await _lock.WaitAsync(ct);
            try
            {
                var now = _clock.UtcNow;

                var command = _state.PendingCommands
                    .Where(c => c.Status == OutboxCommandStatus.Pending && c.NextAttemptAtUtc <= now)
                    .OrderBy(c => c.Priority)
                    .ThenBy(c => c.NextAttemptAtUtc)
                    .FirstOrDefault();

                if (command == null)
                    return null;

                command.Status = OutboxCommandStatus.InProgress;
                command.AttemptCount++;
                await SaveStateAsync(ct);

                return command;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Marks a command as successfully completed.
        /// </summary>
        public async Task CompleteAsync(string commandId, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                var command = _state.PendingCommands.FirstOrDefault(c => c.CommandId == commandId);
                if (command == null)
                {
                    _logger.Warning("Attempted to complete unknown command: {0}", commandId);
                    return;
                }

                command.Status = OutboxCommandStatus.Completed;
                command.CompletedAtUtc = _clock.UtcNow;

                _state.PendingCommands.Remove(command);
                _state.LastModifiedUtc = _clock.UtcNow;

                await SaveStateAsync(ct);

                _logger.Info("[{0}] Command completed: {1}", command.CorrelationId, command.FunctionName);

                _telemetry?.OnDequeue(new DequeueEvent
                {
                    CorrelationId = command.CorrelationId,
                    CommandId = commandId,
                    FunctionName = command.FunctionName,
                    Success = true,
                    AttemptCount = command.AttemptCount,
                    QueueSize = _state.PendingCommands.Count
                });
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Marks a command as failed and schedules retry or moves to dead letter.
        /// </summary>
        public async Task FailAsync(
            string commandId,
            string errorCode,
            string errorMessage,
            bool retryable,
            CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                var command = _state.PendingCommands.FirstOrDefault(c => c.CommandId == commandId);
                if (command == null)
                {
                    _logger.Warning("Attempted to fail unknown command: {0}", commandId);
                    return;
                }

                command.LastErrorCode = errorCode;
                command.LastErrorMessage = errorMessage;

                if (!retryable || command.AttemptCount >= _options.MaxCommandAttempts)
                {
                    // Move to dead letter
                    MoveToDeadLetter(command, retryable
                        ? $"Max attempts ({_options.MaxCommandAttempts}) exceeded"
                        : $"Non-retryable error: {errorCode}");
                }
                else
                {
                    // Schedule retry with exponential backoff
                    var delay = CalculateBackoff(command.AttemptCount);
                    command.NextAttemptAtUtc = _clock.UtcNow.Add(delay);
                    command.Status = OutboxCommandStatus.Pending;

                    _logger.Debug("[{0}] Command scheduled for retry in {1}s (attempt {2}/{3})",
                        command.CorrelationId, delay.TotalSeconds, command.AttemptCount, _options.MaxCommandAttempts);
                }

                _state.LastModifiedUtc = _clock.UtcNow;
                await SaveStateAsync(ct);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Gets current outbox status.
        /// </summary>
        public OutboxStatus GetStatus()
        {
            EnsureLoaded();

            lock (_state)
            {
                var pendingCommands = _state.PendingCommands
                    .Where(c => c.Status == OutboxCommandStatus.Pending)
                    .ToList();

                return new OutboxStatus
                {
                    PendingCount = pendingCommands.Count,
                    DeadLetterCount = _state.DeadLetters.Count,
                    OldestPendingUtc = pendingCommands.OrderBy(c => c.CreatedAtUtc).FirstOrDefault()?.CreatedAtUtc,
                    IsProcessing = _state.PendingCommands.Any(c => c.Status == OutboxCommandStatus.InProgress)
                };
            }
        }

        /// <summary>
        /// Gets all dead letter entries.
        /// </summary>
        public IReadOnlyList<DeadLetterEntry> GetDeadLetters()
        {
            EnsureLoaded();
            lock (_state)
            {
                return _state.DeadLetters.ToList();
            }
        }

        /// <summary>
        /// Removes a dead letter entry.
        /// </summary>
        public async Task RemoveDeadLetterAsync(string commandId, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                var entry = _state.DeadLetters.FirstOrDefault(d => d.Command.CommandId == commandId);
                if (entry != null)
                {
                    _state.DeadLetters.Remove(entry);
                    _state.LastModifiedUtc = _clock.UtcNow;
                    await SaveStateAsync(ct);
                    _logger.Info("Dead letter removed: {0}", commandId);
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Retries a dead letter entry.
        /// </summary>
        public async Task<CloudResult<bool>> RetryDeadLetterAsync(string commandId, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                var entry = _state.DeadLetters.FirstOrDefault(d => d.Command.CommandId == commandId);
                if (entry == null)
                {
                    return CloudResult<bool>.Failure(new BackendError(
                        ErrorCodes.NotFound,
                        "Dead letter entry not found"));
                }

                // Reset command for retry
                entry.Command.Status = OutboxCommandStatus.Pending;
                entry.Command.AttemptCount = 0;
                entry.Command.NextAttemptAtUtc = _clock.UtcNow;
                entry.Command.LastErrorCode = null;
                entry.Command.LastErrorMessage = null;

                _state.PendingCommands.Add(entry.Command);
                _state.DeadLetters.Remove(entry);
                _state.LastModifiedUtc = _clock.UtcNow;

                await SaveStateAsync(ct);

                _logger.Info("[{0}] Dead letter retried: {1}",
                    entry.Command.CorrelationId, entry.Command.FunctionName);

                return CloudResult<bool>.Success(true);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Cleans up old dead letters based on retention policy.
        /// </summary>
        public async Task CleanupDeadLettersAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                var cutoff = _clock.UtcNow - _options.DeadLetterRetentionPeriod;
                var toRemove = _state.DeadLetters
                    .Where(d => d.MovedToDeadLetterAtUtc < cutoff)
                    .ToList();

                if (toRemove.Count > 0)
                {
                    foreach (var entry in toRemove)
                    {
                        _state.DeadLetters.Remove(entry);
                    }
                    _state.LastModifiedUtc = _clock.UtcNow;
                    await SaveStateAsync(ct);

                    _logger.Info("Cleaned up {0} expired dead letters", toRemove.Count);
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        private void MoveToDeadLetter(OutboxCommand command, string reason)
        {
            command.Status = OutboxCommandStatus.DeadLetter;

            var deadLetter = new DeadLetterEntry
            {
                Command = command,
                FailureReason = reason,
                MovedToDeadLetterAtUtc = _clock.UtcNow
            };

            _state.PendingCommands.Remove(command);
            _state.DeadLetters.Add(deadLetter);

            _logger.Warning("[{0}] Command moved to dead letter: {1} - {2}",
                command.CorrelationId, command.FunctionName, reason);

            _telemetry?.OnDeadLetter(new DeadLetterEvent
            {
                CorrelationId = command.CorrelationId,
                CommandId = command.CommandId,
                FunctionName = command.FunctionName,
                Reason = reason,
                AttemptCount = command.AttemptCount
            });
        }

        private TimeSpan CalculateBackoff(int attempt)
        {
            var delay = _retryOptions.InitialDelay.TotalMilliseconds *
                        Math.Pow(_retryOptions.BackoffMultiplier, attempt - 1);
            delay = Math.Min(delay, _retryOptions.MaxDelay.TotalMilliseconds);

            var jitter = (_random.NextDouble() * 2 - 1) * delay * _retryOptions.JitterFactor;
            delay = Math.Max(0, delay + jitter);

            return TimeSpan.FromMilliseconds(delay);
        }

        private async Task SaveStateAsync(CancellationToken ct)
        {
            var json = _serializer.Serialize(_state);
            await _storage.WriteAsync(StorageKey, json, ct);
        }

        private void EnsureLoaded()
        {
            if (!_loaded)
            {
                throw new InvalidOperationException(
                    "Outbox has not been loaded. Call LoadAsync() before using the outbox.");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lock.Dispose();
        }
    }
}
