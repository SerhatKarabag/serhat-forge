# Optional integrations

Serhat Forge keeps third-party integrations opt-in. The default clone compiles and enters Play Mode without production credentials or live service bindings. Install an SDK first, configure it for each environment, and only then enable its scripting symbol.

## Integration workflow

Use the same sequence for every provider:

1. Create an integration branch.
2. Install a provider version compatible with Unity `6000.3.14f1`.
3. Read and accept the provider's current license and platform requirements.
4. Add environment-specific configuration without committing secrets.
5. Enable the required scripting symbols for only the intended build targets.
6. Fix all compile errors before adding runtime configuration.
7. Test disabled, success, cancellation, retry, and failure paths.
8. Make clean IL2CPP builds and run real-device tests.
9. Add production monitoring and a rollback/disable mechanism.

Never commit keystores, service-account files, private keys, store credentials, raw receipts, player session tickets, or production connection strings.

## Capability matrix

| Integration | Required symbol | Default | Configuration boundary |
|---|---|---|---|
| Unity IAP client | None; package dependency | Compiled, not composed | Composition root, store catalog, hardened backend |
| Local purchase stub | `SERHAT_FORGE_LOCAL_MONETIZATION` | Disabled | Editor/development builds only |
| Google Mobile Ads | `GOOGLE_MOBILE_ADS` | Disabled | Provider plugin + `AdRuntimeSettings` |
| Firebase Analytics | `FIREBASE_ANALYTICS_AVAILABLE` | Disabled | Firebase package and platform config |
| PlayFab backend adapter | `PLAYFAB_SDK` | Disabled | PlayFab SDK, title/environment, authenticated session |
| Serhat Forge authentication | `SERHAT_FORGE_AUTH` + PlayFab | Disabled | Provider SDKs, secure storage, platform identity |
| Google Play Games auth | `GOOGLE_PLAY_GAMES` + auth symbols | Disabled | Android plugin and Play Console configuration |
| Game API reference sample | `SERHAT_FORGE_GAME_API_SAMPLE` | Disabled | Imported Package Manager sample |
| DOTween presentation helpers | `DOTWEEN` | Disabled | DOTween installed and set up |
| SRDebugger | `SRDEBUGGER` | Disabled | SRDebugger installed |
| Nice Vibrations | `NICE_VIBRATIONS` or vendor symbol | Disabled | Nice Vibrations installed |
| iOS game-service postprocess | `SERHAT_FORGE_IOS_GAME_SERVICES` | Disabled | iOS target, signing and Apple capabilities |

Do not manually add a symbol merely to silence missing code. A symbol declares that its dependency and configuration are present. Unity IAP is the exception in this matrix: it is a required package dependency and does not use a scripting symbol.

## Unity IAP and monetization

The monetization package requires Unity Purchasing `5.2.0`. Installing Serhat Forge therefore compiles the provider-independent monetization core and the separate Unity IAP adapter assembly automatically. Compilation does not initialize Unity Purchasing, create a store client, or make purchases available at runtime.

### Configure the runtime integration

1. Define your game-specific product catalog and subscription validation policy.
2. Configure matching products in App Store Connect and/or Play Console.
3. Connect `MonetizationBackendClient` to an authenticated `ICloudFunctionInvoker`.
4. In the game composition root, create the `UnityIapStoreClient`, pending-purchase storage, backend client, and one application-lifetime `PurchaseService`. Before initialization, derive both bindings from the stable authenticated PlayFab title-player ID: pass `StoreAccountIdentity.CreateGoogleObfuscatedAccountId` to `SetGoogleObfuscatedAccountId`, and `StoreAccountIdentity.CreateAppleAppAccountToken` to `SetAppleAppAccountToken`. Bind the service as a Zenject singleton or dispose it explicitly so Unity IAP event subscriptions are removed.
5. Deploy the hardened monetization Function App separately.
6. Initialize the purchase service from an owned startup step and expose purchase UI only after initialization succeeds.
7. Verify consumable, non-consumable, subscription, restore, duplicate, refund, and offline flows on sandbox accounts.

