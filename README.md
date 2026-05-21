# Lexicon Localisation Foundation

![License](https://img.shields.io/badge/License-Apache_2.0-blue?style=flat-square)
![Maintained](https://img.shields.io/badge/Maintained%3F-yes-green?style=flat-square)
![Unity](https://img.shields.io/badge/Unity-6%20%2B-black?style=flat-square&logo=unity&logoColor=white)

A lightweight, key-driven localisation system for Unity. Text and asset references are stored behind dot-path keys hashed to `ulong` via xxHash3 — zero runtime string cost after registration, with a Burst-safe snapshot API and a three-mode per-field fallback chain.

-----

## Become a GitHub Sponsor
[![Discord](https://img.shields.io/badge/Discord--1877F2?style=social&logo=discord)](https://discord.gg/6X3xrRc)
[![GitHub followers](https://img.shields.io/github/followers/heathen-engineering?style=social)](https://github.com/heathen-engineering?tab=followers)  
Support Heathen by becoming a [GitHub Sponsor](https://github.com/sponsors/heathen-engineering). Sponsorship directly funds the development and maintenance of free tools like this, as well as our game development [Knowledge Base](https://heathen.group/) and community on [Discord](https://discord.gg/6X3xrRc).

Sponsors also get access to our private SourceRepo, which includes developer tools for O3DE, Unreal, Unity, and Godot.  
Learn more or explore other ways to support @ [heathen.group/kb](https://heathen.group/kb/do-more/)

-----

## What it does

Lexicon Localisation Foundation provides a structured, culture-aware field system built on four core types:

| Type | Purpose |
|------|---------|
| **`LexiconData`** | A `ScriptableObject` that holds key/value entries for one or more BCP 47 culture codes |
| **`LexiconRegistry`** | Static registry that manages active culture, fallback chain, and resolves keys to values |
| **`LexiconText`** | A serializable text field with Localised / Literal / Invariant mode |
| **`LexiconSound`** | A serializable audio-clip field with the same three modes |
| **`LexiconAsset`** | A serializable Unity Object reference field with the same three modes |

Keys follow a dot-path convention (e.g. `"UI.MainMenu.StartButton"`). At registration time each key is hashed to a `ulong` via xxHash3 — all runtime lookups are dictionary hits with no string allocations.

The following features are included:

- **Modes** — Every field (`LexiconText`, `LexiconSound`, `LexiconAsset`) carries one of three modes:
  - `Localised` — resolves via the registry using the active culture, falls back to default culture, then the Default data asset.
  - `Literal` — returns the raw string / clip / asset directly; never touches the registry.
  - `Invariant` — same as Literal but signals to tooling that the value must never be translated.
- **Culture fallback chain** — Active culture → default culture → the `Default` data asset. All three layers are checked before returning `null`.
- **BCP 47 culture codes** — Each `LexiconData` asset declares the culture codes it covers (e.g. `"en"`, `"en-GB"`, `"fr"`, `"ja"`). Multiple cultures per asset are supported.
- **Auto-registration** — `LexiconData` assets in a `Resources` folder with `autoRegister = true` are discovered and registered automatically at startup. The asset named `Default` is always registered first and acts as the unconditional last-resort fallback.
- **Burst support** — `LexiconRegistry.GetStringSnapshot(Allocator)` returns a caller-owned `NativeHashMap<ulong, FixedString512Bytes>` safe to pass into Burst-compiled jobs. Rebuild on `CultureChanged`.
- **Hint types** — Entries carry a `LexiconHintType` (`String`, `Sound`, `Texture`, `Sprite`, `Prefab`, `Asset`) so the editor can display the correct control and validate references.
- **Editor tools** — `LexiconEditorWindow` (`Window > Heathen > Lexicon`) provides a full data editor with per-culture views, validation (`LexiconValidator`), and CSV import/export (`LexiconCsvInterop`).
- **Project Settings panel** — `Project Settings > Lexicon` lists all registered data assets and the active / default culture.

---

## Requirements

- Unity **6000.0** or compatible
- `com.unity.collections` **2.6.2**
- `com.unity.mathematics` **1.2.5**
- `com.heathen.gameplaytagsfoundation` **1.0.0** (hashing shared with GameplayTags)

---

## Installation

### Via Unity Package Manager (UPM)

1. In Unity, go to `Window > Package Manager`.
2. Click **+** > **Add package from git URL**.
3. Enter:
   ```
   https://github.com/heathen-engineering/Unity-Lexicon-Localisation-Foundation.git?path=/com.heathen.lexiconfoundation
   ```

Dependencies (`com.unity.collections`, `com.unity.mathematics`, and `com.heathen.gameplaytagsfoundation`) are resolved automatically by UPM.

-----

## Setup & Workflow

### 1. Create a Culture Data asset

Right-click in the Project window and choose **Create > Heathen > Lexicon > Culture Data**. Set an `Asset ID` (used as a display-name key, e.g. `"en-GB"`) and list the BCP 47 culture codes it covers. For a project-wide fallback, name the asset `Default`.

### 2. Define keys and values

Open **Window > Heathen > Lexicon** to edit your data asset. Add entries using dot-path keys:

```
UI.MainMenu.StartButton   →  "Start Game"
UI.MainMenu.QuitButton    →  "Quit"
Audio.UI.ButtonClick      →  <AudioClip reference>
```

### 3. Use localised fields in code

```csharp
using Heathen.Lexicon;

// Attach a LexiconText to any serializable class or MonoBehaviour
[SerializeField] private LexiconText _buttonLabel = new();

// In the Inspector, set Mode = Localised and KeyOrValue = "UI.MainMenu.StartButton"
// Then at runtime:
string label = _buttonLabel.Resolve(); // returns active-culture string or raw value as fallback
```

### 4. Switch culture at runtime

```csharp
LexiconRegistry.LoadCulture("fr");  // fires CultureChanged event
```

### 5. Burst jobs

```csharp
using Unity.Collections;

// Get a culture snapshot for Burst-compatible access
NativeHashMap<ulong, FixedString512Bytes> snapshot =
    LexiconRegistry.GetStringSnapshot(Allocator.TempJob);

// Rebuild when culture changes:
LexiconRegistry.CultureChanged += _ => RebuildSnapshot();

// ... schedule jobs, then:
snapshot.Dispose();
```

-----

## API Reference

### `LexiconRegistry`

| Member | Description |
|--------|-------------|
| `Register(data)` | Add a `LexiconData` asset to the registry |
| `Unregister(data)` | Remove a data asset and rebuild the culture index |
| `LoadCulture(cultureCode)` | Set the active culture; fires `CultureChanged` |
| `SetDefaultCulture(cultureCode)` | Set the default fallback culture; fires `DefaultCultureChanged` |
| `GetActiveCulture()` | Returns the current active culture code |
| `GetAvailableCultureCodes()` | All registered culture codes |
| `ResolveString(dotPath)` | Resolve a dot-path key to a string in the active culture |
| `ResolveAsset(dotPath)` | Resolve a dot-path key to a `UnityEngine.Object` |
| `ResolveSound(dotPath)` | Resolve a dot-path key to an `AudioClip` |
| `Hash(dotPath)` | Hash a dot-path to `ulong` (xxHash3) |
| `GetStringSnapshot(allocator)` | Burst-safe `NativeHashMap<ulong, FixedString512Bytes>`; caller disposes |
| `CultureChanged` | `event Action<string>` fired when the active culture changes |
| `DefaultCultureChanged` | `event Action<string>` fired when the default culture changes |

### `LexiconText`

| Member | Description |
|--------|-------------|
| `Mode` | `LexiconLocMode`: `Localised`, `Literal`, or `Invariant` |
| `KeyOrValue` | The dot-path key (Localised) or raw string (Literal/Invariant) |
| `Resolve()` | Returns the active-culture string, or `KeyOrValue` as fallback |
| `GetHash()` | Cached `ulong` hash of `KeyOrValue`; recomputed on change |
| `IsLocalised` / `IsLiteral` / `IsInvariant` | Convenience mode checks |

### `LexiconSound`

| Member | Description |
|--------|-------------|
| `Mode` | `LexiconLocMode`: `Localised`, `Literal`, or `Invariant` |
| `Key` | The dot-path key (Localised mode) |
| `LiteralClip` | The direct `AudioClip` reference (Literal/Invariant mode) |
| `Resolve()` | Returns the active-culture clip, or `LiteralClip` as fallback |

### `LexiconAsset`

| Member | Description |
|--------|-------------|
| `Mode` | `LexiconLocMode`: `Localised`, `Literal`, or `Invariant` |
| `Hint` | `LexiconHintType` — advises editor which asset type to expect |
| `Key` | The dot-path key (Localised mode) |
| `LiteralAsset` | The direct `UnityEngine.Object` reference (Literal/Invariant mode) |
| `Resolve()` | Returns the active-culture asset, or `LiteralAsset` as fallback |

`LexiconHintType` values: `None`, `String`, `Sound`, `Texture`, `Sprite`, `Prefab`, `Asset`

-----

## Namespaces

| Namespace | Contents |
|-----------|----------|
| `Heathen.Lexicon` | All runtime types: `LexiconData`, `LexiconRegistry`, `LexiconText`, `LexiconSound`, `LexiconAsset`, `LexiconLocMode`, `LexiconHintType` |
| `Heathen.Lexicon.Editor` | Editor-only: `LexiconEditorWindow`, `LexiconDataEditor`, `LexiconCsvInterop`, `LexiconValidator`, custom drawers |
