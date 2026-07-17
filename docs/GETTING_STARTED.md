# Getting started

This guide takes a clean Serhat Forge repository to a verified Unity project and then separates the removable sample from the foundations a game should keep.

[← README](../README.md) · [Features](FEATURES.md) · [Architecture](ARCHITECTURE.md) · [Troubleshooting](TROUBLESHOOTING.md)

## 1. Prerequisites

Install only what the work you are doing requires:

- **Unity Hub and Unity Editor `6000.3.14f1`** for all template work.
- **Git** available to Unity Package Manager. Two authoring packages are pinned to Git commits.
- The matching **Android Build Support** or **iOS Build Support** Unity module when building that target.
- **PowerShell 7 (`pwsh`)** to run the repository verifier locally.
- **.NET 8 SDK** only when changing or testing the optional cloud reference projects.

Use the exact checked-in Editor version for the first import. Upgrading Unity or packages is a separate, reviewed change; see [Upgrading](UPGRADING.md).

## 2. Create the game repository

### Recommended: GitHub template

1. Open the Serhat Forge repository on GitHub.
2. Select **Use this template → Create a new repository**.
3. Choose the owner, game repository name, and visibility. Do not include every branch; `main` is the template baseline.
4. Clone the newly created game repository—not the Serhat Forge source repository:

   ```bash
   git clone https://github.com/<owner>/<new-game>.git
   cd <new-game>
   ```

The resulting repository has its own history and can evolve independently. Template updates do not flow into it automatically; use the process in [Upgrading](UPGRADING.md).

### Evaluation or template contribution

Clone Serhat Forge directly when evaluating the source or contributing back to the template:

```bash
git clone https://github.com/SerhatKarabag/serhat-forge.git
cd serhat-forge
```

### Do not overlay it on a blank project

Do **not** create a Unity project and then copy Serhat Forge into it. The baseline depends on coordinated files under `Assets`, `Packages`, and `ProjectSettings`, including serialized GUID references, Addressables settings, Zenject composition, package locks, build settings, and stripping preservation.

For an existing game, integrate only the specific embedded `com.serhat.*` package or port a system deliberately. Treat that as a migration, not template installation.

## 3. Open the project in Unity Hub

1. In Unity Hub, select **Add → Add project from disk**.
2. Select the repository root—the folder that directly contains `Assets`, `Packages`, and `ProjectSettings`.
3. If Hub asks for an Editor, install or select `6000.3.14f1`.
4. Open the project and let Package Manager resolution, script compilation, and the first asset import complete.

The first import can be CPU- and disk-intensive. Wait for Unity's progress indicators to finish before responding to secondary compiler errors; an interrupted package resolution is often the actual cause. If the project remains red after resolution, follow [Troubleshooting](TROUBLESHOOTING.md).

## 4. Apply game identity and build defaults

Open **Tools → Serhat Forge → Setup → Project Settings**.

Enter project-owned values:

- **Company Name** and **Product Name**.
- A lowercase reverse-domain **Bundle Identifier**, for example `com.studio.game`.
- **Bundle Version**, **Android Version Code**, and **iOS Build Number**.
- Whether `SampleScene` should be first in Build Settings.
- Whether Android and iOS should use the template's IL2CPP defaults.

For a normal first run, keep the scene and IL2CPP options enabled.

Selecting **Apply Project Setup** changes only local Unity project identity/build settings. In particular, it can:

- set the application identifier for Android, iOS, and Standalone;
- put `Assets/Scenes/SampleScene.unity` first while preserving other Build Settings scenes;
- set Android/iOS to IL2CPP with Medium managed stripping.

It does not create signing keys, service credentials, UGS environments, store products, capabilities, icons, or production backend configuration. Add those per environment through [Integrations](INTEGRATIONS.md).

Review and commit the resulting `ProjectSettings` changes to the game repository.

## 5. Verify Zenject composition

Run **Tools → Serhat Forge → Setup → Repair Zenject Composition**.

This idempotent command:

- creates or repairs `Assets/Resources/ProjectContext.prefab`;
- ensures `ForgeProjectInstaller`, `ForgeBootstrapInstaller`, and `GameBootstrapper` are connected;
- keeps additional project installers already assigned to `ProjectContext`;
- ensures `SampleScene` has one `SceneContext`; and
- ensures the removable `ForgeDemoPanel` smoke test is present.

Run it after resolving composition merge conflicts or if the context assets were removed. It may open and save `SampleScene`, so save current scene work when prompted.

