# Changelog

All notable changes to Serhat Forge are documented in this file. The project follows [Semantic Versioning](https://semver.org/) and the structure of [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Added

- Public repository policies, contribution guidance, security reporting, third-party notices, and CI validation scaffolding.
- Zenject/Extenject composition root and migration away from the custom service container.
- Repository verifier for identity, secret, JSON, metadata, and legacy DI checks.
- EditMode and PlayMode composition smoke tests, plus IL2CPP/linker preservation for private Zenject injection and reflection-created providers.
- Deterministic Android/iOS IL2CPP batch-build entry points and CI artifact retention.
- Hardened cloud monetization identity, replay/idempotency, legacy-endpoint shutdown, and negative security tests.

### Changed

- Moved the game-specific Game API backend to an optional reference sample.
- Normalized embedded package metadata and safe template defaults.
- Made Unity Purchasing an explicit setup-wizard opt-in and disabled UnityConnect, Purchasing, and Ads automatic initialization by default.
- Replaced the backend outbox serializer with Unity-supported Newtonsoft.Json so properties, commands, and timestamps persist correctly.
- Standardized the unset UGS environment as `Guid.Empty`, preventing unlinked-template player builds from failing during configuration injection.
- Reduced the default localization catalogs to game-agnostic UI/settings/message keys and added cross-catalog verification.
- Documented the Unity 6 compatibility patch applied to Extenject's generic editor pool reset hooks.
- Disabled remote Addressables catalog generation and made local content build-with-player behavior repository-defined instead of EditorPrefs-dependent.

### Removed

- Reused platform identifiers, Unity Gaming Services environment binding, the legacy Facebook-derived bitcode postprocessor, and unreferenced provenance-unknown editor icons.

## [0.1.0-preview.1] - 2026-07-17

- Prepared the initial public-preview baseline.
