#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Analytics.Core;
using Serhat.Analytics.Core.EventQueue;
using Serhat.Analytics.Core.UserProperties;
using Serhat.Analytics.Core.Validation;
using Serhat.Analytics.Providers;
using UnityEngine;

namespace Serhat.Analytics
{
    /// <summary>
    /// Main analytics service implementation.
    /// </summary>
    public sealed class AnalyticsService : IAnalyticsService
    {
        private readonly AnalyticsSdkOptions _options;
        private readonly IReadOnlyList<IAnalyticsProvider> _providers;
        private readonly AnalyticsEventQueue _eventQueue;
        private readonly PersistentEventStore? _persistentStore;
        private readonly UserPropertyManager _userPropertyManager;
        private readonly EventValidator _validator;
        private readonly IConnectivity _connectivity;
        private readonly IClock _clock;
        private readonly IAnalyticsLogger _logger;
        private readonly object _lifecycleSync = new();
        private readonly HashSet<Task> _inFlightOperations = new();
        private readonly SemaphoreSlim _dispatchLock = new(1, 1);

        private CancellationTokenSource? _flushCts;
        private Task? _flushTask;
        private bool _enabled = true;
        private bool _disposed;
        private int _resourcesDisposed;
        private DateTime _sessionStartTime;
        private int _sessionEventCount;

        public bool IsInitialized { get; private set; }
        public AnalyticsMode Mode { get; private set; }
        public bool IsEnabled => _enabled && Mode != AnalyticsMode.Disabled;
        public string? SessionId => _userPropertyManager.SessionId;
        public string? UserId => _userPropertyManager.UserId;

        public event Action<AnalyticsEvent>? OnEventTracked;
        public event Action<int>? OnEventsFlushed;
        public event Action<Exception>? OnFlushFailed;

        internal AnalyticsService(
            AnalyticsSdkOptions options,
            IReadOnlyList<IAnalyticsProvider> providers,
            AnalyticsEventQueue eventQueue,
            PersistentEventStore? persistentStore,
            UserPropertyManager userPropertyManager,
            EventValidator validator,
            IConnectivity connectivity,
            IClock clock,
            IAnalyticsLogger logger)
        {
            _options = options;
            _providers = providers;
            _eventQueue = eventQueue;
            _persistentStore = persistentStore;
            _userPropertyManager = userPropertyManager;
            _validator = validator;
            _connectivity = connectivity;
            _clock = clock;
            _logger = logger;

            Mode = options.Mode;
            IsInitialized = true;

            // Subscribe to batch ready event
            _eventQueue.OnBatchReady += OnBatchReady;

            // Subscribe to connectivity changes
            if (_connectivity is UnityConnectivity)
            {
                _connectivity.OnConnectivityChanged += OnConnectivityChanged;
            }
        }

        #region Event Tracking

        public void Track(AnalyticsEvent evt)
        {
            if (!IsEnabled || _disposed) return;
            if (evt == null) return;

            // Validate event
            var (isValid, sanitizedEvent) = _validator.ValidateAndSanitize(evt);
            if (!isValid)
            {
                if (_options.Validation.StrictMode)
                {
                    throw new ArgumentException($"Invalid event: {evt.EventName}");
                }
                return; // Skip invalid events in non-strict mode
            }

            // Enrich event with context
            EnrichEvent(sanitizedEvent);

            // Log to console in debug mode
            if (Mode == AnalyticsMode.DebugOnly || Mode == AnalyticsMode.DebugAndRemote)
            {
                LogEventToConsole(sanitizedEvent);
            }

            // Send to providers in remote mode
            if (Mode == AnalyticsMode.DebugAndRemote || Mode == AnalyticsMode.RemoteOnly)
            {
                _eventQueue.Enqueue(sanitizedEvent);
            }

            _sessionEventCount++;
            OnEventTracked?.Invoke(sanitizedEvent);
        }

