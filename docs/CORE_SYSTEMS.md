# Core systems guide

This guide explains the reusable runtime systems that ship with Serhat Forge. It focuses on ownership, lifecycle, and extension points. For installation, start with [Getting started](GETTING_STARTED.md). For optional SDKs, use [Integrations](INTEGRATIONS.md).

## System ownership at a glance

| System | Default owner | Lifetime | Required? |
|---|---|---|---|
| Zenject composition | `ProjectContext` + scene `SceneContext` | Application / scene | Yes |
| Boot orchestration | `GameBootstrapper` on `ProjectContext` | Application | Yes for the supplied flow |
| Addressables manager | `ForgeProjectInstaller` | Application | Yes |
| Prefab preload cache | `PrefabLoaderService` | Application | Optional to use |
| Startup operations | `StartupPipeline` | One boot attempt | Optional to extend |
| Save repository/coordinator | Your game installer | Application or profile | Opt-in |
| Feature gates | Your game installer | Application or player session | Opt-in |
| Analytics | Your configured analytics composition | Application | Opt-in |
| Audio | `ForgeProjectInstaller` | Application | Safe default included |
| Ads | `ForgeProjectInstaller` | Application | Disabled by default |
| Component pools | The feature that creates the pool | Feature/scene | Opt-in |

The template does not own game-domain state such as inventory, progression, levels, quests, or a store catalog. Compose those in your game layer.

## Zenject composition

`Assets/Resources/ProjectContext.prefab` is the application composition root. `ForgeProjectInstaller` installs the generic services that must survive scene changes:

- `ContentConfiguration` and content retry policy
- `StartupPipeline`
- `IContentManager`
- `IPrefabLoader`
- disabled/null ad behavior unless ads are configured
- default or null audio behavior
- a null loading-screen fallback

`ForgeBootstrapInstaller` lives on `ProjectContext` and binds `GameBootstrapper` as both its concrete type and `IGameBootstrapper`. The supplied sample scene has its own `SceneContext` for scene-scoped/demo composition.

Run **Tools > Serhat Forge > Setup > Repair Zenject Composition** after cloning, after deleting the composition prefab, or when the composition smoke test reports broken installer wiring. The repair command is idempotent.

### Add game-specific services

Do not turn `ForgeProjectInstaller` into a large game composition root. Add a game installer and attach it to `ProjectContext`, or use a scene installer for scene-scoped services.

```csharp
using Zenject;

public sealed class GameProjectInstaller : MonoInstaller
{
    public override void InstallBindings()
    {
        Container.BindInterfacesAndSelfTo<PlayerProfileService>()
            .AsSingle()
            .NonLazy();
    }
}
```

Use constructor injection for plain C# services and `[Inject]` for Unity components managed by Zenject. Runtime-created objects that require injection must be created with Zenject APIs, a Zenject factory, or explicitly injected:

```csharp
var instance = Container.InstantiatePrefab(prefab, parent);
// For an object created by another system:
Container.InjectGameObject(instance);
```

Private injection points and reflection-created providers need linker preservation for IL2CPP. Keep `Assets/link.xml` synchronized when adding either pattern.

## Boot and startup pipeline

`GameBootstrapper` is the single owner of the supplied boot sequence. A normal run is:

1. Initialize Addressables.
2. Optionally check remote catalogs when explicitly enabled.
3. Ensure configured preload labels.
4. Preload configured prefabs.
5. Run ordered startup steps.
6. Load the configured first game scene.

Do not run `ContentBootstrapper` and `GameBootstrapper` as competing boot owners in the same scene. `ContentBootstrapper` is only a standalone/legacy alternative for projects that need content initialization without the complete flow.

### Add a startup step

Use one responsibility per step: load a save, authenticate, fetch remote config, initialize analytics, or enforce a force-update rule.

