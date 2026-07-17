# Contributing to Serhat Forge

Thanks for helping improve the template. Keep changes project-agnostic, safe by default, and small enough to review.

## Before you start

- Use Unity `6000.3.14f1`.
- Do not commit credentials, signing files, service-account files, generated Unity folders, IDE projects, or build output.
- Open an issue before introducing a required third-party SDK, changing the minimum Unity version, or expanding the public backend surface.
- Preserve `.meta` files and GUIDs when moving Unity assets.
- Put game-specific features in `Samples~` or a separate repository, not in the reusable runtime core.

## Local checks

Run repository validation from the project root:

```powershell
pwsh -File ./Tools/Verify-Repository.ps1
```

For changes to the Game API backend reference:

```powershell
dotnet test ./Samples~/GameApiBackend/tests/Serhat.Forge.CloudScript.Tests.csproj
```

For Unity changes, run all EditMode and PlayMode tests from **Window > General > Test Runner**. Changes that affect mobile/native boundaries also require the relevant IL2CPP build and a device smoke test.

## Code expectations

- Prefer clear composition roots and constructor/injected dependencies over global service location.
- Keep optional SDK references behind dedicated asmdefs and define constraints.
- Propagate cancellation through async work and observe tasks that outlive a timeout.
- Avoid hidden allocations in hot paths; document ownership for pooled objects, Addressables handles, subscriptions, and disposable resources.
- Add regression tests for behavior changes and negative tests for security boundaries.
- Update README/changelog/package metadata when public behavior or requirements change.

## Pull requests

A pull request should explain the problem, the chosen design, affected platforms, validation performed, and any known limitation. Screenshots or short recordings are welcome for visible editor/UI changes.

By contributing, you agree that your contribution is licensed under the repository's MIT License unless a file explicitly states different terms.
