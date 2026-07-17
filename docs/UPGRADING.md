# Upgrading an existing game

A repository created from a GitHub template is an independent project. It does not automatically receive later Serhat Forge changes. Treat the template as a versioned baseline and port updates intentionally.

## Record your baseline

When creating a game, record:

- the Serhat Forge release/tag or commit SHA
- Unity editor version
- embedded `com.serhat.*` package versions
- enabled optional integrations and provider versions
- game-specific modifications made inside framework-owned paths

Keep that record in the downstream game's technical documentation. Without a baseline, upgrade review becomes guesswork.

## Ownership boundaries

| Path | Typical owner | Upgrade approach |
|---|---|---|
| `Assets/Scripts/*` reusable systems | Template, then downstream game | Review file-by-file; preserve intentional game changes |
| `Assets/Plugins/Zenject` | Template/vendor patch | Replace only from a reviewed Serhat Forge release; retain patch notes |
| `Packages/com.serhat.*` | Embedded package | Upgrade as complete package units where possible |
| `Assets/Scenes`, game assets/config | Downstream game | Never overwrite wholesale |
| `ProjectSettings` | Shared/high risk | Diff field-by-field; do not copy blindly |
| `Packages/manifest.json` and lock | Shared/high risk | Merge dependency intent and resolve together |
| `.github`, `Tools` | Template plus repository policy | Port workflow/verifier changes and review permissions |
| `Samples~` and backend references | Optional sample | Replace/remove independently from runtime core |

Avoid long-lived game code modifications inside embedded package folders. Prefer extension interfaces, adapters, installers, and game-owned assemblies. This reduces future merge cost.

## Recommended upgrade flow

1. Ensure the game branch is clean and fully tested.
2. Create a dedicated upgrade branch.
3. Read Serhat Forge `CHANGELOG.md`, release notes, package changelogs, and migration notes between the recorded baseline and target.
4. Compare the target release with the game's current framework files.
5. Port changes in small groups: tooling/policy, package updates, runtime systems, then project settings.
6. Preserve Unity `.meta` files and GUIDs for assets that retain identity.
7. Resolve API changes in game adapters rather than copying old implementation details back into the framework.
8. Open Unity with the target editor and wait for package/import completion.
9. Run the setup/repair commands only as needed; review every resulting settings change.
10. Run the full validation matrix.

Do not merge template and game repositories with `--allow-unrelated-histories` as a routine upgrade strategy. GitHub template repositories may not share useful ancestry, and a wholesale merge can overwrite game-owned assets and settings.

## Embedded package upgrade

For a `Packages/com.serhat.*` package:

1. Read that package's changelog.
2. Replace/copy the complete package on the upgrade branch.
3. Retain its `.meta`, license, README, and package metadata.
4. Update dependent embedded packages in a compatible set.
5. Confirm cross-package dependencies in each `package.json`.
6. Compile with all optional provider assemblies disabled first.
7. Re-enable and test only the integrations used by the game.

If packages are later published to a registry or Git URL, migrate only after immutable tags and a documented package-version policy exist.

## Zenject updates

Serhat Forge currently vendors a patched Extenject/Zenject source tree for deterministic compatibility. The upstream base and local Unity compatibility changes are documented in `Assets/Plugins/Zenject/SERHAT_FORGE_PATCHES.md`.

When updating it:

- use a reviewed upstream commit/tag
- reapply only still-required compatibility patches
- do not install a second Zenject/Extenject copy
- run private-injection and composition tests
- verify `Assets/link.xml`
- run Android and iOS IL2CPP builds

## Project settings and Addressables

Never replace the game's `ProjectSettings` directory wholesale.

Review at minimum:

- application identity and version numbers
- scripting backend and managed stripping
- input handling
- graphics/render pipeline
- quality settings
- build scenes
- platform capabilities and signing
- Unity services linkage
- Addressables settings, profiles, groups, schemas, and remote URLs

Keep production identifiers, service environments, signing data, and secrets game-owned.

For Addressables, preserve game entries and labels. Port schema/default changes deliberately, then run Analyze, a clean content build, and an update/rollback rehearsal if remote content is used.

## Preview-version policy

Serhat Forge `0.x` releases are previews. Semantic versioning is followed where practical, but breaking changes may occur before `1.0.0`.

- Patch: fixes and documentation with no intended public API break.
- Minor: new reusable capability; may include preview migration work.
- Major or `1.0.0`: explicit compatibility boundary.

Read release notes even for patch upgrades. Never depend only on the version number for production risk assessment.

## Validation after upgrade

- repository verifier passes
- Unity Console is clean after a clean import
- all EditMode and PlayMode tests pass
- game-domain regression tests pass
- Addressables Analyze and content build pass
- saved data from the previous production version migrates successfully
- downgrade/future-save behavior is understood
- Android/iOS IL2CPP builds pass
- enabled integrations pass real-device sandbox tests
- backend security and idempotency tests pass
- no production identity or secret was overwritten

Keep the upgrade branch separate until those gates pass. Document any deferred migration or known limitation before merge.

## When to skip an update

Do not upgrade merely because a dependency bot opened a pull request. Defer an update when:

- it is unrelated to a needed fix or capability
- provider/platform compatibility is unknown
- a major version requires an unplanned migration
- the required device or backend validation cannot be performed
- the downstream game is in a release freeze

Security fixes should be triaged immediately, but still require compatibility and regression validation.
