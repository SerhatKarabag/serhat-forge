#nullable enable
using System.Collections.Generic;
using Serhat.Analytics.Core;

namespace Serhat.Analytics.Events
{
    /// <summary>
    /// Authentication-related analytics events.
    /// </summary>
    public static class AuthEvents
    {
        public const string Category = EventCategory.Authentication;

        /// <summary>
        /// Track login event.
        /// </summary>
        public static AnalyticsEvent Login(string provider, bool isNewUser = false, string? playFabId = null)
        {
            var evt = new AnalyticsEvent
            {
                EventName = "login",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["provider"] = provider,
                    ["is_new_user"] = isNewUser,
                    ["method"] = provider == "anonymous" ? "anonymous" : "social"
                }
            };

            if (playFabId != null)
            {
                evt.UserId = playFabId;
            }

            return evt;
        }

        /// <summary>
        /// Track logout event.
        /// </summary>
        public static AnalyticsEvent Logout(string reason = "user_initiated")
        {
            return new AnalyticsEvent
            {
                EventName = "logout",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["reason"] = reason
                }
            };
        }

        /// <summary>
        /// Track sign up event (first time user).
        /// </summary>
        public static AnalyticsEvent SignUp(string provider)
        {
            return new AnalyticsEvent
            {
                EventName = "sign_up",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["provider"] = provider,
                    ["method"] = provider == "anonymous" ? "anonymous" : "social"
                }
            };
        }

        /// <summary>
        /// Track provider link event (e.g., linking Google to anonymous account).
        /// </summary>
        public static AnalyticsEvent ProviderLinked(string provider, bool success, string? errorCode = null)
        {
            var evt = new AnalyticsEvent
            {
                EventName = "provider_linked",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["provider"] = provider,
                    ["success"] = success
                }
            };

            if (!success && errorCode != null)
            {
                evt.Parameters["error_code"] = errorCode;
            }

            return evt;
        }

        /// <summary>
        /// Track authentication state change.
        /// </summary>
        public static AnalyticsEvent AuthStateChanged(string oldState, string newState)
        {
            return new AnalyticsEvent
            {
                EventName = "auth_state_changed",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["old_state"] = oldState,
                    ["new_state"] = newState
                }
            };
        }

        /// <summary>
        /// Track authentication error.
        /// </summary>
        public static AnalyticsEvent AuthError(string errorCode, string errorMessage, string? provider = null)
        {
            var evt = new AnalyticsEvent
            {
                EventName = "auth_error",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["error_code"] = errorCode,
                    ["error_message"] = errorMessage.Length > 100 ? errorMessage.Substring(0, 100) : errorMessage
                }
            };

            if (provider != null)
            {
                evt.Parameters["provider"] = provider;
            }

            return evt;
        }

        /// <summary>
        /// Track session refresh event.
        /// </summary>
        public static AnalyticsEvent SessionRefreshed(bool success, float durationMs = 0)
        {
            return new AnalyticsEvent
            {
                EventName = "session_refreshed",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["success"] = success,
                    ["duration_ms"] = durationMs
                }
            };
        }
    }
}
