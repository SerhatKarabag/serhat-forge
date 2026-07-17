# Serhat Forge

**A production-minded Unity 6 foundation for starting real games without rebuilding the same infrastructure.**

[English](README.md) · [Türkçe](README.tr.md) · [Getting started](docs/GETTING_STARTED.md) · [Features](docs/FEATURES.md) · [Architecture](docs/ARCHITECTURE.md)

> **Release status:** `0.1.0-preview.1`
>
> Serhat Forge is ready for evaluation and new-project bootstrapping. It gives a game a tested engineering baseline; it does not make that game production-ready automatically. Every enabled platform, service, store, and backend still needs project-specific configuration and release validation.

## Why Serhat Forge?

Most Unity projects need the same non-gameplay work before the first feature can ship: dependency composition, deterministic startup, content loading, persistence, localization, analytics boundaries, CI, and safe integration points. Serhat Forge provides those foundations in one clone while keeping game rules and credentials out of the template.

- **Start with working composition.** Zenject, a project-lifetime composition root, Addressables initialization, and an observable startup pipeline are already connected.
- **Keep failures controlled.** Startup steps support required/optional semantics, timeout, cooperative cancellation, retry, and explicit failure reporting.
- **Build offline-first.** Versioned save/recovery primitives, analytics outbox support, resilient backend utilities, and local Addressables defaults do not assume a permanent network connection.
- **Opt in deliberately.** Third-party integrations remain behaviorally inactive until their provider, environment configuration, and composition-root wiring are present; the bundled Unity IAP package compiles without starting a purchase flow.
- **Own the game.** The template includes no gameplay loop, progression model, economy, store catalog, production credentials, or live-service environment.

## Who is it for?

Serhat Forge fits developers and teams building a Unity game—especially mobile or service-connected titles—who want reusable infrastructure but still want to own their domain architecture.

It is **not** a finished game, a no-code kit, or an SDK bundle that should be copied over an existing project. If you only need one subsystem in an existing game, evaluate the embedded `com.serhat.*` package for that subsystem instead of merging the entire template.

## What is included?

| Foundation | What it gives the project |
|---|---|
| Unity baseline | Unity `6000.3.14f1`, URP `17.3.0`, Input System, Addressables `2.9.1`, Test Framework, and mobile build entry points |
| Composition | Zenject/Extenject `ProjectContext`, project and bootstrap installers, and testable service boundaries |
| Startup | Async Addressables initialization, optional catalog checks, preloading, ordered startup steps, retries, cancellation, and handled failure states |
| Local data | Versioned JSON persistence, migrations, SHA-256 integrity checks, temporary/backup recovery, coordinated transactional restore, and lifecycle save hooks |
| Runtime utilities | Prefab loading, component pooling, feature gates, audio, localization, tutorial/scenario tools, UI/navigation helpers, haptics, camera helpers, and force-update policy |
| Service foundations | Analytics abstraction with persistent outbox; transport-agnostic backend resilience, coalescing, circuit breaking, concurrency limits, and outbox support |
| Optional adapters | Compile-gated authentication, ads, Firebase Analytics, PlayFab, Google Play Games, DOTween, SRDebugger, and native mobile integrations; separately assembled Unity IAP client code that remains unwired by default |
| Quality gates | EditMode/PlayMode composition tests, repository verification, cloud .NET tests, GitHub Actions workflows, and deterministic Android/iOS development build methods |

See the [capability and readiness matrix](docs/FEATURES.md) before deciding which systems to keep or enable.

## Start a game from the template

Do **not** create a blank Unity project first. Serhat Forge is already a complete Unity project, including `Assets`, `Packages`, and `ProjectSettings`.

1. On GitHub, select **Use this template → Create a new repository**.
2. Give the new repository your game's name, then clone that new repository:

   ```bash
   git clone https://github.com/<owner>/<new-game>.git
   ```