For an Android-only backend set `APPLE_STORE_ENABLED=false`; for an iOS-only backend set
`GOOGLE_STORE_ENABLED=false`. At least one must remain enabled. Disabled providers do not require
credentials and their verification/webhook routes fail closed. Keep both `true` only when the
deployment actually serves both stores.

The built-in client supports first-time subscription purchases but intentionally fails closed when an active subscription is changed to another SKU. Store replacement is project-specific: Google requires an explicit replacement mode plus linked-purchase-token/RTDN backend handling, while Apple subscription-group behavior must match App Store configuration. Do not expose upgrade/downgrade UI until that complete lifecycle is implemented and tested on real sandbox devices.

Use protected/encrypted `IStorage` for pending Google purchase tokens in production. Apple recovery persists only the transaction ID; the client deliberately discards AppReceipt/JWS payloads. `FileStorage` is plaintext and is intended only for local development or threat models that explicitly accept it. Cancellation stops the local wait but cannot cancel a native transaction already accepted by the store; keep the application-lifetime service alive so late callbacks can be reconciled.

The backend validates Google and Apple purchase ownership against the authenticated PlayFab identity. Never derive either binding from a display name, device ID, or caller-supplied backend field. Replace the application-lifetime store service after login/logout so one store instance is never shared across authenticated players. Existing Apple purchases created before `appAccountToken` was enabled need an explicit, reviewed restore/migration policy; production verification otherwise fails closed.

The client must call the hardened `VerifyPurchase` and `GetEntitlements` functions. The retired `IapVerify`, `IapGetEntitlements`, and `GrantPurchaseRewards` endpoints return `410 Gone` and must not be re-enabled.

Treat `GetEntitlements` as an authoritative operation that can fail: it paginates the complete
PlayFab Economy v2 inventory and returns each stack's real quantity and expiry. Do not turn a
`503 INVENTORY_UNAVAILABLE` response into an empty inventory or revoke local access UI based on it.

`SERHAT_FORGE_LOCAL_MONETIZATION` compiles the local backend stub only in the Editor or a Development Build. It is intentionally excluded from non-development players and is never proof of production receipt validation. It does not activate Unity IAP by itself; a composition root must still create and initialize the purchase service.

Use these guides together:

- [Monetization SDK](../Packages/com.serhat.monetization-sdk/README.md)
- [Monetization Function App](../cloudscript-azure-functions-monetization/README.md)

Production secrets belong in Function App settings or a managed secret store. Client builds must never contain Apple private keys, PlayFab developer secrets, Google service-account private keys, or webhook secrets.

## Ads

Ads are disabled in `Assets/Resources/AdRuntimeSettings.asset`. With ads disabled, `ForgeProjectInstaller` binds `NullAdService`.

To integrate Google Mobile Ads:

1. Install a Unity-6-compatible Google Mobile Ads plugin.
2. Resolve Android/iOS native dependencies.
3. Add `GOOGLE_MOBILE_ADS` to the intended targets.
4. Configure application and placement identifiers per environment.
5. Enable ads in `AdRuntimeSettings.asset` only after configuration is present.
6. Validate consent, test-device, no-fill, offline, pause/resume, rewarded completion, and failure paths.
7. Confirm production builds do not use test unit identifiers.

Never reward a player from the client callback alone when the reward has economic value. Use a server-authoritative, idempotent reward endpoint.

## Analytics and Firebase

The analytics core works without Firebase. The Firebase provider assembly is gated by `FIREBASE_ANALYTICS_AVAILABLE`; its assembly definition can derive that symbol from the installed Firebase Analytics package.

1. Install Firebase Analytics using the provider's supported Unity installation method.
2. Add environment-specific Firebase configuration through your secure release process.
3. Confirm the Firebase provider assembly compiles.
4. Configure `AnalyticsServiceBuilder` before `AnalyticsManager.InitializeAsync`.
5. Gate collection and advertising identifiers behind the consent policy required by your game and jurisdictions.
6. Verify event schema, user/session properties, offline outbox, flush, and provider-failure behavior.