        public void Track(string eventName, Dictionary<string, object>? parameters = null)
        {
            Track(new AnalyticsEvent(eventName, parameters));
        }

        private void EnrichEvent(AnalyticsEvent evt)
        {
            evt.UserId ??= UserId;
            evt.SessionId ??= SessionId;

            // Add standard context parameters
            if (!evt.Parameters.ContainsKey("app_version"))
            {
                evt.Parameters["app_version"] = Application.version;
            }

            if (!evt.Parameters.ContainsKey("platform"))
            {
                evt.Parameters["platform"] = Application.platform.ToString();
            }

            if (!evt.Parameters.ContainsKey("environment"))
            {
                evt.Parameters["environment"] = _options.Environment;
            }
        }

        private void LogEventToConsole(AnalyticsEvent evt)
        {
            var paramsStr = evt.Parameters.Count > 0
                ? string.Join(", ", evt.Parameters.Select(kv => $"{kv.Key}={kv.Value}"))
                : "none";

            _logger.Debug("TRACK: [{0}] {1} | params: {2}",
                evt.Category,
                evt.EventName,
                paramsStr);
        }

        #endregion

        #region User Management

        public void SetUserId(string userId)
        {
            if (_disposed) return;

            _ = _userPropertyManager.SetUserIdAsync(userId);

            // Also set on providers
            foreach (var provider in _providers)
            {
                _ = provider.SetUserIdAsync(userId);
            }

            _logger.Info("User ID set: {0}", userId);
        }

        public void ClearUserId()
        {
            if (_disposed) return;

            _ = _userPropertyManager.ClearUserIdAsync();

            foreach (var provider in _providers)
            {
                _ = provider.SetUserIdAsync(null!);
            }

            _logger.Info("User ID cleared");
        }

        public void SetUserProperty(string name, object value)
        {
            if (_disposed) return;

            _ = _userPropertyManager.SetPropertyAsync(name, value);

            foreach (var provider in _providers)
            {
                _ = provider.SetUserPropertyAsync(name, value);
            }
        }

        public void SetUserProperties(Dictionary<string, object> properties)
        {
            if (_disposed) return;

            _ = _userPropertyManager.SetPropertiesAsync(properties);

            foreach (var provider in _providers)
            {
                foreach (var kvp in properties)
                {
                    _ = provider.SetUserPropertyAsync(kvp.Key, kvp.Value);
                }
            }
        }

        #endregion

        #region Session Management

        public void StartSession()
        {
            if (_disposed) return;

            var isFirstSession = SessionId == null;
            _userPropertyManager.GenerateSessionId();
            _sessionStartTime = _clock.UtcNow;
            _sessionEventCount = 0;

            if (_options.Session.AutoTrackSession)
            {
                Track(new AnalyticsEvent("session_start")
                    .WithCategory(EventCategory.Session)
                    .WithParameter("session_id", SessionId!)
                    .WithParameter("is_first_session", isFirstSession));
            }

            _logger.Info("Session started: {0}", SessionId!);
        }

        public void EndSession()
        {
            if (_disposed) return;
            if (SessionId == null) return;

            var duration = (_clock.UtcNow - _sessionStartTime).TotalSeconds;

            if (_options.Session.AutoTrackSession)
            {
                Track(new AnalyticsEvent("session_end")
                    .WithCategory(EventCategory.Session)
                    .WithParameter("session_id", SessionId!)
                    .WithParameter("duration_seconds", duration)
                    .WithParameter("events_tracked", _sessionEventCount));
            }

            _logger.Info("Session ended: {0} (duration: {1:F1}s, events: {2})",
                SessionId, duration, _sessionEventCount);

            _userPropertyManager.SetSessionId(null);
        }

        #endregion

        #region Control

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            _logger.Info("Analytics tracking {0}", enabled ? "enabled" : "disabled");
        }

