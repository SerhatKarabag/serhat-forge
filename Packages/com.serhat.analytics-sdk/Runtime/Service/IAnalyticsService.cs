#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serhat.Analytics.Core;

namespace Serhat.Analytics
{
    /// <summary>
    /// Main analytics service interface.
    /// Handles event tracking, batching, offline queue, and multi-provider dispatch.
    /// </summary>
    public interface IAnalyticsService : IDisposable
    {
        /// <summary>
        /// Whether the service is initialized and ready to track events.
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Current analytics mode.
        /// </summary>
        AnalyticsMode Mode { get; }

        /// <summary>
        /// Whether tracking is enabled.
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// Current session ID.
        /// </summary>
        string? SessionId { get; }

        /// <summary>
        /// Current user ID.
        /// </summary>
        string? UserId { get; }

        #region Event Tracking

        /// <summary>
        /// Tracks an analytics event.
        /// </summary>
        void Track(AnalyticsEvent evt);

        /// <summary>
        /// Tracks an event with the specified name and optional parameters.
        /// </summary>
        void Track(string eventName, Dictionary<string, object>? parameters = null);

        #endregion

        #region User Management

        /// <summary>
        /// Sets the user ID for all subsequent events.
        /// </summary>
        void SetUserId(string userId);

        /// <summary>
        /// Clears the user ID (for logout scenarios).
        /// </summary>
        void ClearUserId();

        /// <summary>
        /// Sets a single user property.
        /// </summary>
        void SetUserProperty(string name, object value);

        /// <summary>
        /// Sets multiple user properties.
        /// </summary>
        void SetUserProperties(Dictionary<string, object> properties);

        #endregion

        #region Session Management

        /// <summary>
        /// Starts a new session.
        /// </summary>
        void StartSession();

        /// <summary>
        /// Ends the current session.
        /// </summary>
        void EndSession();

        #endregion

        #region Control

        /// <summary>
        /// Enables or disables tracking.
        /// </summary>
        void SetEnabled(bool enabled);

        /// <summary>
        /// Sets the analytics mode.
        /// </summary>
        void SetMode(AnalyticsMode mode);

        /// <summary>
        /// Forces an immediate flush of all pending events.
        /// </summary>
        Task FlushAsync();

        /// <summary>
        /// Gets the current queue status.
        /// </summary>
        EventQueueStatus GetQueueStatus();

        #endregion

        #region Events

        /// <summary>
        /// Event raised when an event is tracked (for debug purposes).
        /// </summary>
        event Action<AnalyticsEvent>? OnEventTracked;

        /// <summary>
        /// Event raised when events are flushed to providers.
        /// </summary>
        event Action<int>? OnEventsFlushed;

        /// <summary>
        /// Event raised when flush fails.
        /// </summary>
        event Action<Exception>? OnFlushFailed;

        #endregion
    }
}
