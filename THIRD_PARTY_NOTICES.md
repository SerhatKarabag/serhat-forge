# Third-Party Notices

Serhat Forge's MIT License applies to first-party code only. The components below retain their upstream copyrights and license terms. Keep adjacent license/attribution files when redistributing vendored content.

## Vendored source and assets

| Component | Version/source | License and local notice |
|---|---|---|
| [Extenject (Zenject)](https://github.com/Mathijs-Bakker/Extenject) | 9.2.1, base commit 8d8cc2ca14189b3efe91e19f41d1ae89cf44bf8a, vendored core source with documented Unity 6 compatibility patch | MIT; `Assets/Plugins/Zenject/LICENSE.txt` |
| TextMesh Pro Essential Resources / Unity UI | Unity UI 2.0.0 | Unity Companion License; Unity package license applies |
| Liberation Sans | Bundled with TextMesh Pro resources | SIL Open Font License 1.1; `Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt` |

No trademark rights are granted by the Serhat Forge license. Remove optional sample art/fonts you do not need rather than republishing them as original project assets.

## Unity Package Manager dependencies

Versions and immutable Git revisions are recorded in `Packages/manifest.json` and `Packages/packages-lock.json`.

| Component | Declared source/version | License |
|---|---|---|
| [UIEffect](https://github.com/mob-sakai/UIEffect) | 5.10.8, commit 70937d2ce39b61c29c4feb4d13642c65bb553d6c | MIT |
| [Particle Effect for UGUI](https://github.com/mob-sakai/ParticleEffectForUGUI) | Commit 92fb173507ece2c135c38733deeca82a6d49cacd | MIT |
| [External Dependency Manager for Unity](https://github.com/googlesamples/unity-jar-resolver) | 1.2.187 | Apache License 2.0 |
| Unity registry and built-in packages | Versions in the package lock | Unity package-specific terms, commonly the Unity Companion License |

Each downloaded package carries its authoritative license file. This notice does not replace those terms.

## Direct .NET/NuGet dependencies

The optional Azure Functions projects declare their direct dependencies in their `.csproj` files. They include Microsoft Azure/.NET packages (MIT), Google APIs Auth (Apache License 2.0), PlayFab C# SDK (upstream license), and test-only xUnit/Moq/Microsoft.NET.Test.Sdk packages (upstream licenses). NuGet package contents and metadata contain the authoritative license text and exact transitive dependency set.

## Embedded Serhat packages

Each `Packages/com.serhat.*` package contains its own `LICENSE.md`. Third-party provider SDKs referenced by optional adapters are not vendored by default and remain governed by their own licenses when a downstream project installs them.
