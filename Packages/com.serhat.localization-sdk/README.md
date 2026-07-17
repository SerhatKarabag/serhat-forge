# Serhat Localization SDK

A preview, project-agnostic localization package for Unity with runtime language switching, fallback chains, pluralization, formatting, and TextMeshPro integration.

## Features

- **Runtime Language Switching**: Change languages instantly at runtime
- **Fallback Chain**: en-US -> en -> default locale -> key
- **Pluralization**: Support for English, Turkish, and Russian plural rules
- **Formatting**: Culture-aware number and date formatting
- **TextMeshPro Integration**: Automatic text updates on locale change
- **Multiple Providers**: StreamingAssets and Resources support
- **Editor Tools**: CSV import with validation
- **IL2CPP Safe**: No heavy reflection, mobile-ready

## Installation

The SDK is already installed in the Packages folder.

## Quick Start

### 1. Create Settings Asset
**Tools > Serhat > Localization > Create Settings**

This creates a `LocalizationSettings` asset in `Assets/Resources/`.

### 2. Configure Settings
- Set **Default Locale** (e.g., "en")
- Add **Supported Locales** (e.g., "en", "tr")
- Choose **Provider Type** (StreamingAssets or Resources)

### 3. Prepare Localization Data

#### CSV Format
```csv
key,en,tr
ui.play,Play,Oyna
ui.settings,Settings,Ayarlar
items.count.one,{0} item,{0} oge
items.count.other,{0} items,{0} oge
```

### 4. Import Data
**Tools > Serhat > Localization > Import CSV**

### 5. Use in Code
```csharp
using Serhat.Localization;

// Simple get
string text = Loc.Get("ui.play");

// With formatting
string formatted = Loc.Format("welcome.message", playerName);

// Pluralization
string items = Loc.Plural("items.count", itemCount, itemCount);

// Change locale
await Loc.SetLocaleAsync("tr");

// Listen for changes
Loc.OnLocaleChanged += (sender, args) => UpdateUI();
```

### 6. Use with TextMeshPro
Add `LocalizedTMPText` component to your TextMeshPro objects:
- Set the **Key** field
- Optionally enable **Use Plural Count** for pluralization

## Data Schema

### CSV Format
- First column: `key`
- Additional columns: locale codes (e.g., `en`, `tr`)
- Plural keys use suffixes: `key.one`, `key.other`, `key.few`, `key.many`
- Comments start with `#`
- Empty lines are ignored

### JSON Format
```json
{
  "simple.key": "Simple value",
  "plural.key": {
    "one": "One item",
    "other": "{0} items"
  }
}
```

## Requirements

- Unity 6000.3 or newer
- TextMeshPro 5.0.0 or newer
