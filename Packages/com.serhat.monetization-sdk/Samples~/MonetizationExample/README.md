# Monetization Example

This sample demonstrates `IProductCatalogMapping`, `ITierPolicy`, Unity IAP wiring, purchase UI callbacks, entitlement updates, and pending-purchase persistence.

It is an integration reference, not a production store configuration.

## Before running

1. Import this sample through Package Manager. The package installs Unity IAP `5.2.0` and the sample assembly references the adapter directly; no scripting symbol is required.
2. Replace every `com.example.game.*` product ID in `ExampleProductCatalog` with products configured for your store sandbox.
3. Add `ExamplePurchaseUsage` to a scene object and assign its optional button/text references.
4. Use a store test account and a development build.

The sample compiles when imported but does not activate purchases by itself. `ExamplePurchaseUsage` creates and initializes the store client only when you add that component to an active scene object. Its `localDevelopmentPlayerId` is intentionally a local placeholder. Production code must derive the Google obfuscated account ID from the stable authenticated PlayFab title-player ID before store initialization. The component owns its manually constructed service and disposes it from `OnDestroy`; production composition should provide the same single-instance lifetime through Zenject. The included `MockMonetizationBackendClient` accepts fake verification only in the Unity Editor or a development build. It returns a fail-closed error in release players.

Do not use that mock while validating real receipt security or shipping a game. Replace it with `MonetizationBackendClientBuilder` connected to the separate hardened monetization Function App before performing production-like tests.

## What to replace

- Product IDs and catalog metadata
- Subscription tier names and validation policy
- A project-specific subscription replacement flow before enabling upgrades or downgrades
- Mock backend construction
- Local development player identity and Google account binding source
- UI and player-facing error mapping
- Post-purchase inventory/entitlement reconciliation

## Required production behavior

- Register and invoke `VerifyPurchase` and `GetEntitlements` from the hardened Function App.
- Never use the retired gameplay-backend endpoints `IapVerify`, `IapGetEntitlements`, or `GrantPurchaseRewards`.
- Grant gameplay value only after server verification.
- Keep store receipts and secrets out of logs.
- Use protected/encrypted pending-receipt storage in production; the sample's file storage is plaintext.
- Treat cancellation as cancelling the local wait only; late native store callbacks still require reconciliation.
- Test process termination after store approval but before verification; the pending store must recover that transaction on restart.
