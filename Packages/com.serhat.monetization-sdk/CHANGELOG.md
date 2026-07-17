# Changelog

All notable changes to this package are documented here.

## [Unreleased]

- **Breaking configuration:** remove the manual `UNITY_PURCHASING` gate. The provider-independent core and Unity IAP adapter are now separate assemblies that compile with the package; runtime activation requires explicit composition-root wiring instead of a Player Settings symbol.
- Replace deprecated Unity IAP 4 listeners with the Unity IAP 5 `StoreController`/order flow, transaction-exact pending confirmation, coordinated restores, and lifecycle-safe event cleanup.
- Await the Unity IAP confirmation callback before deleting durable recovery state; add bounded/cancellable store operations, late-callback quarantine, and full/partial/empty/failed restore results.
- Keep durable pending purchases in a lifetime recovery loop with persisted, capped exponential backoff instead of abandoning them after a fixed retry count.
- Coalesce duplicate transaction verification, guard observer callbacks, serialize entitlement refreshes, and fail closed for unimplemented active-subscription replacement flows.
- Require a fresh bounded entitlement check before every subscription purchase, failing closed when authoritative state is unavailable.
- Add deterministic Google Play obfuscated-account binding derived from the authenticated player ID, with composition and sample guidance.
- Add deterministic Apple StoreKit UUIDv8 `appAccountToken` binding and remove raw AppReceipt/JWS extraction, persistence, and transport.
- Remove raw purchase tokens from logs/correlation IDs and harden receipt/DTO/pending-store null handling.
- Forward Google Play's purchase token from `Order.Info.TransactionID` instead of sending Unity's unified receipt JSON to the verifier.
- Enable nullable analysis across runtime and sample source without compiler warnings.
- Target the hardened `VerifyPurchase` and `GetEntitlements` functions instead of retired legacy endpoints.
- Align duplicate-purchase and subscription response fields with the hardened server contract.
- Make `PendingPurchaseStore` persist through its injected `IStorage` implementation.
- Replace the stale sample persistence dependency and provide complete, API-accurate setup and lifecycle documentation.
- Preserve PlayFab Economy stack IDs, 64-bit quantities, and expiry in the entitlement contract; provider failures no longer appear as an empty entitlement list.
- Document same-revision sibling package installation for standalone Git-subpath evaluation.

## [0.1.0-preview.1] - 2026-07-17

- Prepared the package for the initial Serhat Forge public preview.
