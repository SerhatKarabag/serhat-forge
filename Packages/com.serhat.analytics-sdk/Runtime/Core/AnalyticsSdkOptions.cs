#nullable enable
using System;

namespace Serhat.Analytics.Core
{
    /// <summary>
    /// Configuration options for the Analytics SDK.
    /// </summary>
    public sealed class AnalyticsSdkOptions
    {
        /// <summary>
        /// Application identifier.
        /// </summary>
        public string AppId { get; set; } = string.Empty;

        /// <summary>
        /// Environment identifier (e.g., "production", "staging", "development").
        /// </summary>
        public string Environment { get; set; } = "production";

        /// <summary>
        /// Analytics mode based on build type.
        /// </summary>
        public AnalyticsMode Mode { get; set; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            = AnalyticsMode.DebugAndRemote;
#else
            = AnalyticsMode.RemoteOnly;
#endif

        /// <summary>
        /// Event batching configuration.
        /// </summary>
        public BatchingOptions Batching { get; set; } = new();

        /// <summary>
        /// Offline queue configuration.
        /// </summary>
        public OfflineQueueOptions OfflineQueue { get; set; } = new();

        /// <summary>
        /// Validation options.
        /// </summary>
        public ValidationOptions Validation { get; set; } = new();

        /// <summary>
        /// Session tracking options.
        /// </summary>
        public SessionOptions Session { get; set; } = new();

        /// <summary>
        /// Enable detailed logging.
        /// </summary>
        public bool EnableDetailedLogging { get; set; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            = true;
#else
            = false;
#endif
    }

    /// <summary>
    /// Event batching configuration.
    /// </summary>
    public sealed class BatchingOptions
    {
        /// <summary>
        /// Maximum number of events in a single batch.
        /// </summary>
        public int MaxBatchSize { get; set; } = 25;

        /// <summary>
        /// Interval for automatic batch flush.
        /// </summary>
        public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Whether to automatically flush batches on interval.
        /// </summary>
        public bool AutoFlush { get; set; } = true;
    }

    /// <summary>
    /// Offline queue configuration.
    /// </summary>
    public sealed class OfflineQueueOptions
    {
        /// <summary>
        /// Whether offline queue is enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Maximum number of events in the offline queue.
        /// </summary>
        public int MaxQueueSize { get; set; } = 1000;

        /// <summary>
        /// How long to keep events in offline queue before discarding.
        /// </summary>
        public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);

        /// <summary>
        /// Maximum retry attempts for failed events.
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 5;
    }

    /// <summary>
    /// Event validation configuration.
    /// </summary>
    public sealed class ValidationOptions
    {
        /// <summary>
        /// Whether validation is enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Maximum length for event names.
        /// </summary>
        public int MaxEventNameLength { get; set; } = 40;

        /// <summary>
        /// Maximum number of parameters per event.
        /// </summary>
        public int MaxParameterCount { get; set; } = 25;

        /// <summary>
        /// Maximum length for parameter keys.
        /// </summary>
        public int MaxParameterKeyLength { get; set; } = 40;

        /// <summary>
        /// Maximum length for string parameter values.
        /// </summary>
        public int MaxParameterValueLength { get; set; } = 100;

        /// <summary>
        /// Whether to throw on validation errors (false = log warning and skip).
        /// </summary>
        public bool StrictMode { get; set; } = false;
    }

    /// <summary>
    /// Session tracking configuration.
    /// </summary>
    public sealed class SessionOptions
    {
        /// <summary>
        /// Whether to automatically track session start/end.
        /// </summary>
        public bool AutoTrackSession { get; set; } = true;

        /// <summary>
        /// Session timeout duration. If app is inactive longer than this, a new session starts.
        /// </summary>
        public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Whether to track app lifecycle events (pause/resume).
        /// </summary>
        public bool TrackAppLifecycle { get; set; } = true;
    }
}
