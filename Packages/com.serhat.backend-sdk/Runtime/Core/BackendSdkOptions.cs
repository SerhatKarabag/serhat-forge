#nullable enable
using System;

namespace Serhat.Backend.Core
{
    /// <summary>
    /// Configuration options for the Backend SDK.
    /// </summary>
    public sealed class BackendSdkOptions
    {
        /// <summary>
        /// Title/App identifier for the backend service.
        /// </summary>
        public string TitleId { get; set; } = string.Empty;

        /// <summary>
        /// Environment identifier (e.g., "production", "staging", "development").
        /// </summary>
        public string Environment { get; set; } = "production";

        /// <summary>
        /// Retry policy options.
        /// </summary>
        public RetryOptions Retry { get; set; } = new();

        /// <summary>
        /// Circuit breaker options.
        /// </summary>
        public CircuitBreakerOptions CircuitBreaker { get; set; } = new();

        /// <summary>
        /// Outbox options.
        /// </summary>
        public OutboxOptions Outbox { get; set; } = new();

        /// <summary>
        /// Concurrency options.
        /// </summary>
        public ConcurrencyOptions Concurrency { get; set; } = new();

        /// <summary>
        /// Default timeout for operations.
        /// </summary>
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Whether to enable detailed logging.
        /// </summary>
        public bool EnableDetailedLogging { get; set; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            = true;
#else
            = false;
#endif
    }

    /// <summary>
    /// Retry policy configuration.
    /// </summary>
    public sealed class RetryOptions
    {
        /// <summary>
        /// Maximum number of retry attempts.
        /// </summary>
        public int MaxAttempts { get; set; } = 3;

        /// <summary>
        /// Initial delay before first retry.
        /// </summary>
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Maximum delay between retries.
        /// </summary>
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Multiplier for exponential backoff.
        /// </summary>
        public double BackoffMultiplier { get; set; } = 2.0;

        /// <summary>
        /// Jitter factor (0.0 to 1.0) to add randomness to delays.
        /// </summary>
        public double JitterFactor { get; set; } = 0.2;
    }

    /// <summary>
    /// Circuit breaker configuration.
    /// </summary>
    public sealed class CircuitBreakerOptions
    {
        /// <summary>
        /// Whether circuit breaker is enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Number of consecutive failures to open the circuit.
        /// </summary>
        public int FailureThreshold { get; set; } = 5;

        /// <summary>
        /// Duration to keep circuit open before trying half-open.
        /// </summary>
        public TimeSpan OpenDuration { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Number of successful calls in half-open state to close circuit.
        /// </summary>
        public int SuccessThresholdInHalfOpen { get; set; } = 2;
    }

    /// <summary>
    /// Outbox configuration.
    /// </summary>
    public sealed class OutboxOptions
    {
        /// <summary>
        /// Whether outbox is enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Maximum number of commands in the outbox.
        /// </summary>
        public int MaxQueueSize { get; set; } = 100;

        /// <summary>
        /// Interval for background flush worker.
        /// </summary>
        public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Maximum attempts for a single command before moving to dead letter.
        /// </summary>
        public int MaxCommandAttempts { get; set; } = 5;

        /// <summary>
        /// Whether to start flush worker automatically.
        /// </summary>
        public bool AutoStartFlushWorker { get; set; } = true;

        /// <summary>
        /// Time to keep dead letters before automatic cleanup.
        /// </summary>
        public TimeSpan DeadLetterRetentionPeriod { get; set; } = TimeSpan.FromDays(7);
    }

    /// <summary>
    /// Concurrency configuration.
    /// </summary>
    public sealed class ConcurrencyOptions
    {
        /// <summary>
        /// Maximum concurrent read operations.
        /// </summary>
        public int MaxConcurrentReads { get; set; } = 10;

        /// <summary>
        /// Maximum concurrent write operations.
        /// </summary>
        public int MaxConcurrentWrites { get; set; } = 5;
    }
}
