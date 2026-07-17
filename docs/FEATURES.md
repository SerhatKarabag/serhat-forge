# Features and readiness

Serhat Forge separates reusable engineering from game-specific decisions. This document shows what exists, what runs in a clean clone, and what still belongs to the game team.

[← README](../README.md) · [Getting started](GETTING_STARTED.md) · [Architecture](ARCHITECTURE.md) · [Core systems](CORE_SYSTEMS.md)

## Readiness labels

- **Active baseline:** wired into the clean project's composition or validation path.
- **Included foundation:** reusable implementation is present, but the game must bind/configure it and define domain policy.
- **Opt-in integration:** disabled or compile-gated until an external SDK, environment configuration, and validation are supplied.
- **Reference only:** an example boundary or server project, not part of the default runtime contract.

“Active baseline” means the template path is usable and tested. It does not mean a store-facing game can ship without its own content, security, device, backend, and release validation.

## Capability matrix

| Capability | What is provided | Clean-clone state | Game-owned work before shipping |
|---|---|---|---|
| Unity baseline | Unity `6000.3.14f1`, URP `17.3.0`, Input System `1.19.0`, Addressables `2.9.1`, Test Framework `1.6.0` | **Active baseline** | Review render/quality/input policies and pin upgrades through tested changes |
| Dependency composition | Zenject/Extenject, `ProjectContext`, `ForgeProjectInstaller`, `ForgeBootstrapInstaller`, project/scene lifetime boundaries | **Active baseline** | Add focused game and scene installers; preserve IL2CPP/reflection rules for new types |
| Application startup | Addressables init, catalog/preload stages, required/optional `StartupStep`, timeout, cancellation, retry, restart, failure reporting | **Active baseline** | Add ordered game steps; define failure UX; make async operations cooperative and idempotent |
| Addressable content | Content manager, prefab loader, handle ownership helpers, labels, local/remote group skeletons | **Active baseline** for empty local content; remote off | Add assets and ownership policy; configure CDN, versioning, fallback, and content rollback before remote use |
| Persistence | Versioned JSON repository, migration chain, checksum, generation selection, `.tmp`/`.bak` recovery, transactional coordinated restore | **Included foundation** | Define DTOs, migrations, participants, encryption/security policy, save size/performance, and domain tests |
| Pooling | Generic prefab-backed `ComponentPool<T>` with prewarm, lease tracking, release, and disposal | **Included foundation** | Choose lifetime/ownership, reset pooled state, profile capacities, and release Addressables handles |
| Feature gates | Progress/external-condition checks, runtime overrides, sparse IDs, persistent seen state, fail-closed external conditions | **Included foundation** | Define game feature IDs/catalog, authoritative progress source, entitlement source, and migration policy |
| Audio | `SoundManager` contracts for music/SFX/mute/volume plus `NullAudioService` | **Active baseline** with default local service | Supply clips/mixers, persistence, lifecycle behavior, platform audio policy, and game-facing facade as needed |
| Localization | Runtime locale switching, fallback chain, formatting, plural rules, TMP component, CSV import, `en`/`tr` starter data | **Included foundation** | Initialize before use, replace starter keys, maintain locale parity, choose provider, and test fonts/layouts |
| UI and navigation | Loading components, safe-area helpers, press feedback, popup/page/swipe navigation, dynamic scaling, pinned UI effects | **Included foundation** | Establish the game's screen lifecycle, navigation state, accessibility, art, and performance budgets |
| Tutorial and scenario tools | Scriptable tutorial steps/signals and extensible scenario command runner | **Included foundation** | Define domain commands, persistence/resume rules, cancellation, authoring conventions, and tests |
| Haptics and camera helpers | Native/fallback haptics and reusable camera/UI helpers | **Included foundation**; enhanced provider opt-in | Set accessibility/preferences, platform policy, provider SDK, and device validation |
| Analytics | Provider abstraction, validation, serialized dispatch, batching, user properties, persistent offline queue/outbox, event helpers | **Included foundation** | Define event schema/consent/retention, select provider, configure environments, and validate delivery/privacy |
| Backend client core | Transport abstractions, retry, circuit breaker, concurrency limiting, request coalescing, persistent outbox, telemetry | **Included foundation** | Implement domain contracts/transport/auth, idempotency, observability, rate/error policy, and security tests |
| PlayFab adapter | Compile-gated cloud-function transport | **Opt-in integration** | Install/configure PlayFab SDK, enable symbol, map environments, and test authentication/errors |
| Authentication | Orchestrator boundaries, secure-storage adapters, PlayFab service, Game Center and Google Play Games providers | **Opt-in integration** | Install SDKs, configure platform credentials/capabilities, account recovery/linking, consent, and device tests |
| Ads | `IAdService`, safe `NullAdService`, Google Mobile Ads adapter and inspector support | **Opt-in integration**; ads off | Install/configure SDK, consent/privacy, unit IDs per environment, reward idempotency, lifecycle and device tests |
| IAP and monetization | Unity IAP 5 store boundary, callback-awaited confirmation, pending recovery, account-bound backend verification, paginated quantity-aware entitlements, signed refund reconciliation, partial restore results, tier validation | **Opt-in runtime integration**; package compiled, service unwired; active subscription replacement and ambiguous partial refunds fail closed | Configure protected pending storage, catalog and credentials, enable only deployed stores, deploy the hardened backend, then validate receipts/webhooks/recovery on real devices; implement replacement and custom quantity policy end to end before exposing them |
| Firebase Analytics | Compile-gated Firebase provider and event mapper | **Opt-in integration** | Install Firebase, add per-app config outside the template baseline, consent, schemas, and delivery tests |
| Native iOS services | Keychain/Game Center bridges and optional Xcode capability postprocessing | **Opt-in integration** | Set bundle/capabilities/signing, configure Apple services, compile/sign Xcode on macOS, and test real devices |
| Mobile development builds | Deterministic Android ARM64 IL2CPP APK and iOS IL2CPP Xcode export entry points | **Included foundation**; runnable when modules are installed | Add signing/distribution, icons/capabilities, release hardening, store builds, and device tests |
| Repository and cloud CI | Secret/config/static verifier plus .NET 8 cloud tests | **Active baseline** in GitHub Actions | Keep checks green; add domain and deployment gates |
| Unity CI | GameCI EditMode/PlayMode workflow with artifacts | **Opt-in CI** | Set `RUN_UNITY_CI=true`, configure Unity license secrets, review security for fork PRs, and add build jobs |
| Game API backend sample | Azure Functions/PlayFab-style progression/economy reference in `Samples~/GameApiBackend` | **Reference only** | Remove or replace contracts, data, authorization, economy, deployment, and operations for the actual game |
| Monetization server sample | Store verification/webhook reference in `cloudscript-azure-functions-monetization` | **Reference only** | Supply store trust/config, secret management, deployment, monitoring, negative security tests, and ownership |

