# Changelog

All notable changes to Serhat Forge are documented in this file. The project follows [Semantic Versioning](https://semver.org/) and the structure of [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Added

- Public landing pages in English and Turkish, plus getting-started, feature/readiness, architecture, core-systems, integrations, CI/release, troubleshooting, and upgrading guides.
- API-accurate package and sample guides for analytics, localization, backend, and monetization, including complete lifecycle and production checklists.
- A usage-question issue form and CI validation for required documentation and relative Markdown links.
- Client/server contract and pending-purchase persistence tests in the release cloud test graph.
- Public repository policies, contribution guidance, security reporting, third-party notices, and CI validation scaffolding.
- Zenject/Extenject composition root and migration away from the custom service container.
- Repository verifier for identity, secret, JSON, metadata, and legacy DI checks.
- EditMode and PlayMode composition smoke tests, plus IL2CPP/linker preservation for private Zenject injection and reflection-created providers.
- Deterministic Android/iOS IL2CPP batch-build entry points and CI artifact retention.
- Hardened cloud monetization identity, replay/idempotency, legacy-endpoint shutdown, and negative security tests.

### Changed

- **Breaking configuration:** removed the manual `UNITY_PURCHASING` gate and setup-wizard toggle. The monetization core and Unity IAP adapter now compile as separate assemblies whenever the package is installed; games activate purchasing only by wiring the store, catalog, verified backend, and service lifecycle in their composition root.
- Migrated the store adapter from deprecated Unity IAP 4 listeners to the Unity IAP 5 order API, including transaction-exact confirmation, deferred-order handling, restart recovery, restore coordination, and correct Google purchase-token forwarding.
- Hardened monetization confirmation and recovery with callback-awaited store confirmation, bounded cancellation-aware operations, duplicate-verification coalescing, partial restore reporting, token-safe telemetry, and fail-closed subscription replacement.
- Made pending-purchase recovery continuous for the service lifetime, with persisted capped backoff and no fixed retry-count abandonment.
- Hardened the monetization backend with canonical Google purchase-token identities, authenticated account ownership binding, immutable grant snapshots, renewable processing leases, retryable crash recovery, and complete multi-item subscription revocation.
- Hardened Apple verification with deterministic StoreKit account binding, revoked/expired/type/quantity checks, retry-aware App Store API failures, and transaction-ID-only data minimization.
- Added signed Apple refund/revocation reconciliation for one-time purchases and subscriptions, with canonical idempotent revokes and fail-closed partial-quantity handling.
- Migrated Google subscription verification to `purchases.subscriptionsv2`, added fail-closed state/error mapping, and redacted purchase tokens from automatically collected dependency telemetry.
- Hardened Google one-time purchases against product/token mismatch, multi-quantity grants, and already-refunded quantities.
- Made PlayFab Economy entitlement reads use title entity tokens, complete pagination, actual stack quantities/expiry, and explicit upstream failures instead of false empty inventories.
- Added independent Apple/Google store enable flags so single-platform production deployments do not require unused provider credentials.
- Aligned the monetization client with the hardened `VerifyPurchase` and `GetEntitlements` functions, completed response DTO fields, propagated cancellation, and made Google package identity deployment-owned.
- Made `PendingPurchaseStore` honor its injected storage and replaced non-existent sample dependencies with package-owned implementations.
- Made the Game API package sample independent from project-level clock types and corrected its stale API example.
- Expanded Unity CI activation to accept either Personal-license or Professional-license secrets.
- Clarified the pre-public private vulnerability reporting gate and community support path.
- Moved the game-specific Game API backend to an optional reference sample.
- Normalized embedded package metadata and safe template defaults.
- Accepted only Unity's public serialized PS4 placeholder while continuing to reject custom console passcodes and identities.
- Kept UnityConnect, Purchasing, and Ads automatic initialization disabled; purchasing remains inactive until explicitly composed and configured by the game.
- Replaced the backend outbox serializer with Unity-supported Newtonsoft.Json so properties, commands, and timestamps persist correctly.
- Standardized the unset UGS environment as `Guid.Empty`, preventing unlinked-template player builds from failing during configuration injection.
- Reduced the default localization catalogs to game-agnostic UI/settings/message keys and added cross-catalog verification.
- Documented the Unity 6 compatibility patch applied to Extenject's generic editor pool reset hooks.
- Disabled remote Addressables catalog generation and made local content build-with-player behavior repository-defined instead of EditorPrefs-dependent.

### Removed

- Reused platform identifiers, Unity Gaming Services environment binding, the legacy Facebook-derived bitcode postprocessor, and unreferenced provenance-unknown editor icons.

## [0.1.0-preview.1] - 2026-07-17

- Prepared the initial public-preview baseline.
