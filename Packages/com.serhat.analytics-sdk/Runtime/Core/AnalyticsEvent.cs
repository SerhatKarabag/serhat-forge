#nullable enable
using System;
using System.Collections.Generic;

namespace Serhat.Analytics.Core
{
    /// <summary>
    /// Represents a single analytics event.
    /// </summary>
    [Serializable]
    public sealed class AnalyticsEvent
    {
        /// <summary>
        /// Unique identifier for this event instance.
        /// </summary>
        public string EventId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Name of the event (e.g., "level_completed", "purchase_initiated").
        /// </summary>
        public string EventName { get; set; } = string.Empty;

        /// <summary>
        /// Category of the event (e.g., "gameplay", "purchase", "session").
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Event parameters/properties.
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; } = new();

        /// <summary>
        /// UTC timestamp when the event was created.
        /// </summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Unix timestamp in milliseconds.
        /// </summary>
        public long TimestampMs { get; set; }

        /// <summary>
        /// User ID associated with this event.
        /// </summary>
        public string? UserId { get; set; }

        /// <summary>
        /// Session ID associated with this event.
        /// </summary>
        public string? SessionId { get; set; }

        /// <summary>
        /// Sequence number for ordering events.
        /// </summary>
        public int SequenceNumber { get; set; }

        /// <summary>
        /// Number of retry attempts for this event.
        /// </summary>
        public int RetryCount { get; set; }

        /// <summary>
        /// Creates a new analytics event.
        /// </summary>
        public AnalyticsEvent()
        {
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// Creates a new analytics event with the specified name.
        /// </summary>
        public AnalyticsEvent(string eventName) : this()
        {
            EventName = eventName;
        }

        /// <summary>
        /// Creates a new analytics event with the specified name and parameters.
        /// </summary>
        public AnalyticsEvent(string eventName, Dictionary<string, object>? parameters) : this(eventName)
        {
            if (parameters != null)
            {
                Parameters = new Dictionary<string, object>(parameters);
            }
        }

        /// <summary>
        /// Adds a parameter to the event. Fluent API.
        /// </summary>
        public AnalyticsEvent WithParameter(string key, object value)
        {
            Parameters[key] = value;
            return this;
        }

        /// <summary>
        /// Sets the category. Fluent API.
        /// </summary>
        public AnalyticsEvent WithCategory(string category)
        {
            Category = category;
            return this;
        }

        /// <summary>
        /// Sets the user ID. Fluent API.
        /// </summary>
        public AnalyticsEvent WithUserId(string? userId)
        {
            UserId = userId;
            return this;
        }

        /// <summary>
        /// Sets the session ID. Fluent API.
        /// </summary>
        public AnalyticsEvent WithSessionId(string? sessionId)
        {
            SessionId = sessionId;
            return this;
        }

        /// <summary>
        /// Creates a copy of this event.
        /// </summary>
        public AnalyticsEvent Clone()
        {
            return new AnalyticsEvent
            {
                EventId = EventId,
                EventName = EventName,
                Category = Category,
                Parameters = new Dictionary<string, object>(Parameters),
                TimestampUtc = TimestampUtc,
                TimestampMs = TimestampMs,
                UserId = UserId,
                SessionId = SessionId,
                SequenceNumber = SequenceNumber,
                RetryCount = RetryCount
            };
        }

        public override string ToString()
        {
            return $"[{Category}] {EventName} ({Parameters.Count} params)";
        }
    }

    /// <summary>
    /// Event category constants.
    /// </summary>
    public static class EventCategory
    {
        public const string Gameplay = "gameplay";
        public const string Progression = "progression";
        public const string Session = "session";
        public const string Authentication = "authentication";
        public const string Purchase = "purchase";
        public const string Technical = "technical";
        public const string Custom = "custom";
    }
}