3. In Unity Hub, select **Add → Add project from disk** and choose the cloned repository root—the folder containing `Assets`, `Packages`, and `ProjectSettings`.
4. Open it with Unity `6000.3.14f1` and wait for Package Manager resolution and the first import to finish.
5. Run **Tools → Serhat Forge → Setup → Project Settings**. Set the company, product, bundle identifier, version/build numbers, whether the sample scene is first, and mobile IL2CPP defaults.
6. Run **Tools → Serhat Forge → Setup → Repair Zenject Composition**. It is safe to run repeatedly and preserves additional user installers.
7. Open `Assets/Scenes/SampleScene.unity` and enter Play Mode.

A successful first run displays the **Serhat Forge** smoke panel with `Boot state: Ready` and `Content initialized: True`. **Toggle audio mute** should work; the rewarded-ad action remains unavailable until an ad provider is configured. The Console should contain no errors.

Continue with the complete [Getting Started guide](docs/GETTING_STARTED.md), including what to replace, what to keep, and the first production checklist.

## Safe by default

- Ads and provider-specific runtime code are disabled.
- Unity IAP code is compiled because Purchasing is a package dependency, but no store client or purchase service is created until the game wires its catalog, store, and verified backend in the composition root.
- UnityConnect, Purchasing, and Ads automatic initialization are disabled in checked-in settings.
- Remote Addressables catalog build/checks are disabled; local content builds with the player.
- Addressables groups contain no game content, and the remote URL is a non-working placeholder.
- UGS environment IDs, signing data, console IDs, service credentials, and production secrets are absent. Unity's serialized default PS4 passcode is only a public placeholder; the PS4 content/NP title identity remains unset.
- The checked-in application identifier is a placeholder and must be replaced.
- Frame-rate policy is disabled, leaving Unity project settings unchanged.

Safe defaults prevent accidental service calls or secret leakage; they are not substitutes for configuring and testing the services your game enables.

## Documentation

| Guide | Use it for |
|---|---|
| [Getting started](docs/GETTING_STARTED.md) | Creating a repository, opening the project, first-run validation, and turning the sample into a game |
| [Features](docs/FEATURES.md) | Capability scope, default state, readiness level, and project-owned shipping work |
| [Architecture](docs/ARCHITECTURE.md) | Runtime ownership, composition/startup flow, extension points, and repository map |
| [Core systems](docs/CORE_SYSTEMS.md) | Practical recipes for startup, persistence, content, pooling, feature gates, analytics, audio, UI, and localization |
| [Integrations](docs/INTEGRATIONS.md) | SDK prerequisites, scripting symbols, configuration order, and validation for optional providers |
| [CI and release](docs/CI_AND_RELEASE.md) | Repository gates, Unity/GameCI setup, cloud tests, mobile builds, and release checklist |
| [Troubleshooting](docs/TROUBLESHOOTING.md) | Common import, composition, Addressables, integration, and build failures |
| [Upgrading](docs/UPGRADING.md) | Updating a game created from the template without overwriting game-specific work |

Package references:

- [Core](Packages/com.serhat.core-sdk/README.md)
- [Analytics](Packages/com.serhat.analytics-sdk/README.md)
- [Backend](Packages/com.serhat.backend-sdk/README.md)
- [Localization](Packages/com.serhat.localization-sdk/README.md)
- [Monetization](Packages/com.serhat.monetization-sdk/README.md)

Project governance: [Changelog](CHANGELOG.md) · [Contributing](CONTRIBUTING.md) · [Security](SECURITY.md) · [Code of Conduct](CODE_OF_CONDUCT.md)

For non-security help, use the repository's **Usage question** issue form after checking the getting-started and troubleshooting guides. Report vulnerabilities only through the private process in `SECURITY.md`.

## Verify a clone

Run the cross-platform repository gate from the project root:

```powershell
pwsh -File ./Tools/Verify-Repository.ps1
```

Unity tests, cloud tests, Addressables builds, and platform build gates are documented in [CI and release](docs/CI_AND_RELEASE.md). Do not commit generated `Library`, `Temp`, `Logs`, `UserSettings`, `obj`, `.sln`, or Unity-generated `.csproj` files.

## License

Serhat Forge first-party code is available under the [MIT License](LICENSE). Third-party components and imported Unity resources retain their own terms; review [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) before distribution.