```csharp
using System.Threading;
using System.Threading.Tasks;
using Serhat.Forge.Startup;
using UnityEngine;

public sealed class LoadPlayerSaveStep : StartupStep
{
    [SerializeField] private PlayerSaveController _saveController;

    public override Task ExecuteAsync(CancellationToken cancellationToken)
    {
        return _saveController.LoadAsync(cancellationToken);
    }
}
```

Attach the component to the boot hierarchy, configure whether it is required, its timeout, retry count, retry delay, and cancellation grace, then add it to the bootstrapper's ordered startup-step list.

Rules:

- Throw when a step fails; the pipeline decides whether an optional failure may continue.
- Honor the supplied `CancellationToken`.
- A timed-out attempt is not started again while its previous task is still running.
- Keep cancellation cleanup bounded and deterministic.
- Required-step failure stops boot; optional-step failure is reported and boot continues.

## Addressables and content ownership

The checked-in Addressables groups are intentionally empty. `Local_Core` and `Remote_Default` establish a safe structure, not game content. Common labels are `core`, `gameplay`, `ui`, and `audio`.

Remote catalogs are disabled by default. Do not enable remote catalog checks until the profile URLs, hosting, version policy, rollback plan, and offline behavior are configured for the target environment.

`IContentManager.LoadAsync<T>` returns both an asset and an owned handle. Retain and dispose that handle when the consumer's lifecycle ends:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Serhat.Forge.Content;
using UnityEngine;
using Zenject;

public sealed class IconLoader : MonoBehaviour
{
    private IContentManager _content;
    private IContentHandle<Sprite> _iconHandle;

    [Inject]
    private void Construct(IContentManager content)
    {
        _content = content;
    }

    public async Task<Sprite> LoadAsync(string key, CancellationToken cancellationToken)
    {
        _iconHandle?.Dispose();
        var result = await _content.LoadAsync<Sprite>(key, cancellationToken);
        if (!result.IsSuccess)
            throw new System.InvalidOperationException(result.ErrorMessage);

        _iconHandle = result.Handle;
        return result.Asset;
    }

    private void OnDestroy()
    {
        _iconHandle?.Dispose();
        _iconHandle = null;
    }
}
```

Use `IPrefabLoader` when a set of prefab handles should be retained for the whole application and looked up synchronously after boot. Call `ReleaseAll` only when that cache lifecycle ends.

Never call `Addressables.Release` separately for a handle already owned by `IContentManager` or `IPrefabLoader`.

## Persistence

Persistence is deliberately generic. Your game supplies the serializable root DTO, participant mapping, schema identifier, version, and migrations.

The default storage stack provides:

- an atomic primary/temporary/backup write flow
- SHA-256 corruption detection
- schema and data version checks
- ordered migrations
- recovery from the newest valid candidate
- a single-writer gate
- ordered participant capture and restore
- transactional rollback when a participant fails to restore

### Composition

```csharp
using System;
using System.IO;
using Serhat.Forge.Persistence;
using UnityEngine;

[Serializable]
public sealed class GameSaveData
{
    public int softCurrency;
    public int highestLevel;
}

