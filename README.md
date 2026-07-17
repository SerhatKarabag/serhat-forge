# Serhat Forge

A production-minded Unity 6 starter project for teams that want reusable foundations without inheriting a game-specific codebase.

> **Status:** `0.1.0-preview.1`. The default project is safe-by-default and intended for evaluation and new-project bootstrapping. Treat production readiness as a release gate for your game, not as a guarantee inherited from a template.

## What is included

- Unity `6000.3.14f1` with URP `17.3.0`, Input System, Addressables, and Unity Test Framework.
- Zenject/Extenject composition with a project-lifetime installer and an asynchronous startup pipeline.
- Versioned JSON persistence with checksum, migration, recovery, transactional restore, and rollback contracts.
- Addressables content bootstrapping, prefab loading, pooling, feature gates, audio, localization, tutorial/scenario, UI navigation, haptics, and camera helpers.
- Analytics provider abstraction with serialized delivery and an offline outbox.
- Optional, compile-gated adapters for authentication, ads, IAP, Firebase Analytics, PlayFab, Google Play Games, DOTween, SRDebugger, and native mobile services.
- Embedded Serhat packages for core utilities, analytics, backend resilience, localization, and monetization.

The template intentionally contains no gameplay loop, progression model, store catalog, production credentials, or live Unity Gaming Services environment.

## Requirements

- Unity Editor `6000.3.14f1`.
- Git for dependencies pinned by URL or commit.
- .NET 8 SDK only when working with the optional Azure Functions references.
- Android/iOS modules only when building those platforms.

## Quick start

1. Create a repository from this template or clone it into a new folder.
2. Open the folder with Unity `6000.3.14f1` and wait for Package Manager resolution to finish.
3. Run **Tools > Serhat Forge > Setup > Project Settings**. Set your company/product names, application identifiers, version/build numbers, build scenes, and target-platform IL2CPP defaults. Configure signing, capabilities, icons, and store identifiers separately; never place production secrets in the template.
4. Run **Tools > Serhat Forge > Setup > Repair Zenject Composition**. The command is idempotent and creates or repairs `Assets/Resources/ProjectContext.prefab`.
5. Open `Assets/Scenes/SampleScene.unity` and enter Play Mode.
6. Enable only the optional integrations you installed. The corresponding scripting symbols are documented in [TEMPLATE_README.md](TEMPLATE_README.md).
7. Run the repository verifier:

```powershell
pwsh -File ./Tools/Verify-Repository.ps1
```

Do not copy generated `Library`, `Temp`, `Logs`, `UserSettings`, `obj`, `.sln`, or Unity-generated `.csproj` files into a new repository.

## Composition and startup

`ForgeProjectInstaller` owns application-lifetime bindings. `GameBootstrapper` owns boot orchestration only: content initialization, optional catalog checks, preload, startup steps, and first-scene loading. Scene-specific bindings belong in scene installers or scene contexts.

The startup pipeline supports ordered required/optional steps, timeout, cooperative cancellation, bounded cancellation grace, retry policy, and failure reporting. Runtime-created objects that require injection must be created through Zenject factories/container APIs or explicitly injected after Addressables instantiation.

Private Zenject injection points used by the first-party runtime are preserved in `Assets/link.xml` for managed stripping and IL2CPP. Providers created through `Type.GetType`/`Activator` carry explicit `Preserve` attributes. Keep those preservation declarations in sync when adding reflection-created services or private injection targets.

## Packages and optional samples

The reusable client packages are embedded under `Packages/com.serhat.*` so the template works from a clean clone.

`Samples~/GameApiBackend` is a game-domain **reference sample**, not part of the default framework contract. It demonstrates an Azure Functions/PlayFab-style progression backend and can be removed without affecting the generic client core.

`cloudscript-azure-functions-monetization` is an optional server-side reference. Deploy it only after configuring store credentials outside the repository and passing its signature, authentication, replay, idempotency, and environment validation tests.

## Safe defaults

- Ads and optional remote providers are disabled until configured.
- Unity Purchasing client code is disabled by default. Enable `UNITY_PURCHASING` explicitly from the setup wizard for Android, iOS, and Standalone only after hardening the store catalog and backend receipt validation.
- UnityConnect, Purchasing, and Ads automatic initialization are disabled in the checked-in project settings.
- Addressables groups start empty; remote catalog build/checks are disabled, while local content builds deterministically with each player build.
- UGS environment identifiers, platform signing values, console identifiers, and secrets are blank.
- The checked-in application identifier is a placeholder, not a publishable store identity.
- Local settings templates contain placeholders only; real secrets belong in environment variables or a managed secret store.

## Validation before shipping a game

- Unity Console is clean after a fresh import.
- EditMode and PlayMode tests pass.
- The composition tests under `Assets/Tests` confirm the ProjectContext installers, SampleScene SceneContext/demo wiring, private Zenject injection, and PlayMode bootstrap injection.
- Addressables Analyze and a clean content build pass.
- Android and iOS IL2CPP development builds pass on clean agents.
- Mobile authentication, Keychain/Game Center, ads, purchases, analytics, and deep links are exercised on real devices when enabled.
- Server webhook signature/authentication and replay tests pass for every enabled store environment.
- Repository and build-artifact secret scans are clean.

The template includes deterministic mobile development-build entry points. Set
`SERHAT_FORGE_BUILD_PATH`, select the matching target with `-buildTarget`, and call:

```text
Serhat.Forge.Editor.SerhatForgeBatchBuild.BuildAndroidDevelopment
Serhat.Forge.Editor.SerhatForgeBatchBuild.BuildIosDevelopment
```

Both commands fail unless the target uses IL2CPP and at least one valid Build Settings scene is enabled. Android additionally requires ARM64 and deterministically produces a development APK; iOS produces an Xcode project that must still be compiled and signed on macOS.

See the preserved [Turkish technical guide](TEMPLATE_README.md) for detailed system notes and integration symbols.

## Contributing and security

Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request. Report vulnerabilities privately as described in [SECURITY.md](SECURITY.md), never in a public issue.

## License

Serhat Forge first-party code is available under the [MIT License](LICENSE). Third-party components and imported Unity resources retain their own terms; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
