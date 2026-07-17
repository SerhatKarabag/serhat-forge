#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;

namespace Serhat.Analytics.Core.EventQueue
{
    /// <summary>
    /// Thread-safe in-memory event queue for batching analytics events.
    /// </summary>
    public sealed class AnalyticsEventQueue : IDisposable
    {
        private readonly BatchingOptions _options;
        private readonly IClock _clock;
        private readonly IAnalyticsLogger _logger;

        private readonly object _lock = new();
        private readonly List<AnalyticsEvent> _queue = new();
        private int _sequenceNumber;
        private bool _disposed;

        /// <summary>
        /// Event raised when the queue reaches max batch size.
        /// </summary>
        public event Action<IReadOnlyList<AnalyticsEvent>>? OnBatchReady;

        /// <summary>
        /// Current number of events in the queue.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _queue.Count;
                }
            }
        }

        public AnalyticsEventQueue(
            BatchingOptions options,
            IClock clock,
            IAnalyticsLogger logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Enqueues an event for batching.
        /// </summary>
        public void Enqueue(AnalyticsEvent evt)
        {
            if (_disposed) return;
            if (evt == null) return;

            IReadOnlyList<AnalyticsEvent>? batchToFlush = null;

            lock (_lock)
            {
                evt.SequenceNumber = Interlocked.Increment(ref _sequenceNumber);
                _queue.Add(evt);

                _logger.Debug("Event enqueued: {0} (queue size: {1})", evt.EventName, _queue.Count);

                // Check if batch is ready
                if (_queue.Count >= _options.MaxBatchSize)
                {
                    batchToFlush = ExtractBatchUnsafe();
                }
            }

            // Raise event outside of lock to prevent deadlocks
            if (batchToFlush != null && batchToFlush.Count > 0)
            {
                OnBatchReady?.Invoke(batchToFlush);
            }
        }

        /// <summary>
        /// Extracts all events from the queue.
        /// </summary>
        public IReadOnlyList<AnalyticsEvent> DrainAll()
        {
            lock (_lock)
            {
                if (_queue.Count == 0)
                {
                    return Array.Empty<AnalyticsEvent>();
                }

                var events = new List<AnalyticsEvent>(_queue);
                _queue.Clear();
                _logger.Debug("Queue drained: {0} events", events.Count);
                return events;
            }
        }

        /// <summary>
        /// Extracts a batch of events from the queue.
        /// </summary>
        public IReadOnlyList<AnalyticsEvent> DrainBatch(int maxCount)
        {
            lock (_lock)
            {
                return ExtractBatchUnsafe(maxCount);
            }
        }

        /// <summary>
        /// Gets a copy of all pending events without removing them.
        /// </summary>
        public IReadOnlyList<AnalyticsEvent> PeekAll()
        {
            lock (_lock)
            {
                return new List<AnalyticsEvent>(_queue);
            }
        }

        /// <summary>
        /// Clears all events from the queue.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _queue.Clear();
            }
        }

        /// <summary>
        /// Re-queues events that failed to send (for retry).
        /// Events are added to the front of the queue.
        /// </summary>
        public void Requeue(
            IReadOnlyList<AnalyticsEvent> events,
            bool incrementRetry = true)
        {
            if (events == null || events.Count == 0) return;

            lock (_lock)
            {
                if (incrementRetry)
                {
                    foreach (var evt in events)
                    {
                        evt.RetryCount++;
                    }
                }

                // Insert at the beginning for priority
                _queue.InsertRange(0, events);
                _logger.Debug("Requeued {0} events for retry", events.Count);
            }
        }

        private IReadOnlyList<AnalyticsEvent> ExtractBatchUnsafe(int? maxCount = null)
        {
            var count = maxCount ?? _options.MaxBatchSize;
            count = Math.Min(count, _queue.Count);

            if (count == 0)
            {
                return Array.Empty<AnalyticsEvent>();
            }

            var batch = _queue.GetRange(0, count);
            _queue.RemoveRange(0, count);
            return batch;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_lock)
            {
                _queue.Clear();
            }
        }
    }
}
