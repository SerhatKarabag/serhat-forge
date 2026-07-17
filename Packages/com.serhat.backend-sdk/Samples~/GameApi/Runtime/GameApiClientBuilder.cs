#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Backend.Core;
using Serhat.Backend.Core.Coalescing;
using Serhat.Backend.Core.Outbox;
using Serhat.Backend.Core.Resilience;
using Serhat.Backend.Core.Telemetry;

namespace Serhat.Backend.GameApi
{
    /// <summary>
    /// Builder for creating GameApiClient instances with customizable dependencies.
    /// </summary>
    public sealed class GameApiClientBuilder
    {
        private BackendSdkOptions _options = new();
        private IClock? _clock;
        private IRandom? _random;
        private IBackendLogger? _logger;
        private IConnectivity? _connectivity;
        private IStorage? _storage;
        private ISerializer? _serializer;
        private IBackendTelemetrySink? _telemetry;
        private ICloudFunctionInvoker? _invoker;

        /// <summary>
        /// Configures the SDK options.
        /// </summary>
        public GameApiClientBuilder WithOptions(BackendSdkOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            return this;
        }

        /// <summary>
        /// Configures the SDK options using an action.
        /// </summary>
        public GameApiClientBuilder WithOptions(Action<BackendSdkOptions> configure)
        {
            configure?.Invoke(_options);
            return this;
        }

        /// <summary>
        /// Sets the Title ID.
        /// </summary>
        public GameApiClientBuilder WithTitleId(string titleId)
        {
            _options.TitleId = titleId;
            return this;
        }

        /// <summary>
        /// Sets the environment.
        /// </summary>
        public GameApiClientBuilder WithEnvironment(string environment)
        {
            _options.Environment = environment;
            return this;
        }

        /// <summary>
        /// Uses a custom clock implementation.
        /// </summary>
        public GameApiClientBuilder WithClock(IClock clock)
        {
            _clock = clock;
            return this;
        }

        /// <summary>
        /// Uses a custom random implementation.
        /// </summary>
        public GameApiClientBuilder WithRandom(IRandom random)
        {
            _random = random;
            return this;
        }

        /// <summary>
        /// Uses a custom logger implementation.
        /// </summary>
        public GameApiClientBuilder WithLogger(IBackendLogger logger)
        {
            _logger = logger;
            return this;
        }

        /// <summary>
        /// Uses a custom connectivity checker implementation.
        /// </summary>
        public GameApiClientBuilder WithConnectivity(IConnectivity connectivity)
        {
            _connectivity = connectivity;
            return this;
        }

        /// <summary>
        /// Uses a custom storage implementation.
        /// </summary>
        public GameApiClientBuilder WithStorage(IStorage storage)
        {
            _storage = storage;
            return this;
        }

        /// <summary>
        /// Uses a custom serializer implementation.
        /// </summary>
        public GameApiClientBuilder WithSerializer(ISerializer serializer)
        {
            _serializer = serializer;
            return this;
        }

        /// <summary>
        /// Uses a custom telemetry sink.
        /// </summary>
        public GameApiClientBuilder WithTelemetry(IBackendTelemetrySink telemetry)
        {
            _telemetry = telemetry;
            return this;
        }

        /// <summary>
        /// Uses a custom cloud function invoker.
        /// Required - must be provided (e.g., PlayFabCloudFunctionInvoker).
        /// </summary>
        public GameApiClientBuilder WithInvoker(ICloudFunctionInvoker invoker)
        {
            _invoker = invoker;
            return this;
        }

        /// <summary>
        /// Builds and initializes the GameApiClient.
        /// </summary>
        public async Task<IGameApiClient> BuildAsync(CancellationToken ct = default)
        {
            // Resolve dependencies with defaults
            var clock = _clock ?? SystemClock.Instance;
            var random = _random ?? new SystemRandom();
            var logger = _logger ?? new UnityBackendLogger();
            var connectivity = _connectivity ?? new UnityConnectivity();
            var storage = _storage ?? new FileStorage();
            var serializer = _serializer ?? new UnityJsonSerializer();
            var telemetry = _telemetry ?? new LoggingTelemetrySink(logger);

            // Validate required options
            if (string.IsNullOrEmpty(_options.TitleId))
            {
                throw new InvalidOperationException("TitleId must be configured. Call WithTitleId() before building.");
            }

            if (_invoker == null)
            {
                throw new InvalidOperationException("Invoker must be configured. Call WithInvoker() before building.");
            }

            // Create resilience components
            var retryPolicy = new RetryPolicy(_options.Retry, clock, random, logger, telemetry);
            var circuitBreaker = new CircuitBreaker(_options.CircuitBreaker, clock, logger);
            var concurrencyLimiter = new ConcurrencyLimiter(_options.Concurrency, logger);

            var resilience = new ResiliencePipeline(
                retryPolicy,
                circuitBreaker,
                concurrencyLimiter,
                _options,
                logger,
                clock,
                telemetry);

            // Create outbox
            var outbox = new PersistentOutbox(
                _options.Outbox,
                _options.Retry,
                storage,
                serializer,
                clock,
                random,
                logger,
                telemetry);

            // Load outbox state
            if (_options.Outbox.Enabled)
            {
                await outbox.LoadAsync(ct);
            }

            ct.ThrowIfCancellationRequested();

            // Create flush worker
            var flushWorker = new OutboxFlushWorker(
                outbox,
                _invoker,
                connectivity,
                _options.Outbox,
                logger,
                clock,
                serializer);

            // Start flush worker if enabled
            if (_options.Outbox.Enabled && _options.Outbox.AutoStartFlushWorker)
            {
                flushWorker.Start();
            }

            // Create coalescer
            var coalescer = new RequestCoalescer(logger, _options.DefaultTimeout);

            // Build client
            var client = new GameApiClient(
                _invoker,
                resilience,
                outbox,
                flushWorker,
                coalescer,
                _options,
                logger,
                clock);

            logger.Info("GameApiClient initialized for title: {0}, environment: {1}",
                _options.TitleId, _options.Environment);

            return client;
        }

        /// <summary>
        /// Creates a new builder instance.
        /// </summary>
        public static GameApiClientBuilder Create() => new();
    }
}
