## Summary

<!-- What problem does this solve, and why does it belong in the reusable template? -->

## Validation

<!-- List exact repository checks, Unity tests, builds, and device/platform checks performed. -->

- [ ] `pwsh -File ./Tools/Verify-Repository.ps1`
- [ ] Relevant EditMode tests
- [ ] Relevant PlayMode tests
- [ ] Platform/IL2CPP validation when applicable

## Public-template checklist

- [ ] No credentials, signing files, production identifiers, or personal/player data
- [ ] No game-specific domain behavior in the default runtime
- [ ] Third-party SDK references remain isolated in dedicated asmdefs; genuinely optional dependencies remain compile-gated
- [ ] Unity asset moves preserve `.meta` files and GUIDs
- [ ] Tests cover the change and important failure paths
- [ ] Public docs/changelog/package metadata are updated when needed
- [ ] New third-party content has a documented source and compatible license

## Known limitations or follow-up

<!-- Write "None" when there are no known limitations. -->