## Foundation details

### Composition and startup

The clean project has one application-lifetime `ProjectContext`. `ForgeProjectInstaller` binds generic configuration, content, prefab loading, audio, ads fallback, and loading-screen fallback. `ForgeBootstrapInstaller` binds the persistent `GameBootstrapper`.

The bootstrapper is orchestration only. It initializes content and executes configured startup steps; it should not become the location for game rules or every service binding. Add application services through focused project installers and scene services through `SceneContext` installers.

Startup steps are explicitly ordered and marked required or optional. Required failures stop readiness; optional failures are reported and boot continues. Timeout uses cooperative cancellation—operations that ignore cancellation can still consume resources after the user-facing timeout, so every custom step must stop cleanly.

### Persistence and offline behavior

The persistence foundation solves serialization lifecycle and recovery mechanics without choosing the game's data model. It supports:

- explicit schema/data versions and ordered migrations;
- SHA-256 corruption detection;
- primary, temporary, and backup generation recovery;
- serialized single-writer coordination;
- capture/restore participants; and
- fail-closed transactional rollback contracts.

The checksum is not confidentiality, authentication, or anti-cheat. Sensitive data and authoritative economy state need an appropriate security/backend design. Large saves need project-specific profiling.

### Content and asset lifetime

Addressables starts with empty `Local_Core` and `Remote_Default` groups and the labels `core`, `gameplay`, `ui`, and `audio`. The default performs local initialization, allows offline boot, has no preload labels, does not check a remote catalog, and builds Addressables with the player.

`IContentManager` owns content operations; `IPrefabLoader` owns loaded prefab handles. Code that loads outside these services must still make ownership and release explicit. The placeholder `https://YOUR_CDN/...` URL must never reach a release configuration.

### Presentation utilities

Audio, UI, safe-area, loading, navigation, tutorial/scenario, camera, haptic, and thumbnail helpers are deliberately modular. Adopt only the pieces that fit the game. They are implementation building blocks, not a mandatory presentation architecture or finished UI kit.

### Service boundaries

Analytics and backend packages keep domain code independent from a specific vendor. Optional Firebase and PlayFab implementations are isolated behind scripting symbols and assembly definitions. This reduces vendor coupling, but the game must still define privacy, authentication, event naming, idempotency, observability, and environment ownership.

Purchasing follows the same rule: the client can initiate and recover purchases, but the authoritative grant/entitlement decision belongs to a hardened backend. Never grant durable currency or entitlements solely from an unverified client callback.

## Deliberately not included

Serhat Forge does not define:

- gameplay, player progression, level content, economy, or inventory;
- UI art, store catalog, ad unit IDs, analytics taxonomy, or live-ops configuration;
- production UGS/Firebase/PlayFab/store environments or credentials;
- signing certificates, provisioning profiles, keystores, or console records;
- an authoritative anti-cheat/security model;
- deployment, monitoring, on-call, privacy, or compliance policy; or
- automatic propagation of future template changes into generated games.

This is intentional. Those decisions are product- and organization-specific and should be reviewed as part of the game, not inherited invisibly.

## Choosing what to use

For a primarily offline game, a sensible minimum is composition/startup, local Addressables, persistence, pooling, audio, localization, repository verification, and Unity tests. Remove unused cloud samples and keep remote/provider symbols disabled.

For a connected mobile game, add integrations incrementally in this order:

1. environment and secret ownership;
2. authentication and secure storage;
3. backend transport/resilience and domain APIs;
4. analytics consent and schema;
5. remote content policy;
6. ads; and
7. purchases with server-authoritative verification.

Each addition should include its own failure UX, offline behavior, telemetry, tests, and release gate. Follow [Integrations](INTEGRATIONS.md) and [CI and release](CI_AND_RELEASE.md).
