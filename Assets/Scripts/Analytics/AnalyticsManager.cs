#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Analytics;
using UnityEngine;

namespace Serhat.Forge.Analytics
{
    /// <summary>
    /// Thin scene-side wrapper around <see cref="IAnalyticsService"/> from the Serhat Analytics SDK.
    ///
    /// <para>Goals of this wrapper:</para>
    /// <list type="bullet">
    /// <item>Single static accessor (<see cref="Service"/>) for ad-hoc tracking calls.</item>
    /// <item>Lifecycle handling: app pause/quit -&gt; flush.</item>
    /// <item>Provider configuration left to the user (Firebase, custom, etc.) via <see cref="ConfigureBuilder"/>.</item>
    /// </list>
    ///
    /// <para>Usage:</para>
    /// <code>
    /// // In a bootstrapper (after Auth):
    /// AnalyticsManager.ConfigureBuilder = b =&gt; b
    ///     .WithAppId("MyGame")
    ///     .WithEnvironment(Debug.isDebugBuild ? "dev" : "prod")
    ///     .AddFirebase();
    /// await analyticsManager.InitializeAsync(userId);
    ///
    /// // From gameplay:
    /// AnalyticsManager.Service?.TrackAsync(new AnalyticsEvent("level_start") { ... });
    /// </code>
    /// </summary>
    public sealed class AnalyticsManager : MonoBehaviour
    {
        /// <summary>Optional builder configurator. Set this BEFORE <see cref="InitializeAsync"/>.</summary>
        public static AnalyticsManager? Instance { get; private set; }

        [SerializeField] private bool _dontDestroyOnLoad = true;
        private readonly object _initializationSync = new();
        private Task? _initializationTask;
        private CancellationTokenSource? _lifetimeCts;
        private bool _isDestroyed;

        public static Action<AnalyticsServiceBuilder>? ConfigureBuilder;

        /// <summary>Globally accessible service. Null until InitializeAsync completes.</summary>
        public static IAnalyticsService? Service { get; private set; }

        public bool IsInitialized => Service?.IsInitialized ?? false;

        /// <summary>
        /// Builds and initializes the analytics service. Safe to call multiple times — the
        /// second call is a no-op.
        /// </summary>
        public Task InitializeAsync(
            string? userId = null,
            CancellationToken cancellationToken = default)
        {
            lock (_initializationSync)
            {
                if (_isDestroyed || Instance != this || _lifetimeCts == null)
                {
                    return Task.FromException(
                        new ObjectDisposedException(nameof(AnalyticsManager)));
                }

                if (Service != null)
                    return Task.CompletedTask;

                if (_initializationTask != null && !_initializationTask.IsCompleted)
                    return _initializationTask;

                var completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var initializationTask = completion.Task;
                _initializationTask = initializationTask;
                _ = RunInitializationAsync(
                    completion,
                    userId,
                    cancellationToken,
                    _lifetimeCts.Token);
                return initializationTask;
            }
        }

        private async Task RunInitializationAsync(
            TaskCompletionSource<bool> completion,
            string? userId,
            CancellationToken callerToken,
            CancellationToken lifetimeToken)
        {
            try
            {
                await InitializeInternalAsync(userId, callerToken, lifetimeToken);
                completion.TrySetResult(true);
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[AnalyticsManager] Initialization failed: {exception.Message}",
                    this);
                completion.TrySetException(exception);
            }
            finally
            {
                lock (_initializationSync)
                {
                    if (Service == null &&
                        ReferenceEquals(_initializationTask, completion.Task))
                    {
                        _initializationTask = null;
                    }
                }
            }
        }

        private async Task InitializeInternalAsync(
            string? userId,
            CancellationToken callerToken,
            CancellationToken lifetimeToken)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                callerToken,
                lifetimeToken);
            var cancellationToken = linkedCts.Token;
            IAnalyticsService? createdService = null;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var builder = AnalyticsServiceBuilder.Create();
                ConfigureBuilder?.Invoke(builder);
                createdService = await builder.BuildAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                lock (_initializationSync)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_isDestroyed || Instance != this)
                        throw new OperationCanceledException();

                    if (!string.IsNullOrEmpty(userId))
                        createdService.SetUserId(userId);

                    Service = createdService;
                    createdService = null;
                }

                Debug.Log("[AnalyticsManager] Initialized", this);
            }
            finally
            {
                createdService?.Dispose();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnSubsystemRegistration()
        {
            var previousService = Service;

            Service = null;
            Instance = null;
            ConfigureBuilder = null;

            previousService?.Dispose();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            lock (_initializationSync)
            {
                _isDestroyed = false;
                _lifetimeCts?.Dispose();
                _lifetimeCts = new CancellationTokenSource();
            }

            Instance = this;
            if (_dontDestroyOnLoad)
            {
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
            }
        }
        public void SetUserId(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return;
            Service?.SetUserId(userId);
        }

        public void SetUserProperty(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
                return;
            Service?.SetUserProperty(key, value);
        }

        private void OnApplicationPause(bool pause)
        {
            if (pause)
                _ = Service?.FlushAsync();
        }

        private async void OnApplicationQuit()
        {
            if (Service != null)
                await Service.FlushAsync();
        }

        private void OnDestroy()
        {
            if (Instance != this)
                return;

            CancellationTokenSource? lifetimeCts;
            IAnalyticsService? service;

            lock (_initializationSync)
            {
                _isDestroyed = true;
                Instance = null;

                lifetimeCts = _lifetimeCts;
                _lifetimeCts = null;
                service = Service;
                Service = null;
            }

            lifetimeCts?.Cancel();
            lifetimeCts?.Dispose();
            service?.Dispose();
        }
    }
}
