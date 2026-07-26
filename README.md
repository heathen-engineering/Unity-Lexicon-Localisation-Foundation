> **Moved to Codeberg:** this repo now lives at [codeberg.org/Heathen-Engineering/Unity-Lexicon-Localisation-Foundation](https://codeberg.org/Heathen-Engineering/Unity-Lexicon-Localisation-Foundation) — please use that copy going forward. This GitHub copy will remain live for now while Heathen's Pro Toolkits finish migrating to our private Git server; once that's complete, this GitHub repo will be archived.

# Lexicon Localisation Foundation

![License](https://img.shields.io/badge/License-Apache_2.0-blue?style=flat-square)
![Maintained](https://img.shields.io/badge/Maintained%3F-yes-green?style=flat-square)
![Unity](https://img.shields.io/badge/Unity-6%20%2B-%23313131?style=flat-square&logo=unity&logoColor=white)

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

Lexicon Localisation Foundation resolves localised text and assets from `.helex` files — human-authored JSON that is *also* the shipped runtime data, no separate compile step. A `.helex` file is imported by a `ScriptedImporter` straight into a `TextAsset`; at runtime that `TextAsset` is parsed and its entries are indexed into the registry. There is no `LexiconData`/`LexiconCompiledData` ScriptableObject pipeline — that architecture was retired.

| Type | Purpose |
|------|---------|
| **`LexiconSource`** | The parsed, in-memory form of one `.helex` file (asset ID, culture list, entries). Produced by `LexiconSource.Parse` from a `.helex` `TextAsset` and handed to the registry. |
| **`LexiconRegistry`** | Static registry that indexes parsed sources per culture, resolves keys to values, and exposes the runtime-injection (`SetString`/`SetAsset`/`SetAssetByGuid`) and Burst-snapshot APIs. |
| **`LexiconSubsystem`** | A Game Framework Global subsystem that owns the registry's session lifecycle — resets and (re)discovers `.helex` sources at boot, replacing the registry's former ad-hoc `RuntimeInitializeOnLoadMethod` bootstrap. |
| **`LexiconAssetLoader`** | The shared, reference-counted Addressables asset seam. Asset entries in a `.helex` carry a GUID rather than a live reference (a bare GUID string in JSON is invisible to Unity's build dependency walker, so this loader/`LexiconAddressables` mark it addressable at build time and stream it in by GUID at runtime). Every Heathen tool that ships GUID-addressed content routes through this loader. |
| **`LexiconText` / `LexiconSound` / `LexiconAsset` / `LexiconSprite` / `LexiconTexture` / `LexiconPrefab`** | Serializable field wrappers, one per common content shape, each carrying a Localised / Literal / Invariant mode. |

Keys follow a dot-path convention (e.g. `"UI.MainMenu.StartButton"`). At registration time each key is hashed to a `ulong` via xxHash3 — all runtime lookups are dictionary hits with no string allocations.

### The `.helex` file

A `.helex` file is JSON, cross-engine compatible with O3DE Lexicon Foundation. It is both the human-authored source (edited via the Workbench, CSV import, or by hand) and the exact payload shipped in the build:

```json
{
  "assetId":    "UI.Strings",
  "registered": true,
  "cultures":   ["en-GB"],
  "entries": {
    "UI.Play": "Play",
    "UI.Logo": { "guid": "abc123...", "hint": "Sprite", "sub": "Logo", "path": "Assets/Sprites/Logo.png" }
  }
}
```

String entries are a bare JSON string. Asset entries are an object: `guid` is authoritative at runtime (it is the Addressables address); `path` is kept for human readability and as an editor-only fallback (resolved back to a GUID by `AssetDatabase` if missing); `sub` names a sub-asset (e.g. a named sprite in a sheet); `hint` records the content type so the runtime need not load the asset to classify it. `uuid` is an O3DE-specific field and is ignored on Unity. The importer (`LexiconImporter`) fills in and normalises missing GUIDs on import and writes them back to the source file.

A `.helex` whose `assetId` is `"default"` (or whose filename is `Default`) is the culture-neutral last-resort fallback — it may declare no cultures. Every other `.helex` must declare at least one culture code, or its entries can never be reached.

The following features are included:

- **Modes** — Every field (`LexiconText`, `LexiconSound`, `LexiconAsset`, `LexiconSprite`, `LexiconTexture`, `LexiconPrefab`) carries one of three modes:
  - `Localised` — resolves via the registry using the active culture, falling back through base language, default culture, then the Default source.
  - `Literal` — returns the raw string / clip / asset directly; never touches the registry. The default mode for new fields.
  - `Invariant` — same as Literal but signals to tooling that the value must never be translated.
- **Culture fallback chain** — Exact active culture → base language of the active culture (e.g. `fr` from `fr-CA`) → exact default culture → base language of the default culture → the always-present `Default` source, indexed for O(1) lookup. All tiers are checked before returning `null`.
- **BCP 47 culture codes** — Each `.helex` file declares the culture codes it covers (e.g. `"en"`, `"en-GB"`, `"fr"`, `"ja"`). Multiple cultures per file are supported.
- **Delivery via Addressables, not Resources** — `LexiconBuildProcessor` marks every `.helex` `TextAsset` (and every asset it references by GUID) addressable before each player build. At runtime, `LexiconRegistry.LoadShippedSourcesAsync` streams every `.helex` carrying the `"lexicon"` Addressables label with a single labelled load; in the editor, sources resolve synchronously via `AssetDatabase` so play-in-editor needs no built Addressables catalogue.
- **Streamed assets are reference-counted** — Asset entries carrying a GUID (rather than a directly injected reference) are not loaded until acquired via `LexiconRegistry.AcquireAsset`/`AcquireAssetByGuidAsync`, and are freed once every `Release` call balances it — bounding memory for large voice-over/asset bodies of content regardless of total size.
- **Burst support** — `LexiconRegistry.GetStringSnapshot(Allocator)` returns a caller-owned `NativeHashMap<ulong, FixedString512Bytes>` of the active culture's string entries, safe to pass into Burst-compiled jobs. Rebuild on `CultureChanged`.
- **Hint types** — Entries carry a `LexiconHintType` (`String`, `Sound`, `Texture`, `Sprite`, `Prefab`, `Asset`) so the editor can display the correct control and the importer can validate references.
- **Editor tools** — `Project Settings > Subsystems > Localisation Lexicon` (`LexiconSettingsProvider`) is the single editor surface for authoring, with three modes:
  - **Workbench** — a key tree + per-culture table for adding/removing keys, editing values, and adding culture files as extra columns.
  - **Gather** (`LexiconGatherer`) — scans open scenes and project prefabs for `LexiconText` fields left in Literal mode, lets you confirm and assign keys, then commits the literal values into the Default `.helex` and patches the scanned fields to Localised mode.
  - **CSV** (`LexiconCsvInterop`) — export/import a single file or all files at once, text-only or including asset paths.
  - Import-time validation happens in `LexiconImporter` itself: a missing culture declaration or an asset entry with neither `path` nor `guid` raises an import error/warning, not a separate validator class.
- **Optional DOTS/Entities support** — `LexiconStringBlob` (compiled behind `UNITY_ENTITIES`) is a sorted, binary-searchable blob of hash→string entries plus an `IComponentData` wrapper, for Burst ECS systems that want localised strings without going through the managed registry.

---

## Requirements

- Unity **6000.0** or compatible
- Package Manager (UPM) dependencies, resolved automatically:
  - `com.unity.addressables` **2.9.1**
  - `com.unity.collections` **2.6.2**
  - `com.unity.mathematics` **1.2.5**
  - `com.unity.nuget.newtonsoft-json` **3.2.1**
- **Not** UPM dependencies — hard assembly references that must be installed separately (same pattern as Foundation for Steamworks: an editor-script compile guard, not a `package.json` entry):
  - **Heathen Game Framework** (`com.heathen.gameframework`) — Lexicon's runtime and editor assemblies both reference it directly; `LexiconSubsystem` is a Game Framework `Subsystem`. On first load, if it isn't installed, a dialog under `Help > Heathen > Lexicon Foundation > Install Game Framework` offers to add it automatically.
  - **GameplayTags Foundation** (`com.heathen.gameplaytags`) — Lexicon's `Hash()` calls `GameplayTags.GameplayTag.HashPath()` directly, so the two hashing implementations must stay aligned. Install alongside Lexicon.

---

## Installation

### Via Unity Package Manager (UPM)

1. In Unity, go to `Window > Package Manager`.
2. Click **+** > **Add package from git URL**.
3. Enter:
   ```
   https://github.com/heathen-engineering/Unity-Lexicon-Localisation-Foundation.git?path=/com.heathen.lexiconfoundation
   ```

`com.unity.addressables`, `com.unity.collections`, `com.unity.mathematics`, and `com.unity.nuget.newtonsoft-json` are resolved automatically by UPM. **Game Framework and GameplayTags Foundation are not UPM dependencies of this package** and must be added separately (their own git URLs, or via the install prompt described above) before Lexicon's assemblies will compile.

-----

## Setup & Workflow

### 1. Open the Lexicon settings panel

Go to `Project Settings > Subsystems > Localisation Lexicon`. If no `.helex` file exists yet, opening the panel creates `Assets/Settings/Default.helex` for you — the culture-neutral fallback file every project needs.

### 2. Define keys and values

In the **Workbench** tab, add keys with the `+` field (dot-path convention, e.g. `UI.MainMenu.StartButton`), set each key's type (String, Sound, Texture, Sprite, Prefab, Asset), and fill in the Default column. Use **New Culture File…** to add a culture-specific `.helex` (e.g. `fr-CA.helex`) and add it as a table column to translate against the same key set. Alternatively, use the **CSV** tab to export/import in bulk, or hand-edit the `.helex` JSON directly.

### 3. Use localised fields in code

```csharp
using Heathen.Lexicon;

// Attach a LexiconText to any serializable class or MonoBehaviour
[SerializeField] private LexiconText _buttonLabel = new();

// In the Inspector, set Mode = Localised and KeyOrValue = "UI.MainMenu.StartButton"
// Then at runtime:
string label = _buttonLabel.Resolve(); // returns active-culture string or raw value as fallback
```

`LexiconSound`, `LexiconAsset`, `LexiconSprite`, `LexiconTexture`, and `LexiconPrefab` follow the same shape (`Mode` + `Key`/`KeyOrValue` + a literal fallback field + `Resolve()`).

### 4. Switch culture at runtime

```csharp
LexiconRegistry.UseCulture("fr");  // fires CultureChanged event
// LexiconRegistry.LoadCulture("fr") is a back-compat alias for UseCulture
```

### 5. Streaming asset content

Asset entries authored with a GUID (rather than a directly injected reference) are not resident until acquired — useful for holding a bounded window of large content (e.g. voice-over) resident regardless of total project size:

```csharp
await LexiconRegistry.AcquireAsset("VO.Line042");   // streams it in (Addressables in a player, AssetDatabase in the editor)
var clip = LexiconRegistry.ResolveSound("VO.Line042");
// ... when the line is no longer needed:
LexiconRegistry.ReleaseAsset("VO.Line042");          // unloaded once every acquire is balanced by a release
```

### 6. Burst jobs

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
| `UseCulture(cultureCode)` | Set the active culture; fires `CultureChanged` |
| `LoadCulture(cultureCode)` | Back-compat alias for `UseCulture` |
| `GetActiveCulture()` | Returns the current active culture code |
| `GetDefaultCulture()` | Returns the default (fallback) culture code — seeded automatically from the first registered source's first declared culture; there is no explicit setter |
| `GetMappedCultureCodes()` | All culture codes with at least one registered entry |
| `GetAvailableAssetIds()` | Asset IDs of all currently registered `.helex` sources |
| `GetDisplayName(assetId)` | Resolves `Language.{assetId}` in the active culture, falling back to `assetId` |
| `ResolveString(dotPath)` / `ResolveString(hash)` | Resolve a dot-path key (or pre-hashed key) to a string in the active culture |
| `ResolveAsset(dotPath)` / `ResolveAsset(hash)` | Resolve a dot-path key to a `UnityEngine.Object` |
| `ResolveSound(dotPath)` / `ResolveSound(hash)` | Resolve a dot-path key to an `AudioClip` |
| `ResolveAssetByGuid(guid, subAssetName)` | Resolve an asset directly by GUID via the Addressables seam, for baked/JSON content with no live reference |
| `AcquireAsset(dotPath)` / `AcquireAssetByGuidAsync(guid)` | Reference-counted stream-in of an asset entry; balance with `ReleaseAsset`/`ReleaseAssetByGuid` |
| `ReleaseAsset(dotPath)` / `ReleaseAssetByGuid(guid)` | Release one reference acquired above |
| `SetString(dotPath, value, cultureCode)` | Inject/overwrite a runtime string entry; survives source rebuilds |
| `SetAsset(dotPath, asset, cultureCode)` | Inject/overwrite a runtime entry with a direct asset reference |
| `SetAssetByGuid(dotPath, guid, hint, subAssetName, cultureCode)` | Inject/overwrite a runtime entry that streams by GUID, same shape a compiled `.helex` source registers |
| `RemoveKey(dotPath, cultureCode)` | Remove a runtime-injected entry, from one culture or all |
| `Hash(dotPath)` | Hash a dot-path to `ulong` (XXH3, via `GameplayTags.GameplayTag.HashPath`) |
| `GetStringSnapshot(allocator)` | Burst-safe `NativeHashMap<ulong, FixedString512Bytes>` of active-culture strings; caller disposes |
| `RegisteredSourceCount` | Number of registered `.helex` sources, for diagnostics |
| `CultureChanged` | `event Action<string>` fired when the active culture changes |
| `DefaultCultureChanged` | `event Action<string>` fired when the default culture is first established |

### `LexiconText`

| Member | Description |
|--------|-------------|
| `Mode` | `LexiconLocMode`: `Localised`, `Literal`, or `Invariant` |
| `KeyOrValue` | The dot-path key (Localised) or raw string (Literal/Invariant) |
| `Resolve()` | Returns the active-culture string, or `KeyOrValue` as fallback |
| `GetHash()` | Cached `ulong` hash of `KeyOrValue`; recomputed on change |
| `IsLocalised` / `IsLiteral` / `IsInvariant` | Convenience mode checks |

### `LexiconSound` / `LexiconSprite` / `LexiconTexture` / `LexiconPrefab`

Same shape as `LexiconText`, one per content type (`AudioClip`, `Sprite`, `Texture2D`, `GameObject` respectively):

| Member | Description |
|--------|-------------|
| `Mode` | `LexiconLocMode`: `Localised`, `Literal`, or `Invariant` |
| `Key` | The dot-path key (Localised mode) |
| `LiteralClip` / `LiteralSprite` / `LiteralTexture` / `LiteralPrefab` | The direct reference (Literal/Invariant mode), and the fallback in Localised mode |
| `Resolve()` | Returns the active-culture value, or the literal reference as fallback |

### `LexiconAsset`

Like the above, but for a generic `UnityEngine.Object` reference:

| Member | Description |
|--------|-------------|
| `Mode` | `LexiconLocMode`: `Localised`, `Literal`, or `Invariant` |
| `Hint` | `LexiconHintType` — advises the editor drawer which asset type to expect |
| `Key` | The dot-path key (Localised mode) |
| `LiteralAsset` | The direct `UnityEngine.Object` reference (Literal/Invariant mode) |
| `Resolve()` | Returns the active-culture asset, or `LiteralAsset` as fallback |

`LexiconHintType` values: `None`, `String`, `Sound`, `Texture`, `Sprite`, `Prefab`, `Asset`

-----

## Namespaces

| Namespace | Contents |
|-----------|----------|
| `Heathen.Lexicon` | Runtime types: `LexiconRegistry`, `LexiconSource`, `LexiconEntry`, `LexiconAssetLoader`, `LexiconSubsystem`, `LexiconText`, `LexiconSound`, `LexiconAsset`, `LexiconSprite`, `LexiconTexture`, `LexiconPrefab`, `LexiconLocMode`, `LexiconHintType`, and (behind `UNITY_ENTITIES`) `LexiconStringBlob`/`LexiconStringBlobComponent` |
| `Heathen.Lexicon.Editor` | Editor-only: `LexiconImporter` (the `.helex` `ScriptedImporter`), `LexiconSettingsProvider` (Workbench/Gather/CSV), `LexiconGatherer`, `LexiconCsvInterop`, `LexiconBuildProcessor`, `LexiconAddressables`, `LexiconDataEditor`, `LexiconSubsystemSettingsPage`, `LexiconMenuItems` (Game Framework install prompt, in its own dependency-free bootstrap assembly), and the field property drawers |
