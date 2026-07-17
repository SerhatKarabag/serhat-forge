#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Serhat.Backend.Core;
using UnityEngine;

namespace Serhat.Forge.Updates
{
    /// <summary>
    /// Immutable client-side view of a mandatory update requirement returned by backend.
    /// </summary>
    [Serializable]
    public sealed class ForceUpdateRequirement
    {
        private const string CurrentVersionKey = "currentVersion";
        private const string MinimumSupportedVersionKey = "minSupportedVersion";
        private const string StoreUrlKey = "storeUrl";
        private const string TitleKey = "title";
        private const string MessageKey = "message";

        public ForceUpdateRequirement(
            string currentVersion,
            string minimumSupportedVersion,
            string storeUrl,
            string title,
            string message)
        {
            CurrentVersion = currentVersion ?? string.Empty;
            MinimumSupportedVersion = minimumSupportedVersion ?? string.Empty;
            StoreUrl = storeUrl ?? string.Empty;
            Title = string.IsNullOrWhiteSpace(title) ? "Update Required" : title;
            Message = string.IsNullOrWhiteSpace(message)
                ? $"A new version is required to continue. Please update the game to at least {MinimumSupportedVersion}."
                : message;
        }

        public string CurrentVersion { get; }
        public string MinimumSupportedVersion { get; }
        public string StoreUrl { get; }
        public string Title { get; }
        public string Message { get; }

        public string ResolveStoreUrl()
        {
            if (!string.IsNullOrWhiteSpace(StoreUrl))
            {
                return StoreUrl;
            }

#if UNITY_ANDROID
            var appId = Application.identifier;
            if (!string.IsNullOrWhiteSpace(appId))
            {
                return $"market://details?id={appId}";
            }
#endif
            return string.Empty;
        }

        public bool Matches(ForceUpdateRequirement? other)
        {
            if (other == null)
            {
                return false;
            }

            return string.Equals(CurrentVersion, other.CurrentVersion, StringComparison.Ordinal) &&
                   string.Equals(MinimumSupportedVersion, other.MinimumSupportedVersion, StringComparison.Ordinal) &&
                   string.Equals(StoreUrl, other.StoreUrl, StringComparison.Ordinal) &&
                   string.Equals(Title, other.Title, StringComparison.Ordinal) &&
                   string.Equals(Message, other.Message, StringComparison.Ordinal);
        }

        public static bool TryCreate(
            BackendError? error,
            [NotNullWhen(true)] out ForceUpdateRequirement? requirement)
        {
            requirement = null;
            if (error == null || !string.Equals(error.Code, ErrorCodes.VersionMismatch, StringComparison.Ordinal))
            {
                return false;
            }

            var details = error.Details;
            var currentVersion = GetValue(details, CurrentVersionKey, Application.version);
            var minimumSupportedVersion = GetValue(details, MinimumSupportedVersionKey, string.Empty);
            if (string.IsNullOrWhiteSpace(minimumSupportedVersion))
            {
                return false;
            }

            var storeUrl = GetValue(details, StoreUrlKey, string.Empty);
            var title = GetValue(details, TitleKey, "Update Required");
            var message = GetValue(details, MessageKey, error.Message);

            requirement = new ForceUpdateRequirement(
                currentVersion,
                minimumSupportedVersion,
                storeUrl,
                title,
                message);

            return true;
        }

        private static string GetValue(
            IReadOnlyDictionary<string, string>? details,
            string key,
            string fallback)
        {
            if (details != null && details.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return fallback;
        }
    }
}
