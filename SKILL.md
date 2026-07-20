---
name: heathen-lexicon-unity-foundation
description: Orientation for an agent resolving localised text/sound/assets via dot-path keys, or wiring in a custom source/culture, using Heathen's Lexicon Localisation Foundation for Unity.
---

# Lexicon Localisation Foundation (Unity)

A lightweight, key-driven localisation system. Text and asset references are stored behind
dot-path keys (e.g. `"UI.MainMenu.StartButton"`) hashed to a `ulong` via xxHash3 at registration
time, so every runtime lookup is a dictionary hit with no string allocation. Source data ships as
`.helex` — human-authored JSON that is *also* the exact runtime payload, no separate compile step.
`.helex` is cross-engine compatible with the O3DE and Godot Lexicon Foundations (same schema, same
hash algorithm/seed).

## Tier

**Foundation** — this repo is the free, standalone base layer. The paid **Toolkit** tier
(no-code UI/audio binding components) lives in Heathen's private `SourceRepo`, at
`Unity/ToolkitSource/Assets/Toolkits/com.heathen.lexicontoolkit/` — see that package's own
`SKILL.md` for its surface (`LexiconBindings`, `LexiconKeyBinding`, `LexiconMemberTarget`). This
Foundation owns the registry, culture switching, and the Workbench/Gather/CSV settings page; the
Toolkit is a thin no-code layer on top and requires this Foundation installed.

## Up

