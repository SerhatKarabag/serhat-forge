#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Analytics.Core;

namespace Serhat.Analytics.Providers
{
    /// <summary>
    /// Provider-agnostic interface for analytics backends.
    /// Implementations: Firebase, Amplitude, Mixpanel, Custom, etc.
    /// </summary>
    public interface IAnalyticsProvider : IDisposable
    {
        /// <summary>
        /// Unique identifier for this provider (e.g., "firebase", "amplitude").
        /// </summary>
        string ProviderId { get; }

        /// <summary>
        /// Whether the provider has been initialized and is ready to receive events.
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Initializes the provider.
        /// </summary>
        Task InitializeAsync(CancellationToken ct = default);

        /// <summary>
        /// Logs a single analytics event.
        /// </summary>
        Task LogEventAsync(AnalyticsEvent evt, CancellationToken ct = default);

        /// <summary>
        /// Logs multiple analytics events (for batch sending).
        /// </summary>
        Task LogEventsAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken ct = default);

        /// <summary>
        /// Sets a user property.
        /// </summary>
        Task SetUserPropertyAsync(string name, object value, CancellationToken ct = default);

        /// <summary>
        /// Sets the user ID.
        /// </summary>
        Task SetUserIdAsync(string userId, CancellationToken ct = default);

        /// <summary>
        /// Forces the provider to flush any buffered events.
        /// </summary>
        Task FlushAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Base class for analytics providers with common functionality.
    /// </summary>
    public abstract class AnalyticsProviderBase : IAnalyticsProvider
    {
        protected readonly IAnalyticsLogger? Logger;

        public abstract string ProviderId { get; }
        public bool IsInitialized { get; protected set; }

        protected AnalyticsProviderBase(IAnalyticsLogger? logger = null)
        {
            Logger = logger;
        }

        public abstract Task InitializeAsync(CancellationToken ct = default);
        public abstract Task LogEventAsync(AnalyticsEvent evt, CancellationToken ct = default);
        public abstract Task SetUserPropertyAsync(string name, object value, CancellationToken ct = default);
        public abstract Task SetUserIdAsync(string userId, CancellationToken ct = default);

        public virtual async Task LogEventsAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken ct = default)
        {
            foreach (var evt in events)
            {
                await LogEventAsync(evt, ct);
            }
        }

        public virtual Task FlushAsync(CancellationToken ct = default)
        {
            // Most providers handle their own flushing
            return Task.CompletedTask;
        }

        public virtual void Dispose()
        {
            // Override in derived classes if cleanup is needed
        }
    }

    /// <summary>
    /// A no-op provider for testing or disabled scenarios.
    /// </summary>
    public sealed class NullAnalyticsProvider : IAnalyticsProvider
    {
        public static readonly NullAnalyticsProvider Instance = new();

        public string ProviderId => "null";
        public bool IsInitialized => true;

        private NullAnalyticsProvider() { }

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task LogEventAsync(AnalyticsEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task LogEventsAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetUserPropertyAsync(string name, object value, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetUserIdAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
