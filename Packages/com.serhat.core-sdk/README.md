# Serhat Core SDK

Shared, dependency-free runtime primitives used by the embedded Serhat Forge packages. This preview package intentionally stays small so higher-level packages can share infrastructure without depending on game-specific code.

## Included API

| API | Purpose |
|---|---|
| `MainThreadDispatcher` | Queues a callback for execution from a Unity `Update` on the main thread |

The package is embedded at `Packages/com.serhat.core-sdk`. Keep it installed while another `com.serhat.*` package references `Serhat.Core.Runtime`.

## Main-thread dispatch

Create the dispatcher once from the Unity main thread during startup, before any worker can enqueue work:

```csharp
using Serhat.Core.Utilities;
using UnityEngine;

public sealed class RuntimeBootstrap : MonoBehaviour
{
    private void Awake()
    {
        _ = MainThreadDispatcher.Instance;
    }
}
```

Code running on a worker may then queue the smallest possible Unity-facing callback:

```csharp
using System.Threading.Tasks;
using Serhat.Core.Utilities;
using UnityEngine;

public static class ExampleLoader
{
    public static Task LoadAsync()
    {
        return Task.Run(() =>
        {
            var result = PerformCpuOnlyWork();
            MainThreadDispatcher.Enqueue(() => Debug.Log(result));
        });
    }

    private static string PerformCpuOnlyWork() => "Ready";
}
```

## Lifecycle rules

- Never create `MainThreadDispatcher.Instance` for the first time from a worker thread; constructing Unity objects is main-thread-only.
- Do not call Unity APIs inside the background portion of a task.
- Keep queued callbacks short. Long work in a callback blocks the frame.
- Stop background producers during shutdown; callbacks queued after application quit are not guaranteed to run.
- Let the dispatcher manage its own `DontDestroyOnLoad` object. Do not add multiple dispatcher components to scenes.

## Requirements

- Unity 6000.3 or newer
- No third-party runtime dependencies

Project-level setup and validation are documented in the repository root `README.md`.
