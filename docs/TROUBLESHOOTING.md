# Troubleshooting

Start from the smallest reproducible state. Record the Serhat Forge commit, Unity version, target platform, enabled scripting symbols, and the first error in the log. Later errors are often consequences of the first one.

## Baseline recovery sequence

1. Close Unity and your IDE.
2. Confirm the project root contains `Assets`, `Packages`, and `ProjectSettings`.
3. Confirm the editor version is `6000.3.14f1`.
4. Confirm Git is installed and available to Unity Hub/Unity.
5. Remove any scripting symbol whose SDK is not installed.
6. Reopen the project and wait for Package Manager/import to finish.
7. Run **Tools > Serhat Forge > Setup > Repair Zenject Composition**.
8. Run repository validation and then EditMode tests.

If a clean reimport is required, close Unity and remove the generated `Library` folder. Unity will rebuild it. Never delete `Assets`, `Packages`, `ProjectSettings`, or `.meta` files as part of cache cleanup.

## Unity Hub does not recognize the project

Select the directory that directly contains:

```text
Assets/
Packages/
ProjectSettings/
```

Do not create a blank Unity project first and do not select the parent directory that merely contains the clone.

## Wrong Unity version or automatic upgrade prompt

The tested version is stored in `ProjectSettings/ProjectVersion.txt`. Install `6000.3.14f1` in Unity Hub and add the project with that editor.

Do not accept an editor upgrade on the main branch. Create an upgrade branch, read Unity/package migration notes, commit a clean pre-upgrade state, perform the upgrade, and rerun the full test/build matrix.

## Package Manager cannot resolve dependencies

Most likely causes:

- Git is missing or unavailable to the Unity process.
- A Git dependency is blocked by network/proxy policy.
- A package lock and manifest were partially edited.
- A provider SDK was installed with an incompatible method/version.

Checks:

1. Inspect the first Package Manager error.
2. Confirm the Git URLs and pinned commits in `Packages/manifest.json` are reachable from your environment.
3. Restore `Packages/manifest.json` and `Packages/packages-lock.json` together if an edit was accidental.
4. Remove duplicate provider installations.
5. Close Unity and clear only generated package/import caches when necessary.

Serhat Forge vendors its patched Zenject/Extenject source under `Assets/Plugins/Zenject`. Do not also install a second Extenject/Zenject package; duplicate assemblies and types will result.

## `ProjectContext` or Zenject composition is missing

Symptoms include a missing `ProjectContext`, unresolved `IContentManager`, an installer smoke-test failure, or boot components not receiving injection.

Run:

**Tools > Serhat Forge > Setup > Repair Zenject Composition**

Then verify:

- `Assets/Resources/ProjectContext.prefab` exists.
- `ForgeProjectInstaller` is present in its installer list.
- The boot scene has a `SceneContext`.
- The boot scene's `ForgeBootstrapInstaller` references the `GameBootstrapper`.

The repair command is safe to run repeatedly.

## Runtime-created object is not injected

`Instantiate` and Addressables do not automatically inject arbitrary objects. Prefer a Zenject factory or `DiContainer.InstantiatePrefab`. If another system owns creation, explicitly call `DiContainer.InjectGameObject` after instantiation and before the object starts dependent work.

For IL2CPP, update `Assets/link.xml` when adding private injection points or reflection-created types.

## Boot hangs, times out, or loads no scene

Check the `GameBootstrapper` log and each `StartupStep` configuration.

- Ensure at least one valid scene is enabled in Build Settings.
- Confirm required Addressables keys/labels actually exist.
- Keep remote catalogs disabled until their URLs are configured.
- Make every startup step honor cancellation.
- Do not run `ContentBootstrapper` and `GameBootstrapper` as competing owners.
- A required step stops boot; mark truly non-critical integrations optional.
- Do not retry a timed-out operation outside the pipeline while the old operation may still run.

## Addressables asset is missing or never released

- The template groups intentionally start empty; add your own entries.
- Use exact keys and the defined `core`, `gameplay`, `ui`, or `audio` labels.
- Check `ContentLoadResult.Status` and `ErrorMessage`.
- Retain the returned `IContentHandle<T>` and dispose it once.
- Do not release the same handle through both Addressables and `IContentManager`.
- Use Addressables Analyze and a clean content build before player builds.
- `Remote_Default` URLs are placeholders; do not enable remote loading before configuring hosting.

