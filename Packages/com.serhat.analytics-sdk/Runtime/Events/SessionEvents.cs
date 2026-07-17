#nullable enable
using System.Collections.Generic;
using Serhat.Analytics.Core;
using UnityEngine;

namespace Serhat.Analytics.Events
{
    /// <summary>
    /// Session-related analytics events.
    /// </summary>
    public static class SessionEvents
    {
        public const string Category = EventCategory.Session;

        /// <summary>
        /// Track session start.
        /// </summary>
        public static AnalyticsEvent SessionStart(string sessionId, bool isFirstSession = false)
        {
            return new AnalyticsEvent
            {
                EventName = "session_start",
                Category = Category,
                SessionId = sessionId,
                Parameters = new Dictionary<string, object>
                {
                    ["session_id"] = sessionId,
                    ["is_first_session"] = isFirstSession,
                    ["platform"] = Application.platform.ToString(),
                    ["app_version"] = Application.version,
                    ["os_version"] = SystemInfo.operatingSystem,
                    ["device_model"] = SystemInfo.deviceModel
                }
            };
        }

        /// <summary>
        /// Track session end.
        /// </summary>
        public static AnalyticsEvent SessionEnd(string sessionId, float durationSeconds, int eventsTracked = 0)
        {
            return new AnalyticsEvent
            {
                EventName = "session_end",
                Category = Category,
                SessionId = sessionId,
                Parameters = new Dictionary<string, object>
                {
                    ["session_id"] = sessionId,
                    ["duration_seconds"] = durationSeconds,
                    ["events_tracked"] = eventsTracked
                }
            };
        }

        /// <summary>
        /// Track app going to background.
        /// </summary>
        public static AnalyticsEvent AppBackgrounded(float sessionDurationSoFar)
        {
            return new AnalyticsEvent
            {
                EventName = "app_backgrounded",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["session_duration_so_far"] = sessionDurationSoFar
                }
            };
        }

        /// <summary>
        /// Track app resuming from background.
        /// </summary>
        public static AnalyticsEvent AppResumed(float backgroundDurationSeconds)
        {
            return new AnalyticsEvent
            {
                EventName = "app_resumed",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["background_duration_seconds"] = backgroundDurationSeconds
                }
            };
        }

        /// <summary>
        /// Track screen/page view.
        /// </summary>
        public static AnalyticsEvent ScreenView(string screenName, string? screenClass = null)
        {
            var evt = new AnalyticsEvent
            {
                EventName = "screen_view",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["screen_name"] = screenName
                }
            };

            if (screenClass != null)
            {
                evt.Parameters["screen_class"] = screenClass;
            }

            return evt;
        }

        /// <summary>
        /// Track daily login.
        /// </summary>
        public static AnalyticsEvent DailyLogin(int daysSinceLastLogin = 0, int totalLoginDays = 0)
        {
            return new AnalyticsEvent
            {
                EventName = "daily_login",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["days_since_last_login"] = daysSinceLastLogin,
                    ["total_login_days"] = totalLoginDays
                }
            };
        }
    }
}