public static class GameSaveComposition
{
    public static SaveCoordinator<GameSaveData> Create()
    {
        var path = Path.Combine(Application.persistentDataPath, "player.save");
        var repository = new VersionedJsonSaveRepository<GameSaveData>(
            path,
            schemaId: "com.example.game.player",
            currentDataVersion: 1);

        return new SaveCoordinator<GameSaveData>(
            repository,
            dataFactory: static () => new GameSaveData());
    }
}
```

Register game systems as `ITransactionalSaveParticipant<TData>`. Each participant must capture a non-mutating pre-restore snapshot and be able to roll back. The default coordinator rejects non-transactional participants before mutation.

Save DTO constraints:

- Mark the root and nested DTOs `[Serializable]`.
- Use fields supported by `JsonUtility`; do not use dictionaries.
- Treat the checksum as corruption detection, not encryption or anti-cheat.
- Keep secrets and unnecessary personal data out of local saves.
- Profile capture/serialization cost for large saves.
- A build with an older data version refuses a valid future-version save.

Use `SaveLifecycleRelay` only after initializing it with the coordinator. Pause, focus-loss, and quit saves are safety nets; keep explicit checkpoint saves for important transitions.

## Feature gates

`FeatureGateService` combines:

- progress thresholds
- optional external conditions such as entitlements or experiments
- unlock and visibility decisions
- persistent seen/notification state
- runtime overrides

Create a `FeatureGateConfig` from **Assets > Create > Serhat Forge > Config > Feature Gate Config**. Use unique `FeatureId` values and one rule per feature. Duplicate IDs fail validation.

Supply:

- `IFeatureProgressProvider` for the current generic progress value
- `IFeatureGateStateStore` for seen state
- optionally `IFeatureGateConditionProvider` for entitlements/experiments

Missing external conditions fail closed. Call `Dispose` when the service lifecycle ends so event subscriptions are removed. `PlayerPrefsFeatureGateStateStore` is suitable for low-risk UI seen state, not authoritative progression or entitlements.

`LevelUnlockCatalog` remains named for serialized asset compatibility; its runtime meaning is a generic progress-threshold catalog.

## Pooling

`ComponentPool<T>` wraps Unity's `ObjectPool<T>` with prefab creation, prewarming, active-lease tracking, safe disposal, and cleanup of externally destroyed Unity objects.

```csharp
var pool = new ComponentPool<ProjectileView>(
    projectilePrefab,
    poolRoot,
    defaultCapacity: 16,
    maxSize: 128);

pool.Prewarm(16);
var projectile = pool.Get(position, rotation);
pool.Release(projectile);

// When the owning feature/scene ends:
pool.Dispose();
```

Only the pool owner may release or dispose leases. A double release or an instance from another pool throws. `Prewarm` allocates during setup; steady-state get/release is designed to avoid managed allocation.

## Analytics

The embedded analytics package provides provider abstraction, serialized dispatch, batching, offline persistence, and safe fallback behavior. The scene-side `AnalyticsManager` and `AnalyticsStartupStep` are integration helpers; the reusable implementation lives in `Packages/com.serhat.analytics-sdk`.

See the [Analytics SDK guide](../Packages/com.serhat.analytics-sdk/README.md) for composition and lifecycle. In production:

- obtain consent before collecting data that requires it
- never send credentials, raw receipts, or unnecessary personal data
- set stable event schemas and version them
- flush during controlled lifecycle transitions
- dispose the SDK so in-flight delivery can drain

## Localization

Serhat Forge includes a project-level localization bridge and the embedded localization SDK. Catalogs live under `Assets/StreamingAssets/Localization/Locales`; `en.json`, `tr.json`, and `Localization.csv` must keep the same keys.

Initialize before reading values:

```csharp
await Serhat.Localization.Loc.InitializeAsync();
var title = Serhat.Localization.Loc.Get("ui.title");
```

Use **Tools > Serhat > Localization** for settings and CSV import. See the [Localization SDK guide](../Packages/com.serhat.localization-sdk/README.md).

## Audio, UI, tutorial, scenario, and haptics

- `IAudioService` is application-scoped. The default installer creates `SoundManager`; disabling it binds `NullAudioService`.
- Navigation and tween-driven UI components compile only when the documented DOTween integration is enabled.
- `TutorialRunner` provides data-driven ordered steps and session signals. Persisted tutorial progress remains a game policy.
- The scenario system provides reusable command assets/components. Treat game-specific sequences as content, not framework code.
- Haptics use the optional Nice Vibrations adapter when available and a limited mobile fallback otherwise.

These presentation helpers are optional. Remove unused scene components and game content, but preserve `.meta` files when moving retained assets.

## Runtime and release checks

For every enabled system:

1. Test its disabled/default path.
2. Test initialization failure and cancellation.
3. Verify lifecycle cleanup and owned resources.
4. Run EditMode and PlayMode tests.
5. Run an IL2CPP build for each shipping platform.
6. Exercise native/provider integrations on real devices.

Use [CI and release](CI_AND_RELEASE.md) for the complete release gate.
