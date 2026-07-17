# Serhat Localization SDK

Project-agnostic localization for Unity with runtime locale switching, fallback chains, plural rules, culture-aware formatting, CSV import, and TextMeshPro integration.

## Capabilities

- Runtime locale switching with a persisted player preference
- Region-to-language-to-default fallback (`en-US` -> `en` -> configured default)
- English, Turkish, and Russian plural rules
- JSON string tables generated from CSV
- `StreamingAssets` and `Resources` providers
- `LocalizedTMPText` updates when the locale changes
- Android/WebGL-compatible `StreamingAssets` loading

## Required startup sequence

Localization does not bootstrap itself. Call and await `Loc.InitializeAsync` once before reading strings or displaying localized components. The `Auto Initialize` field on the settings asset is configuration data; it does not replace this explicit startup call in the current preview.

```csharp
using System;
using Serhat.Localization;
using UnityEngine;

public sealed class LocalizationBootstrap : MonoBehaviour
{
    public bool IsReady { get; private set; }

    private async void Awake()
    {
        try
        {
            await Loc.InitializeAsync();
            IsReady = true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            enabled = false;
        }
    }
}
```

Keep gameplay/UI startup behind this initialization step so the first frame never renders localization keys.

## Setup

### 1. Create settings

In Unity, select **Tools > Serhat > Localization > Create Settings**. This creates:

```text
Assets/Resources/LocalizationSettings.asset
```

Configure:

- **Default Locale**: for example `en`
- **Supported Locales**: for example `en`, `tr`, `en-US`
- **Use System Language**: uses the closest supported locale on first launch
- **Provider Type** and **Data Path**

Provider locations for the default `Localization/Locales` data path are:

| Provider | Expected files |
|---|---|
| `StreamingAssets` | `Assets/StreamingAssets/Localization/Locales/en.json` |
| `Resources` | `Assets/Resources/Localization/Locales/en.json` |

Do not include `.json` in the configured data path.

### 2. Prepare CSV

```csv
key,en,tr
ui.play,Play,Oyna
welcome.message,Hello {0}!,Merhaba {0}!
items.count.one,{0} item,{0} oge
items.count.other,{0} items,{0} oge
```

Rules:

- The first column is `key`; remaining headers are locale codes.
- Plural rows use `.zero`, `.one`, `.two`, `.few`, `.many`, or `.other` suffixes.
- Lines beginning with `#` and empty lines are ignored.
- Quote CSV values that contain commas; escape a quote as `""`.
- Include the configured default locale column or import fails.

### 3. Generate JSON

Select **Tools > Serhat > Localization > Import CSV** and choose the CSV. The importer writes one JSON file per locale to the provider/data path configured above.

Commit the generated JSON files. Validate every supported locale in a player build; `StreamingAssets` access differs by platform.

Generated files use simple values and nested plural forms:

```json
{
  "ui.play": "Play",
  "welcome.message": "Hello {0}!",
  "items.count": {
    "one": "{0} item",
    "other": "{0} items"
  }
}
```

## Runtime usage

Only call these APIs after initialization:

```csharp
string playLabel = Loc.Get("ui.play");
string greeting = Loc.Format("welcome.message", playerName);
string itemLabel = Loc.Plural("items.count", itemCount, itemCount);

await Loc.SetLocaleAsync("tr");
```

Prefer `SetLocaleAsync` and await it. `SetLocale` starts the asynchronous load without exposing completion, so it is unsuitable when the next UI operation depends on the new table.

Subscribe and unsubscribe with the same owner:

```csharp
private void OnEnable()
{
    Loc.OnLocaleChanged += HandleLocaleChanged;
}

private void OnDisable()
{
    Loc.OnLocaleChanged -= HandleLocaleChanged;
}

private void HandleLocaleChanged(object sender, LocaleChangedEventArgs args)
{
    RefreshView();
}
```

## TextMeshPro

Add `LocalizedTMPText` to an object with a `TMP_Text` component, then set its key. For plural text, enable plural count and update `PluralCount` at runtime.

The component intentionally does nothing before `Loc.IsInitialized`. Initialize localization before enabling the first localized scene, or call `UpdateText` after your startup gate opens.

## Fallback and missing keys

For `en-US`, resolution is:

1. `en-US`
2. `en`, when it is supported
3. configured default locale
4. the key itself, with a missing-key warning

Keep the default locale complete and treat missing-key warnings as content validation failures in development builds.

## Sample

Import **Basic Usage** from Package Manager and follow its included `README.md`. The sample CSV must still be imported into your configured provider path before running the sample scene/UI.

## Requirements

- Unity 6000.3 or newer
- TextMeshPro 5.0.0 or newer