## 6. Prove the baseline works

1. Open `Assets/Scenes/SampleScene.unity`.
2. Clear the Console.
3. Enter Play Mode.

Expected result:

- The **Serhat Forge** panel appears.
- `Boot state: Ready` is displayed.
- `Content initialized: True` is displayed.
- **Restart startup** completes successfully.
- **Toggle audio mute** changes both music and SFX mute state.
- **Show rewarded ad** is unavailable because the safe default uses `NullAdService`.
- The Console contains no errors or unhandled exceptions.

An empty Addressables preload and a disabled rewarded-ad button are expected; the template intentionally ships without game content or an ad provider.

Then run the repository gate from the repository root:

```powershell
pwsh -File ./Tools/Verify-Repository.ps1
```

For EditMode/PlayMode, Addressables, cloud, and platform build gates, follow [CI and release](CI_AND_RELEASE.md).

## 7. Turn the sample into your game

Keep the application-lifetime foundation, replace the demonstration surface, and introduce game-domain code behind your own contracts.

1. **Create the game's scenes.** Add a boot/menu/gameplay structure appropriate to the game and update Build Settings. Keep `SampleScene` temporarily as a smoke test or remove it after equivalent composition coverage exists.
2. **Keep one application composition root.** Extend `ForgeProjectInstaller` carefully or add focused installers to `ProjectContext`. Put scene-lifetime bindings in each scene's `SceneContext`, not in the bootstrapper.
3. **Remove the demo UI.** Remove `ForgeDemoPanel` from the sample `SceneContext`, or delete the sample scene after replacing its test coverage. Do not remove `ProjectContext.prefab` merely because the panel is gone.
4. **Define startup work.** Implement small `StartupStep` assets for required operations such as save restore and optional operations such as analytics. Make every async step honor cancellation.
5. **Create game persistence DTOs.** Keep them serializable and versioned, add migrations, and use transactional participants for restore. A checksum detects corruption; it is not encryption or anti-cheat.
6. **Organize content.** Add Addressables entries and labels with explicit handle ownership. Keep remote catalogs off until the CDN paths and failure behavior are configured.
7. **Replace starter localization keys.** Maintain every supported locale together and initialize localization before calling the `Loc` facade.
8. **Add optional services one at a time.** Install the SDK, add environment configuration, enable a scripting symbol only when that integration documents a genuinely optional dependency, then run its tests. Never add a symbol merely to make a missing type compile; package-owned dependencies such as Unity IAP do not need one.
9. **Add game-specific tests and CI.** Preserve the template's composition tests and add coverage for domain startup, save migrations, offline behavior, and release integrations.

Practical recipes live in [Core systems](CORE_SYSTEMS.md). Ownership rules and the runtime flow are shown in [Architecture](ARCHITECTURE.md).

## 8. First production checklist

Before considering the new game baseline established:

- [ ] Replace the placeholder company, product, bundle identifier, versions, icons, and splash assets.
- [ ] Decide which template systems are kept, adapted, or removed using [Features](FEATURES.md).
- [ ] Confirm every required startup step fails safely and every optional step degrades safely.
- [ ] Define save schema ownership, migrations, rollback behavior, and corruption policy.
- [ ] Replace starter localization data with game-owned keys and content.
- [ ] Keep remote Addressables disabled or configure real, environment-specific build/load URLs and rollback.
- [ ] Keep unused integrations disabled and remove unused SDKs.
- [ ] Store credentials in platform configuration, CI secrets, or a managed secret store—never in Git.
- [ ] Enable and pass the relevant repository, Unity, cloud, Addressables, and platform gates.
- [ ] Test enabled authentication, ads, purchases, analytics, deep links, secure storage, and native capabilities on real devices.

## Common mistakes to avoid

- Committing `Library`, `Temp`, `Logs`, `UserSettings`, `obj`, `.sln`, or Unity-generated `.csproj` files.
- Renaming/moving assets outside Unity and losing `.meta` GUID relationships.
- Using the placeholder remote Addressables URL or bundle identifier in a build.
- Treating the reference cloud projects as a ready-made game economy.
- Putting service credentials or signing material in Resources, StreamingAssets, source, or committed JSON.
- Loading Addressables directly without a clear handle owner and release point.
- Creating injected runtime objects with plain `new`/`Instantiate` when they require Zenject construction or explicit injection.
- Assuming template validation replaces store, backend, security, and real-device validation for the game.

If the first run does not match the expected result, start with [Troubleshooting](TROUBLESHOOTING.md).
