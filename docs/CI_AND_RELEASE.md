# CI and release guide

Serhat Forge ships three GitHub Actions workflows and local equivalents. A green template pipeline proves the reusable baseline; it does not replace game-specific platform, store, backend, or device validation.

## Workflow overview

| Workflow | Trigger | Default behavior | Purpose |
|---|---|---|---|
| Repository validation | Push to `main`, pull request, manual | Runs | Required files, secrets/config, JSON, package dependencies, DI migration, GUIDs, generated files |
| Cloud .NET Tests | Relevant backend paths, pull request, manual | Runs when paths match | .NET 8 release tests with warnings as errors |
| Unity tests | Push to `main`, pull request, manual | Intentionally skipped until enabled | EditMode and PlayMode tests through GameCI |

Actions are pinned to commit SHAs. Dependabot proposes action and NuGet updates; review release notes and CI before merging major upgrades.

## Local prerequisites

- Unity `6000.3.14f1`
- Git
- PowerShell 7 (`pwsh`) for repository validation
- .NET 8 SDK for the optional backend tests
- Android/iOS Unity modules only for those targets

## Local checks

Run repository validation from the root:

```powershell
pwsh -File ./Tools/Verify-Repository.ps1
```

Run cloud tests in release mode:

```powershell
dotnet test "./Samples~/GameApiBackend/tests/Serhat.Forge.CloudScript.Tests.csproj" `
  --configuration Release `
  --property:TreatWarningsAsErrors=true
```

Run Unity tests from **Window > General > Test Runner**:

1. Run all EditMode tests.
2. Run all PlayMode tests.
3. Resolve every unexpected warning/error after a clean import.

## Enable Unity tests on GitHub

Unity tests are gated so a newly generated repository does not fail before its owner configures a Unity license.

1. Open **Repository Settings > Secrets and variables > Actions > Variables**.
2. Create repository variable `RUN_UNITY_CI` with value `true`.
3. In **Actions > Secrets**, configure one supported activation method.

For a Unity Personal license, the workflow accepts:

- `UNITY_LICENSE`
- `UNITY_EMAIL`
- `UNITY_PASSWORD`

For a Unity Professional license, the workflow accepts:

- `UNITY_SERIAL`
- `UNITY_EMAIL`
- `UNITY_PASSWORD`

Follow the current [GameCI activation guide](https://game.ci/docs/github/activation/) for obtaining the correct value for your license type. Store sensitive values as GitHub Actions secrets, never repository variables or committed files. GitHub documents repository variables and secrets separately in its [Actions variables](https://docs.github.com/en/actions/concepts/workflows-and-actions/variables) and [secrets](https://docs.github.com/en/actions/reference/security/secrets) references.

The workflow intentionally skips pull requests from forks because repository secrets are not exposed to them. Review forked changes before running licensed workflows from a trusted branch.

### Expected states

- `RUN_UNITY_CI` missing or not `true`: **Unity tests / skipped** is expected.
- Variable enabled but license secrets missing/invalid: activation fails.
- Correct variable and activation: EditMode and PlayMode jobs run independently.
- Test artifacts and logs are retained for 14 days.

## Deterministic mobile development builds

Set `SERHAT_FORGE_BUILD_PATH`, select the matching build target, and execute the supplied method.

Android example:

```powershell
$env:SERHAT_FORGE_BUILD_PATH = "C:\builds\serhat-forge.apk"
& "C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe" `
  -batchmode -quit `
  -projectPath . `
  -buildTarget Android `
  -executeMethod Serhat.Forge.Editor.SerhatForgeBatchBuild.BuildAndroidDevelopment `
  -logFile "Logs/android-build.log"
```

iOS example:

```powershell
$env:SERHAT_FORGE_BUILD_PATH = "C:\builds\ios-xcode"
& "C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe" `
  -batchmode -quit `
  -projectPath . `
  -buildTarget iOS `
  -executeMethod Serhat.Forge.Editor.SerhatForgeBatchBuild.BuildIosDevelopment `
  -logFile "Logs/ios-build.log"
```

The entry points require at least one enabled Build Settings scene and IL2CPP. Android additionally requires ARM64 and creates a development APK. The iOS result is an Xcode project; compile and sign it on macOS.

Do not commit build output or signing material.

## Pull-request gate

Before merge:

- repository validation passes
- relevant .NET tests pass
- EditMode and PlayMode tests pass locally, and in CI when enabled
- new public behavior includes tests and documentation
- package metadata/changelogs are updated where applicable
- no generated Unity/IDE files are tracked
- no credentials or environment-specific identities are added
- affected mobile/native integrations have an IL2CPP build and device evidence

## Game release gate

Before shipping a downstream game:

1. Perform a clean clone/import on a clean agent.
2. Run repository, EditMode, PlayMode, Addressables Analyze, and content-build checks.
3. Produce Android/iOS IL2CPP builds from the intended release configuration.
4. Compile/sign the iOS Xcode project on macOS.
5. Test authentication, ads, purchases, analytics, deep links, notifications, secure storage, and platform services on real devices when enabled.
6. Run negative backend security tests for signatures, authentication, replay, idempotency, wrong application identity, wrong environment, and sandbox policy.
7. Scan repository and build artifacts for secrets.
8. Verify privacy, consent, store listing, data-safety, signing, symbols, crash reporting, monitoring, and rollback plans.

Template validation is not certification of the downstream game's store, legal, security, or operational readiness.

## Version and release policy

- The current line is preview software and follows semantic versioning where practical.
- Update `CHANGELOG.md` for user-visible behavior.
- Create an immutable Git tag for a published template release.
- Do not advertise an untagged commit as a stable release.
- Keep package versions and repository release notes synchronized when their public API changes.
- Test dependency updates individually or in compatible groups; do not auto-merge major versions.

See [Upgrading](UPGRADING.md) for moving template changes into an existing game.