## Localization returns keys or empty values

1. Call `await Loc.InitializeAsync()` before the first lookup.
2. Confirm `Assets/Resources/LocalizationSettings.asset` exists.
3. Confirm locale files are under `Assets/StreamingAssets/Localization/Locales` when using the StreamingAssets provider.
4. Keep `en.json`, `tr.json`, and `Localization.csv` keys aligned.
5. Use **Tools > Serhat > Localization > Import CSV** after editing the CSV source.
6. Check fallback/default locale configuration.

The repository verifier reports catalog-key drift and invalid JSON.

## Optional SDK symbol causes compile errors

Remove the symbol first. Then install/configure the provider and re-enable the symbol.

Common mappings:

- `DOTWEEN` -> DOTween
- `SRDEBUGGER` -> SRDebugger
- `GOOGLE_MOBILE_ADS` -> Google Mobile Ads
- `PLAYFAB_SDK` -> PlayFab Unity SDK
- `SERHAT_FORGE_AUTH` -> auth layer, also requires PlayFab
- `GOOGLE_PLAY_GAMES` -> Google Play Games, also requires auth symbols

See [Integrations](INTEGRATIONS.md) for the complete matrix.

Unity IAP does not use a manual scripting symbol. The monetization package declares Unity Purchasing `5.2.0` and isolates its adapter in `Serhat.BackendSdk.Monetization.UnityIap`. If that adapter does not compile, restore Package Manager dependencies and verify that `Unity.Purchasing` and `Unity.Purchasing.Stores` are available; do not add `UNITY_PURCHASING`.

## Purchases return `410 LEGACY_MONETIZATION_DISABLED`

The client or backend registration is targeting retired endpoints. Use the hardened `VerifyPurchase` and `GetEntitlements` functions from `cloudscript-azure-functions-monetization`. Do not re-enable `IapVerify`, `IapGetEntitlements`, or `GrantPurchaseRewards`.

Verify that:

- the updated client is deployed
- PlayFab/Azure function registration uses the hardened function names
- the hardened Function App is deployed separately
- request authentication and environment configuration are valid
- client/server DTO and envelope versions match

## Cloud tests fail

Run the same release graph locally:

```powershell
dotnet test "./Samples~/GameApiBackend/tests/Serhat.Forge.CloudScript.Tests.csproj" `
  --configuration Release `
  --property:TreatWarningsAsErrors=true
```

Use .NET 8. Start with the first compiler/test failure. Never weaken authentication, signature, replay, identity, or production-environment assertions merely to make a test pass.

## GitHub Unity workflow is skipped

This is expected until repository variable `RUN_UNITY_CI` is exactly `true`. Configure the appropriate Unity license secrets described in [CI and release](CI_AND_RELEASE.md).

Fork pull requests are intentionally skipped because secrets are not exposed to untrusted forks.

## Android development build fails

The supplied batch entry point requires:

- Android is the selected `-buildTarget`
- IL2CPP scripting backend
- ARM64 enabled
- at least one enabled Build Settings scene
- Android Build Support installed in Unity Hub
- a writable `SERHAT_FORGE_BUILD_PATH`

Production signing is intentionally outside the template. Do not commit keystores or passwords.

## iOS export succeeds but there is no installable app

Unity produces an Xcode project. Compile and sign it on macOS with the downstream game's team, provisioning, identifiers, entitlements, and capabilities.

Native authentication, Keychain, Game Center, notifications, purchases, and deep links require real-device validation.

## Repository verifier reports a risky or generated file

Remove generated Unity/IDE/build output from Git tracking and ensure `.gitignore` covers it. For a secret/config finding:

1. Revoke/rotate the exposed credential first.
2. Remove it from the current tree and Git history as appropriate.
3. Move the value to environment variables, GitHub secrets, or a managed secret store.
4. Rerun the verifier.

Do not merely add a real credential to an allowlist.

## Reporting a reproducible problem

Use the repository's bug-report form and include:

- Serhat Forge version/commit
- Unity version
- platform
- minimal clean-clone reproduction
- expected and actual behavior
- sanitized logs beginning with the first error
- enabled scripting symbols and optional SDK versions

Remove credentials, personal/player data, raw receipts, tokens, signing material, and private backend identifiers. Report security vulnerabilities privately according to `SECURITY.md`.
