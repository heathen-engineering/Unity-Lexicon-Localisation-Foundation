using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Heathen.Lexicon.Editor
{
    // Imports .helex JSON source files as TextAssets — the .helex file is BOTH the human-authored source AND
    // the shipped runtime data (loaded via Addressables and parsed by LexiconSource at runtime). No compiled
    // ScriptableObject is produced any more.
    //
    // Source format (.helex) — cross-engine compatible with O3DE Lexicon Foundation:
    // {
    //   "assetId":    "UI.Strings",
    //   "registered": true,
    //   "cultures":   ["en-GB"],
    //   "entries": {
    //     "UI.Play": "Play",
    //     "UI.Logo": { "guid": "abc123...", "hint": "Sprite", "sub": "Logo", "path": "Assets/Sprites/Logo.png" }
    //   }
    // }
    //
    // For asset entries "guid" is authoritative at runtime (it is the Addressables address); "path" is kept for
    // human readability and as an editor fallback. On import, a missing/stale "guid" is resolved from "path" and
    // written back into the source file (idempotent — a file whose GUIDs are already correct is not rewritten),
    // so the shipped file always carries the GUIDs the runtime needs. "uuid" is O3DE-specific and ignored.
    [ScriptedImporter(2, "helex")]
    public class LexiconImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            string json;
            try { json = File.ReadAllText(ctx.assetPath); }
            catch (Exception e) { ctx.LogImportError($"Failed to read .helex: {e.Message}"); return; }

            string normalised = json;
            bool   guidsChanged = false;
            string assetId = Path.GetFileNameWithoutExtension(ctx.assetPath);
            string[] cultures = Array.Empty<string>();

            try
            {
                var root = JObject.Parse(json);
                assetId  = string.IsNullOrWhiteSpace(root["assetId"]?.Value<string>())
                    ? Path.GetFileNameWithoutExtension(ctx.assetPath)
                    : root["assetId"].Value<string>();
                cultures = root["cultures"]?.ToObject<string[]>() ?? Array.Empty<string>();

                guidsChanged = NormaliseAssetEntries(root, ctx);
                normalised   = root.ToString(Newtonsoft.Json.Formatting.Indented);
            }
            catch (Exception e)
            {
                // Ship the file as-is so a syntax error does not strip the asset entirely; report the error.
                ctx.LogImportError($"Failed to parse .helex JSON: {e.Message}");
            }

            // The .helex ships as a TextAsset holding the normalised JSON (GUIDs guaranteed present).
            var textAsset = new TextAsset(normalised) { name = Path.GetFileNameWithoutExtension(ctx.assetPath) };
            ctx.AddObjectToAsset("main", textAsset);
            ctx.SetMainObject(textAsset);

            // The Default source is the culture-neutral fallback and may declare no cultures; every other .helex
            // must list at least one, otherwise its entries would be unreachable at runtime.
            bool isDefault = string.Equals(assetId, "default", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(Path.GetFileNameWithoutExtension(ctx.assetPath), "Default", StringComparison.OrdinalIgnoreCase);
            if (!isDefault && cultures.Length == 0)
                ctx.LogImportError(
                    $"[Lexicon] '{Path.GetFileNameWithoutExtension(ctx.assetPath)}.helex' declares no cultures. " +
                    "Every .helex except Default must list at least one culture code, otherwise its entries can " +
                    "never be resolved.");

            // Write the normalised JSON (with resolved GUIDs) back to the source file so it stays in sync. Only
            // when a GUID was actually added/corrected, to avoid reformatting churn and an import loop: the next
            // import finds the GUIDs already present, so guidsChanged is false and nothing is written.
            if (guidsChanged)
            {
                var path = ctx.assetPath;
                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        if (File.ReadAllText(path) == normalised) return;
                        File.WriteAllText(path, normalised);
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    }
                    catch (Exception e) { Debug.LogWarning($"[Lexicon] Could not write GUIDs back to '{path}': {e.Message}"); }
                };
            }

            // Keep the edit-mode registry current so a freshly edited .helex resolves without a domain reload.
            LexiconSourceRefresh.RequestRefresh();
        }

        // Resolves a GUID (and hint) for every asset entry from its path, mutating the JObject in place.
        // Returns true when any GUID was newly set or corrected.
        private static bool NormaliseAssetEntries(JObject root, AssetImportContext ctx)
        {
            if (root["entries"] is not JObject entries) return false;

            bool changed = false;
            foreach (var prop in entries.Properties())
            {
                if (prop.Value is not JObject o) continue; // string entries need no work

                var path = o["path"]?.Value<string>();
                var guid = o["guid"]?.Value<string>();

                if (!string.IsNullOrEmpty(path))
                {
                    ctx.DependsOnSourceAsset(path); // reimport if the referenced asset moves

                    var resolved = AssetDatabase.AssetPathToGUID(path);
                    if (!string.IsNullOrEmpty(resolved) && resolved != guid)
                    {
                        o["guid"] = resolved;
                        guid      = resolved;
                        changed   = true;
                    }
                    else if (string.IsNullOrEmpty(resolved))
                    {
                        ctx.LogImportWarning($"Asset not found at '{path}' for key '{prop.Name}' — it will not resolve at runtime.");
                    }
                }
                else if (string.IsNullOrEmpty(guid))
                {
                    ctx.LogImportWarning($"Asset entry '{prop.Name}' has neither 'path' nor 'guid' — it will not resolve at runtime.");
                }

                // Record the asset type so the runtime need not load the asset to classify it.
                if (string.IsNullOrEmpty(o["hint"]?.Value<string>()) && !string.IsNullOrEmpty(path))
                {
                    var hint = HintFromAsset(AssetDatabase.LoadAssetAtPath<Object>(path));
                    if (hint != LexiconHintType.None) { o["hint"] = hint.ToString(); changed = true; }
                }
            }
            return changed;
        }

        private static LexiconHintType HintFromAsset(Object asset) => asset switch
        {
            AudioClip  _ => LexiconHintType.Sound,
            Texture2D  _ => LexiconHintType.Texture,
            Sprite     _ => LexiconHintType.Sprite,
            GameObject _ => LexiconHintType.Prefab,
            null         => LexiconHintType.None,
            _            => LexiconHintType.Asset,
        };
    }

    // Keeps the edit-mode registry in sync with the project's .helex sources: refreshes on domain reload and,
    // debounced, whenever a .helex is (re)imported. Replaces the former LexiconCompiledData discovery.
    [InitializeOnLoad]
    internal static class LexiconSourceRefresh
    {
        private static bool _queued;

        static LexiconSourceRefresh() => EditorApplication.delayCall += Refresh;

        internal static void RequestRefresh()
        {
            if (_queued) return;
            _queued = true;
            EditorApplication.delayCall += () => { _queued = false; Refresh(); };
        }

        internal static void Refresh() => LexiconRegistry.RefreshEditorSources();
    }
}