        public void SetMode(AnalyticsMode mode)
        {
            if (_providers.Count == 0 &&
                (mode == AnalyticsMode.DebugAndRemote || mode == AnalyticsMode.RemoteOnly))
            {
                throw new InvalidOperationException(
                    $"Cannot enable remote analytics mode '{mode}' without a provider.");
            }

            Mode = mode;
            _logger.Info("Analytics mode set to: {0}", mode);
        }

        public Task FlushAsync() => StartTrackedOperation(FlushCoreAsync);

        private async Task FlushCoreAsync()
        {
            var events = new List<AnalyticsEvent>();
            await _dispatchLock.WaitAsync();

            try
            {
                events.AddRange(_eventQueue.DrainAll());

                if (_persistentStore != null && _persistentStore.PendingCount > 0)
                {
                    var offlineEvents = await _persistentStore.DequeueAllAsync();
                    events.AddRange(offlineEvents);
                }

                if (events.Count == 0)
                    return;

                if (!_connectivity.IsOnline)
                {
                    await StoreForRetryAsync(events, incrementRetry: false);
                    _logger.Debug("Offline: retained {0} events for later", events.Count);
                    return;
                }

                var success = await SendToProvidersAsync(events);
                if (success)
                {
                    _logger.Debug("Flushed {0} events to providers", events.Count);
                    InvokeEventsFlushedSafely(events.Count);
                }
                else
                {
                    await StoreForRetryAsync(events, incrementRetry: true);
                }
            }
            catch (Exception ex)
            {
                if (events.Count > 0)
                {
                    try
                    {
                        await StoreForRetryAsync(events, incrementRetry: true);
                    }
                    catch (Exception recoveryException)
                    {
                        _logger.Error(
                            "Failed to retain analytics events after a flush error",
                            recoveryException);
                    }
                }

                _logger.Error("Failed to flush events", ex);
                InvokeFlushFailedSafely(ex);
            }
            finally
            {
                _dispatchLock.Release();
            }
        }

        public EventQueueStatus GetQueueStatus()
        {
            int inFlightOperationCount;
            lock (_lifecycleSync)
            {
                inFlightOperationCount = _inFlightOperations.Count;
            }

            return new EventQueueStatus
            {
                PendingCount = _eventQueue.Count,
                OfflineQueueCount = _persistentStore?.PendingCount ?? 0,
                OldestPendingUtc = _persistentStore?.OldestEventUtc,
                IsProcessing = inFlightOperationCount > 0
            };
        }

        #endregion

        #region Internal

        internal void StartAutoFlush()
        {
            if (!_options.Batching.AutoFlush) return;

            _flushCts = new CancellationTokenSource();
            _flushTask = RunAutoFlushLoop(_flushCts.Token);
        }