[`github.com/heathen-engineering/SourceRepo/SKILL.md`](https://github.com/heathen-engineering/SourceRepo/blob/main/SKILL.md)
(ecosystem guide — this repo has no local engine-level `SKILL.md` to link to, since it ships
standalone).

## Key namespaces / entry points

Namespace: `Heathen.Lexicon`. Package id: `com.heathen.lexiconfoundation` (folder
`com.heathen.lexiconfoundation/` at repo root — that's the UPM package folder installed via
git-URL Package Manager).

| Type | Purpose |
| :--- | :--- |
| `LexiconRegistry` (static class, `Runtime/LexiconRegistry.cs`) | The registry. Culture switching (`UseCulture`/`LoadCulture`, `GetActiveCulture`/`GetDefaultCulture`), resolution (`ResolveString`, `ResolveAsset`, `ResolveSound`, both by dot-path `string` and by pre-hashed `ulong` key), runtime injection/override (`SetString`, `SetAsset`, `SetAssetByGuid`, `RemoveKey`), async asset lifecycle (`AcquireAsset`/`AcquireAssetByGuidAsync`, `ReleaseAsset`/`ReleaseAssetByGuid`), Burst-safe snapshot (`GetStringSnapshot`), and hashing (`Hash(string)`). |
| `LexiconSource` (`Runtime/LexiconSource.cs`) | Parsed, in-memory form of one `.helex` file (asset ID, culture list, entries). Produced by `LexiconSource.Parse` from a `.helex` `TextAsset`, handed to `LexiconRegistry.RegisterParsed`. |
| `LexiconSubsystem` (`Runtime/LexiconSubsystem.cs`) | `[Subsystem(SubsystemScope.Global)]` — Game Framework subsystem owning the registry's session lifecycle (reset + (re)discovery of shipped `.helex` sources at boot). |
| `LexiconAssetLoader` (`Runtime/LexiconAssetLoader.cs`) | Ref-counted Addressables asset seam. `.helex` asset entries carry a GUID (not a live reference); this loader resolves/streams by GUID at runtime and cooperates with `LexiconAddressables` (editor-side) to mark entries addressable at build time. |
| `LexiconText` / `LexiconSound` / `LexiconAsset` / `LexiconSprite` / `LexiconTexture` / `LexiconPrefab` (`Runtime/`) | `[Serializable]` field wrappers, one per common content shape. Each has a `LexiconLocMode`: `Localised` (resolve via key), `Literal` (raw assigned value, default for new fields), `Invariant` (never overridden by a matching registry key). Implicit conversions to their underlying type (`LexiconText → string`, `LexiconSound → AudioClip`, `LexiconAsset → UnityEngine.Object`) via each type's `Resolve()`. |
| `LexiconHintType` (`Runtime/LexiconHintType.cs`) | `None, String, Sound, Texture, Sprite, Prefab, Asset` — tells the registry/drawers which typed resolution path a key expects. |
| Editor: `LexiconSettingsProvider` (`Editor/LexiconSettingsProvider.cs`) | Project Settings page at **Project/Subsystems/Localisation Lexicon**, three tabs: **Workbench** (author/edit keys), **Gather** (`LexiconGatherer` scans scenes/prefabs for untracked strings), **CSV** (`LexiconCsvInterop` import/export). |

## Dependencies

- `com.unity.addressables` (2.9.1), `com.unity.collections` (2.6.2), `com.unity.mathematics`
  (1.2.5), `com.unity.nuget.newtonsoft-json` (3.2.1) — all real UPM deps, listed in
  `com.heathen.lexiconfoundation/package.json`.
- `Heathen.GameFramework` — referenced in both Runtime and Editor asmdefs (Subsystem lifecycle).
  Not UPM-listed; guarded on the Editor asmdef via `defineConstraints: ["HEATHEN_GAMEFRAMEWORK"]`,
  but the **Runtime asmdef has no such gate** — the Runtime assembly hard-fails to compile if
  Game Framework is missing, unlike the Editor half.
- `Heathen.GameplayTags` — referenced in the Runtime asmdef. **Load-bearing, not incidental**:
  `LexiconRegistry.Hash(string)` delegates directly to `GameplayTags.GameplayTag.HashPath(text)`
  rather than hashing independently. This Foundation cannot hash a single key without GameplayTags
  Foundation installed. Neither this nor Game Framework appears in `package.json`'s `dependencies`
  block — both are enforced only at the asmdef-reference level, not via UPM resolution, so install
  both Foundations manually alongside this one.

## Common tasks

- **Resolve a localised string by key**: `LexiconRegistry.ResolveString("UI.MainMenu.StartButton")`,
  or use a `LexiconText` field (implicit `string` conversion) so mode/fallback is handled for you.
- **Resolve a localised sound/generic asset**: `LexiconRegistry.ResolveSound(...)` /
  `.ResolveAsset(...)`, or `LexiconSound`/`LexiconAsset`/`LexiconSprite`/`LexiconTexture`/
  `LexiconPrefab` field wrappers.
- **Switch the active culture at runtime**: `LexiconRegistry.UseCulture("fr-CA")` (BCP 47 code);
  subscribe to `LexiconRegistry.CultureChanged` to react to the switch.
- **Inject/override a value at runtime without a `.helex` entry**: `LexiconRegistry.SetString(...)`
  / `SetAsset(...)` / `SetAssetByGuid(...)`; remove with `RemoveKey(...)`. Overrides live in a
  separate layer re-applied on top of freshly (re)registered sources, so they survive a registry
  reset.
- **Author or bulk-edit keys**: Project Settings → **Project/Subsystems/Localisation Lexicon** →
  Workbench tab (hand-authoring), Gather tab (scan scenes/prefabs for untracked strings), CSV tab
  (spreadsheet round-trip).
- **Add a new `.helex` source**: author the JSON (dot-path key → literal string, or
  `{"path"/"guid","sub","hint"}` for an asset reference), import as a `TextAsset` — `LexiconSource
  .Parse` + `LexiconRegistry.RegisterParsed` pick it up; `LexiconSubsystem` re-discovers shipped
  sources at boot, and `RefreshEditorSources` re-syncs in-editor on import without a domain reload.
- **Manually manage a streamed asset's lifecycle**: `LexiconRegistry.AcquireAsset`/
  `AcquireAssetByGuidAsync` to pin a reference, `ReleaseAsset`/`ReleaseAssetByGuid` to release it —
  normally handled for you by the field wrappers.

## Where full usage docs live

No dedicated KB article URL for this product was found in the README or hub docs at time of
writing — don't guess one. The repo's own `README.md` (root of this repo) is the most complete
current usage writeup, including the full `.helex` schema. General support/community links:
`https://heathen.group/kb/do-more/`, Discord `https://discord.gg/6X3xrRc`.

## Version/changelog

`com.heathen.lexiconfoundation/package.json` (`version` field, currently `1.0.10`) +
`com.heathen.lexiconfoundation/CHANGELOG.md` (currently a single baseline entry, prior history not
itemized).
