# Basic Usage sample

This sample provides English/Turkish source data and a `LanguageSwitcher` component. It does not modify project settings or copy localization data automatically.

## Run it

1. Import **Basic Usage** from Package Manager.
2. Select **Tools > Serhat > Localization > Create Settings**.
3. Add `en` and `tr` to **Supported Locales** and keep one of them as **Default Locale**.
4. Select **Tools > Serhat > Localization > Import CSV** and choose the imported `Data/Localization.csv` file. This generates JSON under the provider path configured by the settings asset.
5. Create a Canvas with:
   - a `TMP_Dropdown` for language selection;
   - two `TMP_Text` fields for welcome and item-count text;
   - a `Slider` for the item count.
6. Add `LanguageSwitcher` to a scene object and assign those references.
7. Enter Play Mode. `LanguageSwitcher.Start` awaits `Loc.InitializeAsync` before reading any key.

The files under `Data/Locales` are reference output. Runtime providers do not read them from the imported sample folder unless you deliberately configure/copy them into a supported `StreamingAssets` or `Resources` path.

## Production notes

- Move localization initialization into your application startup flow instead of relying on a sample scene component.
- Keep one owner for locale-change subscriptions and release them during teardown.
- Test device builds for every target platform, especially when using `StreamingAssets`.
- Replace the sample ASCII Turkish text with reviewed UTF-8 translations for a shipped game.
