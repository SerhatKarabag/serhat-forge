#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Serhat.Analytics.Core.Validation
{
    /// <summary>
    /// Result of event validation.
    /// </summary>
    public sealed class ValidationResult
    {
        public bool IsValid { get; }
        public string? Error { get; }
        public List<string> Warnings { get; } = new();

        public static ValidationResult Success() => new(true, null);
        public static ValidationResult Failure(string error) => new(false, error);

        private ValidationResult(bool isValid, string? error)
        {
            IsValid = isValid;
            Error = error;
        }

        public ValidationResult WithWarning(string warning)
        {
            Warnings.Add(warning);
            return this;
        }
    }

    /// <summary>
    /// Validates analytics events according to provider constraints.
    /// </summary>
    public sealed class EventValidator
    {
        private readonly ValidationOptions _options;
        private readonly IAnalyticsLogger _logger;

        // Firebase reserved event name prefixes
        private static readonly string[] ReservedPrefixes = { "firebase_", "google_", "ga_" };

        // Valid event name pattern (alphanumeric + underscore, must start with letter)
        private static readonly Regex EventNamePattern = new(@"^[a-zA-Z][a-zA-Z0-9_]*$", RegexOptions.Compiled);

        // Valid parameter key pattern
        private static readonly Regex ParamKeyPattern = new(@"^[a-zA-Z][a-zA-Z0-9_]*$", RegexOptions.Compiled);

        public EventValidator(ValidationOptions options, IAnalyticsLogger logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Validates an analytics event.
        /// </summary>
        public ValidationResult Validate(AnalyticsEvent evt)
        {
            if (!_options.Enabled)
            {
                return ValidationResult.Success();
            }

            // Validate event name
            var nameResult = ValidateEventName(evt.EventName);
            if (!nameResult.IsValid)
            {
                return HandleValidationFailure(nameResult.Error!, evt);
            }

            // Validate parameters
            var paramResult = ValidateParameters(evt.Parameters);
            if (!paramResult.IsValid)
            {
                return HandleValidationFailure(paramResult.Error!, evt);
            }

            // Log warnings
            foreach (var warning in nameResult.Warnings.Concat(paramResult.Warnings))
            {
                _logger.Warning("Event validation warning for '{0}': {1}", evt.EventName, warning);
            }

            return ValidationResult.Success();
        }

        /// <summary>
        /// Validates and potentially sanitizes an event.
        /// Returns a sanitized copy if needed, or the original if valid.
        /// </summary>
        public (bool IsValid, AnalyticsEvent Event) ValidateAndSanitize(AnalyticsEvent evt)
        {
            var result = Validate(evt);
            if (!result.IsValid)
            {
                return (false, evt);
            }

            // Sanitize if needed
            var sanitized = SanitizeEvent(evt);
            return (true, sanitized);
        }

        private ValidationResult ValidateEventName(string? eventName)
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                return ValidationResult.Failure("Event name cannot be empty");
            }

            if (eventName.Length > _options.MaxEventNameLength)
            {
                return ValidationResult.Failure(
                    $"Event name '{eventName}' exceeds max length of {_options.MaxEventNameLength}");
            }

            if (!EventNamePattern.IsMatch(eventName))
            {
                return ValidationResult.Failure(
                    $"Event name '{eventName}' must start with a letter and contain only alphanumeric characters and underscores");
            }

            var result = ValidationResult.Success();

            // Check for reserved prefixes (warning only)
            foreach (var prefix in ReservedPrefixes)
            {
                if (eventName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    result.WithWarning($"Event name '{eventName}' uses reserved prefix '{prefix}'");
                }
            }

            return result;
        }

        private ValidationResult ValidateParameters(Dictionary<string, object>? parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return ValidationResult.Success();
            }

            if (parameters.Count > _options.MaxParameterCount)
            {
                return ValidationResult.Failure(
                    $"Event has {parameters.Count} parameters, max allowed is {_options.MaxParameterCount}");
            }

            var result = ValidationResult.Success();

            foreach (var kvp in parameters)
            {
                // Validate key
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    return ValidationResult.Failure("Parameter key cannot be empty");
                }

                if (kvp.Key.Length > _options.MaxParameterKeyLength)
                {
                    return ValidationResult.Failure(
                        $"Parameter key '{kvp.Key}' exceeds max length of {_options.MaxParameterKeyLength}");
                }

                if (!ParamKeyPattern.IsMatch(kvp.Key))
                {
                    return ValidationResult.Failure(
                        $"Parameter key '{kvp.Key}' must start with a letter and contain only alphanumeric characters and underscores");
                }

                // Validate value
                if (kvp.Value is string strValue && strValue.Length > _options.MaxParameterValueLength)
                {
                    result.WithWarning(
                        $"Parameter '{kvp.Key}' value will be truncated from {strValue.Length} to {_options.MaxParameterValueLength} characters");
                }
            }

            return result;
        }

        private AnalyticsEvent SanitizeEvent(AnalyticsEvent evt)
        {
            var needsSanitization = false;
            var sanitizedParams = new Dictionary<string, object>(evt.Parameters.Count);

            foreach (var kvp in evt.Parameters)
            {
                var value = kvp.Value;

                // Truncate long string values
                if (value is string strValue && strValue.Length > _options.MaxParameterValueLength)
                {
                    value = strValue.Substring(0, _options.MaxParameterValueLength);
                    needsSanitization = true;
                }

                sanitizedParams[kvp.Key] = value;
            }

            if (!needsSanitization)
            {
                return evt;
            }

            // Create sanitized copy
            var sanitized = evt.Clone();
            sanitized.Parameters = sanitizedParams;
            return sanitized;
        }

        private ValidationResult HandleValidationFailure(string error, AnalyticsEvent evt)
        {
            if (_options.StrictMode)
            {
                _logger.Error("Event validation failed for '{0}': {1}", null, evt.EventName, error);
            }
            else
            {
                _logger.Warning("Event validation failed for '{0}': {1} (event will be skipped)", evt.EventName, error);
            }

            return ValidationResult.Failure(error);
        }
    }

    internal static class EnumerableExtensions
    {
        public static IEnumerable<T> Concat<T>(this IEnumerable<T> first, IEnumerable<T> second)
        {
            foreach (var item in first) yield return item;
            foreach (var item in second) yield return item;
        }
    }
}
