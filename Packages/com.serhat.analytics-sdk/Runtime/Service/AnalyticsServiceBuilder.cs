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

namespace Serhat.Analytics
{
    /// <summary>
    /// Builder for creating AnalyticsService instances with customizable dependencies.
    /// </summary>
    public sealed class AnalyticsServiceBuilder
    {
        private AnalyticsSdkOptions _options = new();
        private IClock? _clock;
        private IAnalyticsLogger? _logger;
        private IConnectivity? _connectivity;
        private IStorage? _storage;
        private ISerializer? _serializer;
        private readonly List<IAnalyticsProvider> _providers = new();
        private bool _useFirebaseProvider;

        /// <summary>
        /// Configures the SDK options.
        /// </summary>
        public AnalyticsServiceBuilder WithOptions(AnalyticsSdkOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            return this;
        }

        /// <summary>
        /// Configures the SDK options using an action.
        /// </summary>
        public AnalyticsServiceBuilder WithOptions(Action<AnalyticsSdkOptions> configure)
        {
            configure?.Invoke(_options);
            return this;
        }

        /// <summary>
        /// Sets the application ID.
        /// </summary>
        public AnalyticsServiceBuilder WithAppId(string appId)
        {
            _options.AppId = appId;
            return this;
        }

        /// <summary>
        /// Sets the environment.
        /// </summary>
        public AnalyticsServiceBuilder WithEnvironment(string environment)
        {
            _options.Environment = environment;
            return this;
        }

        /// <summary>
        /// Sets the analytics mode.
        /// </summary>
        public AnalyticsServiceBuilder WithMode(AnalyticsMode mode)
        {
            _options.Mode = mode;
            return this;
        }

        /// <summary>
        /// Uses a custom clock implementation.
        /// </summary>
        public AnalyticsServiceBuilder WithClock(IClock clock)
        {
            _clock = clock;
            return this;
        }

        /// <summary>
        /// Uses a custom logger implementation.
        /// </summary>
        public AnalyticsServiceBuilder WithLogger(IAnalyticsLogger logger)
        {
            _logger = logger;
            return this;
        }

        /// <summary>
        /// Uses a custom connectivity checker implementation.
        /// </summary>
        public AnalyticsServiceBuilder WithConnectivity(IConnectivity connectivity)
        {
            _connectivity = connectivity;
            return this;
        }

        /// <summary>
        /// Uses a custom storage implementation.
        /// </summary>
        public AnalyticsServiceBuilder WithStorage(IStorage storage)
        {
            _storage = storage;
            return this;
        }

        /// <summary>
        /// Uses a custom serializer implementation.
        /// </summary>
        public AnalyticsServiceBuilder WithSerializer(ISerializer serializer)
        {
            _serializer = serializer;
            return this;
        }

        /// <summary>
        /// Adds an analytics provider.
        /// </summary>
        public AnalyticsServiceBuilder AddProvider(IAnalyticsProvider provider)
        {
            _providers.Add(provider ?? throw new ArgumentNullException(nameof(provider)));
            return this;
        }

        /// <summary>
        /// Adds Firebase Analytics provider.
        /// </summary>
        public AnalyticsServiceBuilder AddFirebase()
        {
            // Defer actual provider creation until BuildAsync so resolved logger can report diagnostics.
            _useFirebaseProvider = true;
            return this;
        }

        /// <summary>
        /// Builds and initializes the AnalyticsService.
        /// </summary>
        public async Task<IAnalyticsService> BuildAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Resolve dependencies with defaults
            var clock = _clock ?? SystemClock.Instance;
            var logger = _logger ?? new UnityAnalyticsLogger();
            var connectivity = _connectivity ?? new UnityConnectivity();
            var storage = _storage ?? new FileStorage("analytics_sdk");
            var serializer = _serializer ?? new AnalyticsJsonSerializer();

            AnalyticsEventQueue? eventQueue = null;
            PersistentEventStore? persistentStore = null;
            UserPropertyManager? userPropertyManager = null;
            AnalyticsService? service = null;
            var ownershipTransferred = false;