Do not manually force `FIREBASE_ANALYTICS_AVAILABLE` when the SDK is absent. See the [Analytics SDK guide](../Packages/com.serhat.analytics-sdk/README.md).

## PlayFab backend adapter

The provider-neutral backend package has no PlayFab dependency. PlayFab code is compiled only with `PLAYFAB_SDK`.

1. Install a compatible PlayFab Unity SDK.
2. Configure the title ID through PlayFab's project settings or another environment-specific configuration source.
3. Add `PLAYFAB_SDK` to the intended targets.
4. Authenticate the player before making player-scoped calls.
5. Bind `PlayFabCloudFunctionInvoker` as `ICloudFunctionInvoker` in your application composition root.
6. Map provider failures to domain-safe error handling and keep correlation IDs in sanitized logs.

Never include the PlayFab developer secret in a Unity client.

The optional Game API sample contains game-specific progression/economy DTOs. Import it from the Backend SDK package samples and define `SERHAT_FORGE_GAME_API_SAMPLE` only when using it as a reference. Replace its contracts before treating them as your production API.

## Authentication

The Serhat Forge auth assembly requires both `SERHAT_FORGE_AUTH` and `PLAYFAB_SDK`. Without both, it is excluded.

### Shared setup

1. Configure a PlayFab title ID outside reusable framework code where possible.
2. Define `PLAYFAB_SDK` and `SERHAT_FORGE_AUTH` for the intended targets.
3. Verify anonymous/guest fallback policy, account linking, logout, token expiry, cancellation, and recovery.
4. Confirm logs never contain platform tokens, PlayFab session tickets, or identity signatures.

### Android Google Play Games

1. Install and configure a compatible Google Play Games plugin.
2. Configure Play Console application/signing identities.
3. Add `GOOGLE_PLAY_GAMES` in addition to the shared auth symbols.
4. Test first sign-in, cancelled sign-in, revoked access, account switching, linking, and release-signing behavior on a real device.

### iOS Game Center and Keychain

The generic native bridges are under `Assets/Plugins/iOS`. Keychain service naming follows the application identifier.

1. Configure the application identifier, team, provisioning, and capabilities in the downstream game.
2. Add `SERHAT_FORGE_IOS_GAME_SERVICES` only when the iOS postprocessor should add Game Center and Push Notifications capabilities.
3. Test clean install, upgrade, reinstall/keychain persistence policy, Game Center cancellation, identity verification, and account switching on a signed device build.

## DOTween UI and camera helpers

Tween-driven navigation, popup, camera, and scenario helpers are guarded by `DOTWEEN`.

1. Install DOTween and run its setup tool.
2. Add `DOTWEEN` to the intended targets.
3. Reimport and verify all relevant assemblies.
4. Test tween cancellation and object destruction during scene changes.

Without DOTween, guarded components do not compile into the project. Do not attach them to required runtime prefabs unless the integration is mandatory for that game.

## SRDebugger

`DebugActivator` can open SRDebugger when `SRDEBUGGER` is defined. Without it, the fallback UnityEvent remains available.

Keep debug panels and privileged commands out of production builds unless access controls and data-redaction policies are explicitly reviewed.

## Haptics

`HapticHelper` uses Nice Vibrations when `NICE_VIBRATIONS` or the compatible vendor-installed symbol is available. Otherwise it falls back to limited mobile vibration behavior.

Test haptic settings, accessibility preferences, unsupported devices, application pause, and rapid repeated triggers. Never make haptics the only feedback channel.

## Removing an integration

1. Disable its runtime feature/config.
2. Remove its scripting symbols from every target.
3. Remove provider-specific scene components and installer bindings.
4. Remove the provider package/plugin and resolve native dependencies again.
5. Delete environment configuration files only through the appropriate secure process.
6. Clean-reimport and run EditMode, PlayMode, IL2CPP, and device tests.
7. Confirm the safe null/disabled path still works.

See [Troubleshooting](TROUBLESHOOTING.md) when a provider symbol causes compile errors.
