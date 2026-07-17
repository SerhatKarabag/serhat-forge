#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Serhat.Analytics.Core.EventQueue
{
    /// <summary>
    /// Persistent storage for offline event queue.
    /// Follows the outbox pattern for reliable event delivery.
    /// </summary>
    public sealed class PersistentEventStore : IDisposable
    {
        private const string StorageKey = "analytics_offline_queue";

        private readonly OfflineQueueOptions _options;
        private readonly IStorage _storage;
        private readonly ISerializer _serializer;
        private readonly IClock _clock;
        private readonly IAnalyticsLogger _logger;

        private readonly SemaphoreSlim _lock = new(1, 1);
        private EventStoreState _state = new();
        private bool _loaded;
        private bool _disposed;

        /// <summary>
        /// Number of pending events in the store.
        /// </summary>
        public int PendingCount => _state.Events.Count;

        /// <summary>
        /// Oldest event timestamp in the store.
        /// </summary>
        public DateTime? OldestEventUtc => _state.Events.Count > 0 ? _state.Events[0].TimestampUtc : null;

        public PersistentEventStore(
            OfflineQueueOptions options,
            IStorage storage,
            ISerializer serializer,
            IClock clock,
            IAnalyticsLogger logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Loads the persisted state from storage.
        /// </summary>
        public async Task LoadAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                var json = await _storage.ReadAsync(StorageKey, ct);
                if (!string.IsNullOrEmpty(json))
                {
                    var loaded = _serializer.Deserialize<EventStoreState>(json);
                    if (loaded != null)
                    {
                        // Clean up old events based on retention period
                        var cutoff = _clock.UtcNow - _options.RetentionPeriod;
                        loaded.Events = loaded.Events
                            .Where(e => e.TimestampUtc > cutoff)
                            .ToList();

                        // Also remove events that exceeded max retry attempts
                        loaded.Events = loaded.Events
                            .Where(e => e.RetryCount < _options.MaxRetryAttempts)
                            .ToList();

                        _state = loaded;
                        _logger.Info("Loaded {0} pending analytics events from storage", _state.Events.Count);
                    }
                }
                _loaded = true;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to load persistent event store", ex);
                _state = new EventStoreState();
                _loaded = true;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Enqueues an event for persistent storage.
        /// </summary>
        public async Task<bool> EnqueueAsync(AnalyticsEvent evt, CancellationToken ct = default)
        {
            EnsureLoaded();

            await _lock.WaitAsync(ct);
            try
            {
                if (_state.Events.Count >= _options.MaxQueueSize)
                {
                    // Remove oldest event to make room
                    var removed = _state.Events[0];
                    _state.Events.RemoveAt(0);
                    _logger.Warning("Offline queue full, dropped oldest event: {0}", removed.EventName);
                }

                _state.Events.Add(evt);
                _state.LastModifiedUtc = _clock.UtcNow;

                await SaveAsync(ct);
                _logger.Debug("Event persisted to offline queue: {0} (queue size: {1})",
                    evt.EventName, _state.Events.Count);
                return true;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Enqueues multiple events for persistent storage.
        /// </summary>
        public async Task<int> EnqueueManyAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken ct = default)
        {
            if (events == null || events.Count == 0) return 0;

            EnsureLoaded();

            await _lock.WaitAsync(ct);
            try
            {
                var added = 0;
                foreach (var evt in events)
                {
                    if (_state.Events.Count >= _options.MaxQueueSize)
                    {
                        _state.Events.RemoveAt(0);
                        _logger.Warning("Offline queue full, dropped oldest event");
                    }

                    _state.Events.Add(evt);
                    added++;
                }

                _state.LastModifiedUtc = _clock.UtcNow;
                await SaveAsync(ct);

                _logger.Debug("Persisted {0} events to offline queue (total: {1})",
                    added, _state.Events.Count);
                return added;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Dequeues all events from the store.
        /// </summary>
        public async Task<IReadOnlyList<AnalyticsEvent>> DequeueAllAsync(CancellationToken ct = default)
        {
            EnsureLoaded();

            await _lock.WaitAsync(ct);
            try
            {
                if (_state.Events.Count == 0)
                {
                    return Array.Empty<AnalyticsEvent>();
                }

                var events = new List<AnalyticsEvent>(_state.Events);
                _state.Events.Clear();
                _state.LastModifiedUtc = _clock.UtcNow;

                await SaveAsync(ct);
                _logger.Debug("Dequeued all {0} events from offline queue", events.Count);
                return events;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Dequeues a batch of events from the store.
        /// </summary>
        public async Task<IReadOnlyList<AnalyticsEvent>> DequeueBatchAsync(int batchSize, CancellationToken ct = default)
        {
            EnsureLoaded();

            await _lock.WaitAsync(ct);
            try
            {
                var count = Math.Min(batchSize, _state.Events.Count);
                if (count == 0)
                {
                    return Array.Empty<AnalyticsEvent>();
                }

                var events = _state.Events.Take(count).ToList();
                _state.Events.RemoveRange(0, count);
                _state.LastModifiedUtc = _clock.UtcNow;

                await SaveAsync(ct);
                _logger.Debug("Dequeued batch of {0} events from offline queue", events.Count);
                return events;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Peeks at events without removing them.
        /// </summary>
        public async Task<IReadOnlyList<AnalyticsEvent>> PeekAsync(int count, CancellationToken ct = default)
        {
            EnsureLoaded();

            await _lock.WaitAsync(ct);
            try
            {
                return _state.Events.Take(count).ToList();
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Removes specific events from the store (after successful send).
        /// </summary>
        public async Task RemoveAsync(IReadOnlyList<string> eventIds, CancellationToken ct = default)
        {
            if (eventIds == null || eventIds.Count == 0) return;

            EnsureLoaded();

            await _lock.WaitAsync(ct);
            try
            {
                var idSet = new HashSet<string>(eventIds);
                var removed = _state.Events.RemoveAll(e => idSet.Contains(e.EventId));

                if (removed > 0)
                {
                    _state.LastModifiedUtc = _clock.UtcNow;
                    await SaveAsync(ct);
                    _logger.Debug("Removed {0} sent events from offline queue", removed);
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Increments retry count for specific events.
        /// </summary>
        public async Task IncrementRetryCountAsync(IReadOnlyList<string> eventIds, CancellationToken ct = default)
        {
            if (eventIds == null || eventIds.Count == 0) return;

            EnsureLoaded();

            await _lock.WaitAsync(ct);
            try
            {
                var idSet = new HashSet<string>(eventIds);
                var toRemove = new List<AnalyticsEvent>();

                foreach (var evt in _state.Events.Where(e => idSet.Contains(e.EventId)))
                {
                    evt.RetryCount++;
                    if (evt.RetryCount >= _options.MaxRetryAttempts)
                    {
                        toRemove.Add(evt);
                        _logger.Warning("Event exceeded max retries, dropping: {0}", evt.EventName);
                    }
                }

                foreach (var evt in toRemove)
                {
                    _state.Events.Remove(evt);
                }

                _state.LastModifiedUtc = _clock.UtcNow;
                await SaveAsync(ct);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Clears all events from the store.
        /// </summary>
        public async Task ClearAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                _state.Events.Clear();
                _state.LastModifiedUtc = _clock.UtcNow;
                await SaveAsync(ct);
                _logger.Info("Cleared offline event queue");
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task SaveAsync(CancellationToken ct)
        {
            try
            {
                var json = _serializer.Serialize(_state);
                await _storage.WriteAsync(StorageKey, json, ct);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save persistent event store", ex);
            }
        }

        private void EnsureLoaded()
        {
            if (!_loaded)
            {
                throw new InvalidOperationException("Event store not loaded. Call LoadAsync() first.");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lock.Dispose();
        }
    }

    /// <summary>
    /// Internal state for the persistent event store.
    /// </summary>
    [Serializable]
    internal sealed class EventStoreState
    {
        public List<AnalyticsEvent> Events { get; set; } = new();
        public DateTime LastModifiedUtc { get; set; }
        public int Version { get; set; } = 1;
    }
}