            try
            {
                // Log configuration
                if (_options.EnableDetailedLogging)
                {
                    logger.Info("Building AnalyticsService - AppId: {0}, Environment: {1}, Mode: {2}",
                        _options.AppId, _options.Environment, _options.Mode);
                }

                if (_useFirebaseProvider)
                {
                    TryAddFirebaseProvider(logger);
                }

                if (_providers.Count == 0)
                {
                    if (_options.Mode == AnalyticsMode.RemoteOnly)
                    {
                        throw new InvalidOperationException(
                            "Analytics is configured for RemoteOnly mode, but no provider is registered.");
                    }

                    if (_options.Mode == AnalyticsMode.DebugAndRemote)
                    {
                        logger.Warning(
                            "No analytics provider is configured; falling back to DebugOnly mode.");
                        _options.Mode = AnalyticsMode.DebugOnly;
                    }
                }

                // Create event queue
                eventQueue = new AnalyticsEventQueue(_options.Batching, clock, logger);

                // Create persistent store (offline queue)
                if (_options.OfflineQueue.Enabled)
                {
                    persistentStore = new PersistentEventStore(
                        _options.OfflineQueue,
                        storage,
                        serializer,
                        clock,
                        logger);
                    await persistentStore.LoadAsync(cancellationToken);
                }

                // Create user property manager
                userPropertyManager = new UserPropertyManager(storage, serializer, logger);
                await userPropertyManager.LoadAsync(cancellationToken);

                // Create validator
                var validator = new EventValidator(_options.Validation, logger);

                // Initialize providers
                foreach (var provider in _providers)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await provider.InitializeAsync(cancellationToken);
                        logger.Info("Analytics provider initialized: {0}", provider.ProviderId);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.Error("Failed to initialize provider {0}", ex, provider.ProviderId);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Create service and transfer all disposable dependencies to it.
                service = new AnalyticsService(
                    _options,
                    _providers,
                    eventQueue,
                    persistentStore,
                    userPropertyManager,
                    validator,
                    connectivity,
                    clock,
                    logger);

                if (_options.Batching.AutoFlush)
                {
                    service.StartAutoFlush();
                }

                if (_options.Session.AutoTrackSession)
                {
                    service.StartSession();
                }

                cancellationToken.ThrowIfCancellationRequested();
                logger.Info("AnalyticsService initialized - Mode: {0}, Providers: {1}",
                    _options.Mode, _providers.Count);

                ownershipTransferred = true;
                return service;
            }
            finally
            {
                if (!ownershipTransferred)
                {
                    if (service != null)
                    {
                        DisposeSafely(service, logger, nameof(AnalyticsService));
                    }
                    else
                    {
                        DisposeSafely(userPropertyManager, logger, nameof(UserPropertyManager));
                        DisposeSafely(persistentStore, logger, nameof(PersistentEventStore));
                        DisposeSafely(eventQueue, logger, nameof(AnalyticsEventQueue));

                        foreach (var provider in _providers)
                        {
                            DisposeSafely(provider, logger, provider.ProviderId);
                        }
                    }
                }
            }
        }

        private static void DisposeSafely(
            IDisposable? disposable,
            IAnalyticsLogger logger,
            string resourceName)
        {
            if (disposable == null)
                return;

            try
            {
                disposable.Dispose();
            }
            catch (Exception exception)
            {
                logger.Error("Failed to dispose analytics resource {0}.", exception, resourceName);
            }
        }

        /// <summary>
        /// Creates a new builder instance.
        /// </summary>
        public static AnalyticsServiceBuilder Create() => new();

        private void TryAddFirebaseProvider(IAnalyticsLogger logger)
        {
            const string providerTypeName = "Serhat.Analytics.Providers.Firebase.FirebaseAnalyticsProvider";
            const string providerAssemblyName = "Serhat.AnalyticsSdk.Firebase";

            // Fast path with assembly-qualified name.
            var providerType = Type.GetType($"{providerTypeName}, {providerAssemblyName}", throwOnError: false);

            // Fallback: scan loaded assemblies in case runtime used a different assembly identity string.
            if (providerType == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    providerType = asm.GetType(providerTypeName, throwOnError: false);
                    if (providerType != null)
                    {
                        break;
                    }
                }
            }

            if (providerType == null)
            {
                logger.Warning("Firebase provider type not found. Ensure Firebase provider assembly is compiled and loaded.");
                return;
            }

            try
            {
                var instance = Activator.CreateInstance(providerType, logger)
                    ?? Activator.CreateInstance(providerType);

                if (instance is not IAnalyticsProvider provider)
                {
                    logger.Warning("Firebase provider type does not implement IAnalyticsProvider.");
                    DisposeSafely(instance as IDisposable, logger, providerTypeName);
                    return;
                }

                if (_providers.Any(p => string.Equals(p.ProviderId, provider.ProviderId, StringComparison.Ordinal)))
                {
                    logger.Warning("Firebase provider already added. Skipping duplicate registration.");
                    DisposeSafely(provider, logger, provider.ProviderId);
                    return;
                }

                _providers.Add(provider);
            }
            catch (Exception ex)
            {
                logger.Error("Failed to create Firebase provider instance.", ex);
            }
        }
    }
}