        private async Task RunAutoFlushLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_options.Batching.FlushInterval, ct);
                    await FlushAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error("Auto-flush error", ex);
                }
            }
        }

        private void OnBatchReady(IReadOnlyList<AnalyticsEvent> batch)
        {
            _ = StartTrackedOperation(() => SendBatchCoreAsync(batch));
        }

        private async Task SendBatchCoreAsync(IReadOnlyList<AnalyticsEvent> batch)
        {
            await _dispatchLock.WaitAsync();
            try
            {
                if (!_connectivity.IsOnline)
                {
                    await StoreForRetryAsync(batch, incrementRetry: false);
                    return;
                }

                if (await SendToProvidersAsync(batch))
                {
                    InvokeEventsFlushedSafely(batch.Count);
                }
                else
                {
                    await StoreForRetryAsync(batch, incrementRetry: true);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    await StoreForRetryAsync(batch, incrementRetry: true);
                }
                catch (Exception recoveryException)
                {
                    _logger.Error(
                        "Failed to retain analytics batch after a send error",
                        recoveryException);
                }

                _logger.Error("Batch send error", ex);
                InvokeFlushFailedSafely(ex);
            }
            finally
            {
                _dispatchLock.Release();
            }
        }

        private void OnConnectivityChanged(bool isOnline)
        {
            var hasPendingEvents =
                _eventQueue.Count > 0 ||
                (_persistentStore != null && _persistentStore.PendingCount > 0);
            if (isOnline && hasPendingEvents)
            {
                _logger.Info("Connectivity restored, flushing pending events");
                _ = FlushAsync();
            }
        }

        private async Task<bool> SendToProvidersAsync(IReadOnlyList<AnalyticsEvent> events)
        {
            if (_providers.Count == 0) return true;

            var allSuccess = true;
            var hasInitializedProvider = false;

            foreach (var provider in _providers)
            {
                if (!provider.IsInitialized) continue;
                hasInitializedProvider = true;

                try
                {
                    await provider.LogEventsAsync(events);
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to send events to provider {0}", ex, provider.ProviderId);
                    allSuccess = false;
                }
            }

            return hasInitializedProvider && allSuccess;
        }

        private async Task StoreForRetryAsync(
            IReadOnlyList<AnalyticsEvent> events,
            bool incrementRetry)
        {
            if (events.Count == 0)
                return;

            if (_persistentStore != null)
            {
                if (incrementRetry)
                {
                    foreach (var evt in events)
                    {
                        evt.RetryCount++;
                    }
                }

                await _persistentStore.EnqueueManyAsync(events);
                return;
            }

            _eventQueue.Requeue(events, incrementRetry);
        }

        private Task StartTrackedOperation(Func<Task> operationFactory)
        {
            TaskCompletionSource<bool> completion;
            Task trackedTask;

            lock (_lifecycleSync)
            {
                if (_disposed)
                    return Task.CompletedTask;

                completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                trackedTask = completion.Task;
                _inFlightOperations.Add(trackedTask);
            }

            _ = RunTrackedOperationAsync(operationFactory, completion, trackedTask);
            return trackedTask;
        }

        private async Task RunTrackedOperationAsync(
            Func<Task> operationFactory,
            TaskCompletionSource<bool> completion,
            Task trackedTask)
        {
            try
            {
                await operationFactory();
                completion.TrySetResult(true);
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
            finally
            {
                bool disposeResources;
                lock (_lifecycleSync)
                {
                    _inFlightOperations.Remove(trackedTask);
                    disposeResources = _disposed && _inFlightOperations.Count == 0;
                }

                if (disposeResources)
                    DisposeResources();
            }
        }

        private void InvokeEventsFlushedSafely(int eventCount)
        {
            try
            {
                OnEventsFlushed?.Invoke(eventCount);
            }
            catch (Exception exception)
            {
                _logger.Error("Analytics flush subscriber failed", exception);
            }
        }

        private void InvokeFlushFailedSafely(Exception error)
        {
            try
            {
                OnFlushFailed?.Invoke(error);
            }
            catch (Exception exception)
            {
                _logger.Error("Analytics failure subscriber failed", exception);
            }
        }

        #endregion

        public void Dispose()
        {
            bool disposeResources;
            lock (_lifecycleSync)
            {
                if (_disposed)
                    return;

                _disposed = true;
                disposeResources = _inFlightOperations.Count == 0;
            }

            _flushCts?.Cancel();
            _flushCts?.Dispose();
            _flushCts = null;

            _eventQueue.OnBatchReady -= OnBatchReady;
            _connectivity.OnConnectivityChanged -= OnConnectivityChanged;

            if (disposeResources)
                DisposeResources();
        }

        private void DisposeResources()
        {
            if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
                return;

            _eventQueue.Dispose();
            _persistentStore?.Dispose();
            _userPropertyManager.Dispose();
            _dispatchLock.Dispose();

            foreach (var provider in _providers)
            {
                try
                {
                    provider.Dispose();
                }
                catch (Exception exception)
                {
                    _logger.Error(
                        "Failed to dispose analytics provider {0}",
                        exception,
                        provider.ProviderId);
                }
            }

            _logger.Info("AnalyticsService disposed");
        }
    }
}
