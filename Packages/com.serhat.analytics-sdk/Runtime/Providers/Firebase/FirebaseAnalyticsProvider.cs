#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Analytics.Core;
using UnityEngine.Scripting;

namespace Serhat.Analytics.Providers.Firebase
{
    /// <summary>
    /// Firebase Analytics provider implementation.
    /// </summary>
    [Preserve]
    public sealed class FirebaseAnalyticsProvider : AnalyticsProviderBase
    {
        public override string ProviderId => "firebase";

        [Preserve]
        public FirebaseAnalyticsProvider(IAnalyticsLogger? logger = null) : base(logger)
        {
        }

        public override async Task InitializeAsync(CancellationToken ct = default)
        {
            try
            {
                var dependencyStatus = await global::Firebase.FirebaseApp.CheckAndFixDependenciesAsync();
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                if (dependencyStatus != global::Firebase.DependencyStatus.Available)
                {
                    Logger?.Warning("Firebase dependencies unavailable: {0}", dependencyStatus);
                    IsInitialized = false;
                    return;
                }

                var app = global::Firebase.FirebaseApp.DefaultInstance;
                IsInitialized = app != null;

                if (!IsInitialized)
                {
                    Logger?.Warning("Firebase not initialized. Ensure google-services.json/GoogleService-Info.plist is configured.");
                }
                else
                {
                    global::Firebase.Analytics.FirebaseAnalytics.SetAnalyticsCollectionEnabled(true);
                    Logger?.Info("Firebase Analytics provider initialized");
                }
            }
            catch (Exception ex)
            {
                Logger?.Error("Firebase initialization check failed", ex);
                IsInitialized = false;
            }
        }

        public override Task LogEventAsync(AnalyticsEvent evt, CancellationToken ct = default)
        {
            if (!IsInitialized || evt == null)
            {
                return Task.CompletedTask;
            }

            try
            {
                var eventName = FirebaseEventMapper.MapEventName(evt.EventName);
                var parameters = FirebaseEventMapper.MapParameters(evt.Parameters);

                if (parameters.Length == 0)
                {
                    global::Firebase.Analytics.FirebaseAnalytics.LogEvent(eventName);
                }
                else
                {
                    global::Firebase.Analytics.FirebaseAnalytics.LogEvent(eventName, parameters);
                }

                Logger?.Debug("Firebase event logged: {0}", evt.EventName);
            }
            catch (Exception ex)
            {
                Logger?.Error("Failed to log Firebase event: {0}", ex, evt.EventName);
            }
            return Task.CompletedTask;
        }

        public override Task SetUserPropertyAsync(string name, object value, CancellationToken ct = default)
        {
            if (!IsInitialized) return Task.CompletedTask;

            try
            {
                var stringValue = value?.ToString();
                global::Firebase.Analytics.FirebaseAnalytics.SetUserProperty(name, stringValue);
                Logger?.Debug("Firebase user property set: {0}={1}", name, stringValue ?? "null");
            }
            catch (Exception ex)
            {
                Logger?.Error("Failed to set Firebase user property: {0}", ex, name);
            }
            return Task.CompletedTask;
        }

        public override Task SetUserIdAsync(string userId, CancellationToken ct = default)
        {
            if (!IsInitialized) return Task.CompletedTask;

            try
            {
                global::Firebase.Analytics.FirebaseAnalytics.SetUserId(userId);
                Logger?.Debug("Firebase user ID set: {0}", userId ?? "null");
            }
            catch (Exception ex)
            {
                Logger?.Error("Failed to set Firebase user ID", ex);
            }
            return Task.CompletedTask;
        }

        public override Task FlushAsync(CancellationToken ct = default)
        {
            // Firebase handles its own batching and flushing
            return Task.CompletedTask;
        }
    }
}
