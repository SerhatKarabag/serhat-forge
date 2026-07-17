#nullable enable
using System.Collections.Generic;
using Serhat.Analytics.Core;

namespace Serhat.Analytics.Events
{
    /// <summary>
    /// Technical/infrastructure analytics events (API calls, errors, performance).
    /// </summary>
    public static class TechnicalEvents
    {
        public const string Category = EventCategory.Technical;

        /// <summary>
        /// Track API call.
        /// </summary>
        public static AnalyticsEvent ApiCall(
            string operationName,
            long durationMs,
            bool success,
            string? errorCode = null,
            int? statusCode = null)
        {
            var evt = new AnalyticsEvent
            {
                EventName = "api_call",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["operation"] = operationName,
                    ["duration_ms"] = durationMs,
                    ["success"] = success
                }
            };

            if (!success && errorCode != null)
            {
                evt.Parameters["error_code"] = errorCode;
            }

            if (statusCode.HasValue)
            {
                evt.Parameters["status_code"] = statusCode.Value;
            }

            return evt;
        }

        /// <summary>
        /// Track generic error.
        /// </summary>
        public static AnalyticsEvent Error(
            string errorType,
            string errorMessage,
            string? source = null,
            bool? isFatal = null)
        {
            var evt = new AnalyticsEvent
            {
                EventName = "error",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["error_type"] = errorType,
                    ["error_message"] = errorMessage.Length > 100 ? errorMessage.Substring(0, 100) : errorMessage
                }
            };

            if (source != null)
            {
                evt.Parameters["source"] = source;
            }

            if (isFatal.HasValue)
            {
                evt.Parameters["is_fatal"] = isFatal.Value;
            }

            return evt;
        }

        /// <summary>
        /// Track offline operation (queue).
        /// </summary>
        public static AnalyticsEvent OfflineOperation(
            string operationType,
            int queueSize,
            bool flushed = false)
        {
            return new AnalyticsEvent
            {
                EventName = "offline_operation",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["operation_type"] = operationType,
                    ["queue_size"] = queueSize,
                    ["flushed"] = flushed
                }
            };
        }

        /// <summary>
        /// Track circuit breaker state change.
        /// </summary>
        public static AnalyticsEvent CircuitBreakerStateChange(
            string previousState,
            string newState,
            int consecutiveFailures = 0)
        {
            return new AnalyticsEvent
            {
                EventName = "circuit_breaker_state_change",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["previous_state"] = previousState,
                    ["new_state"] = newState,
                    ["consecutive_failures"] = consecutiveFailures
                }
            };
        }

        /// <summary>
        /// Track API retry.
        /// </summary>
        public static AnalyticsEvent ApiRetry(
            string operationName,
            int attemptNumber,
            long delayMs,
            string? errorCode = null)
        {
            var evt = new AnalyticsEvent
            {
                EventName = "api_retry",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["operation"] = operationName,
                    ["attempt"] = attemptNumber,
                    ["delay_ms"] = delayMs
                }
            };

            if (errorCode != null)
            {
                evt.Parameters["error_code"] = errorCode;
            }

            return evt;
        }

        /// <summary>
        /// Track dead letter (permanently failed operation).
        /// </summary>
        public static AnalyticsEvent DeadLetter(
            string functionName,
            string reason,
            int attemptCount)
        {
            return new AnalyticsEvent
            {
                EventName = "dead_letter",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["function_name"] = functionName,
                    ["reason"] = reason,
                    ["attempt_count"] = attemptCount
                }
            };
        }

        /// <summary>
        /// Track connectivity change.
        /// </summary>
        public static AnalyticsEvent ConnectivityChanged(bool isOnline, float offlineDurationSeconds = 0)
        {
            return new AnalyticsEvent
            {
                EventName = "connectivity_changed",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["is_online"] = isOnline,
                    ["offline_duration_seconds"] = offlineDurationSeconds
                }
            };
        }

        /// <summary>
        /// Track app performance metric.
        /// </summary>
        public static AnalyticsEvent PerformanceMetric(
            string metricName,
            double value,
            string? unit = null)
        {
            var evt = new AnalyticsEvent
            {
                EventName = "performance_metric",
                Category = Category,
                Parameters = new Dictionary<string, object>
                {
                    ["metric_name"] = metricName,
                    ["value"] = value
                }
            };

            if (unit != null)
            {
                evt.Parameters["unit"] = unit;
            }

            return evt;
        }
    }
}
