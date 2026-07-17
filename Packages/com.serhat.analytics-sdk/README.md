# Serhat Analytics SDK

Provider-agnostic analytics for Unity with validation, serialized dispatch, batching, user/session context, and a persistent offline outbox.

The package is embedded at `Packages/com.serhat.analytics-sdk` and is safe to run in debug-only mode without a remote analytics vendor.

## Capabilities

- Typed event factories for authentication, gameplay, progression, purchases, sessions, and technical events
- Custom `AnalyticsEvent` support
- Event-name and parameter validation with configurable limits
- Batched remote dispatch and explicit flush
- Persistent offline queue with retention and retry limits
- User properties and session tracking
- Pluggable providers through `IAnalyticsProvider`
- Optional Firebase Analytics adapter

## Modes

| Mode | Console | Remote provider | Behavior without a provider |
|---|---:|---:|---|
| `Disabled` | No | No | Tracking is ignored |
| `DebugOnly` | Yes | No | Valid standalone mode |
| `DebugAndRemote` | Yes | Yes | Falls back to `DebugOnly` with a warning |
| `RemoteOnly` | No | Yes | `BuildAsync` throws; this prevents silent telemetry loss |

## Quick start

Build the service once in your composition root, retain it for the application lifetime, and dispose it during teardown:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Analytics;
using Serhat.Analytics.Core;
using Serhat.Analytics.Events;
using UnityEngine;

public sealed class AnalyticsBootstrap : MonoBehaviour
{
    private CancellationTokenSource? _lifetime;
    private IAnalyticsService? _analytics;

    private async void Awake()
    {
        _lifetime = new CancellationTokenSource();

        try
        {
            _analytics = await AnalyticsServiceBuilder.Create()
                .WithAppId(Application.identifier)
                .WithEnvironment(Debug.isDebugBuild ? "development" : "production")
                .WithMode(AnalyticsMode.DebugOnly)
                .WithOptions(options =>
                {
                    options.Batching.MaxBatchSize = 25;
                    options.OfflineQueue.Enabled = true;
                    options.Validation.StrictMode = Debug.isDebugBuild;
                })
                .BuildAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Normal teardown during startup.
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    public void TrackLevelStarted(int levelId)
    {
        _analytics?.Track(GameplayEvents.LevelStart(levelId));
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            _ = FlushSafelyAsync();
        }
    }

    private async Task FlushSafelyAsync()
    {
        try
        {
            if (_analytics != null)
            {
                await _analytics.FlushAsync();
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Analytics flush failed: {exception.Message}");
        }
    }

    private void OnDestroy()
    {
        _lifetime?.Cancel();
        _analytics?.Dispose();
        _lifetime?.Dispose();
    }
}
```

Use a custom event when no typed factory fits:

```csharp
var itemUsed = new AnalyticsEvent("booster_used")
    .WithCategory(EventCategory.Gameplay)
    .WithParameter("booster_id", boosterId)
    .WithParameter("level_id", levelId);

analytics.Track(itemUsed);
```

## Remote providers

For Firebase:

1. Install and configure the Firebase Unity SDK, including the platform configuration files.
2. Ensure the `Firebase.Analytics` assembly is available. A UPM installation can enable the adapter through the package's `FIREBASE_ANALYTICS_AVAILABLE` version define. For a `.unitypackage` installation, add that symbol manually only after the Firebase assembly resolves.
3. Add the provider before building:

```csharp
var analytics = await AnalyticsServiceBuilder.Create()
    .WithAppId(Application.identifier)
    .WithMode(AnalyticsMode.DebugAndRemote)
    .AddFirebase()
    .BuildAsync(cancellationToken);
```

For another vendor, implement `IAnalyticsProvider` and register it with `AddProvider`. Provider initialization failures are logged; verify provider readiness and event delivery in staging before release.

## Identity, consent, and privacy

- Call `SetUserId` only with a stable, non-sensitive application identifier; never send email, receipt payloads, access tokens, or secrets.
- Call `ClearUserId` on logout.
- Gate collection behind your consent policy with `SetEnabled(false)` or build in `Disabled` mode until consent exists.
- Treat event parameters as exported production data. Maintain an event schema and avoid free-form personal data.
- Flush on application pause and before an orderly logout when practical. Offline delivery is resilient, not a guarantee against process termination.

## Requirements

- Unity 6000.3 or newer
- A remote provider is optional unless using `DebugAndRemote` or `RemoteOnly`

Project-level setup and validation are documented in the repository root `README.md`.
