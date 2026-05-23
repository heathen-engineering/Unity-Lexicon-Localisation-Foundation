using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Heathen.Lexicon.Editor
{
    // Compiles .helex JSON source files into LexiconCompiledData sub-assets.
    //
    // Source format (.helex) — cross-engine compatible with O3DE Lexicon Foundation:
    // {
    //   "assetId":    "UI.Strings",
    //   "registered": true,
    //   "cultures":   ["en-GB"],
    //   "entries": {
    //     "UI.Play": "Play",
    //     "UI.Logo": { "uuid": "...", "path": "Assets/Sprites/Logo.png" }
    //   }
    // }
    //
    // "uuid" is O3DE-specific — ignored in Unity.
    // "path" is the Unity project-relative asset path for asset-type entries.
    // "registered" defaults to true when omitted.
    // All keys are hashed with XXH3 (seed 0) — identical to O3DE at runtime.
    [ScriptedImporter(1, "helex")]
    public class LexiconImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            string json;
            try
            {
                json = File.ReadAllText(ctx.assetPath);
            }
            catch (Exception e)
            {
                ctx.LogImportError($"Failed to read .helex: {e.Message}");
                return;
            }

            var compiled = CreateInstance<LexiconCompiledData>();

            try
            {
                using var doc  = JsonDocument.Parse(json);
                var       root = doc.RootElement;

                compiled.AssetId = root.TryGetProperty("assetId", out var idEl) &&
                                   idEl.ValueKind == JsonValueKind.String
                    ? idEl.GetString()
                    : Path.GetFileNameWithoutExtension(ctx.assetPath);

                // "registered" defaults to true (every helex file should register)
                compiled.AutoRegister = !root.TryGetProperty("registered", out var regEl) ||
                                        regEl.ValueKind != JsonValueKind.False;

                var cultures = new List<string>();
                if (root.TryGetProperty("cultures", out var cultEl) &&
                    cultEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in cultEl.EnumerateArray())
                        if (c.ValueKind == JsonValueKind.String)
                            cultures.Add(c.GetString());
                }
                compiled.Cultures = cultures.ToArray();

                if (compiled.AutoRegister &&
                    root.TryGetProperty("entries", out var entriesEl) &&
                    entriesEl.ValueKind == JsonValueKind.Object)
                {
                    compiled.Entries = BuildEntries(entriesEl, ctx);
                }
                else
                {
                    compiled.Entries = Array.Empty<CompiledLexiconEntry>();
                }
            }
            catch (Exception e)
            {
                ctx.LogImportError($"Failed to parse .helex JSON: {e.Message}");
                compiled.Entries  = Array.Empty<CompiledLexiconEntry>();
                compiled.Cultures = Array.Empty<string>();
            }

            ctx.AddObjectToAsset("main", compiled);
            ctx.SetMainObject(compiled);

            if (compiled.AutoRegister)
                LexiconRegistry.Register(compiled);
        }

        private static CompiledLexiconEntry[] BuildEntries(JsonElement entriesEl, AssetImportContext ctx)
        {
            var result = new List<CompiledLexiconEntry>();

            foreach (var prop in entriesEl.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(prop.Name)) continue;
                var key  = prop.Name.Trim();
                var hash = LexiconRegistry.Hash(key);

                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    result.Add(new CompiledLexiconEntry
                    {
                        Hash        = hash,
                        Key         = key,
                        Hint        = LexiconHintType.String,
                        StringValue = prop.Value.GetString() ?? "",
                    });
                }
                else if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    // Asset entry — "path" is Unity project-relative path; "uuid" is O3DE-only.
                    if (!prop.Value.TryGetProperty("path", out var pathEl) ||
                        pathEl.ValueKind != JsonValueKind.String)
                    {
                        ctx.LogImportWarning($"Asset entry '{key}' has no 'path' field — skipped.");
                        continue;
                    }

                    var assetPath = pathEl.GetString();
                    var asset     = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                    if (asset == null)
                    {
                        ctx.LogImportWarning($"Asset not found at '{assetPath}' for key '{key}' — skipped.");
                        continue;
                    }

                    ctx.DependsOnSourceAsset(assetPath);
                    result.Add(new CompiledLexiconEntry
                    {
                        Hash       = hash,
                        Key        = key,
                        Hint       = HintFromAsset(asset),
                        AssetValue = asset,
                    });
                }
                else
                {
                    ctx.LogImportWarning($"Entry '{key}' has unrecognised value type — skipped.");
                }
            }

            return result.ToArray();
        }

        private static LexiconHintType HintFromAsset(Object asset) => asset switch
        {
            AudioClip  _ => LexiconHintType.Sound,
            Texture2D  _ => LexiconHintType.Texture,
            Sprite     _ => LexiconHintType.Sprite,
            GameObject _ => LexiconHintType.Prefab,
            _            => LexiconHintType.Asset,
        };
    }

    // Refreshes the editor-time registry from all compiled .helex assets on load
    // and after any asset import. Mirrors LexiconDataEditor's [InitializeOnLoad] pattern.
    [InitializeOnLoad]
    internal static class LexiconCompiledDataRefresh
    {
        static LexiconCompiledDataRefresh()
        {
            EditorApplication.delayCall += Refresh;
        }

        internal static void Refresh()
        {
            var guids  = AssetDatabase.FindAssets("t:LexiconCompiledData");
            var assets = new List<LexiconCompiledData>(guids.Length);
            foreach (var guid in guids)
            {
                var asset = AssetDatabase.LoadAssetAtPath<LexiconCompiledData>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.AutoRegister) assets.Add(asset);
            }
            // Register "default" assets first so they become the unconditional fallback
            foreach (var a in assets) if ( LexiconRegistry.IsDefaultCompiledAsset(a)) LexiconRegistry.Register(a);
            foreach (var a in assets) if (!LexiconRegistry.IsDefaultCompiledAsset(a)) LexiconRegistry.Register(a);
        }
    }

    [CustomEditor(typeof(LexiconCompiledData))]
    internal class LexiconCompiledDataEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var data = (LexiconCompiledData)target;

            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Compiled from",
                AssetDatabase.GetAssetPath(data), EditorStyles.miniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("Asset ID", data.AssetId ?? "");
            EditorGUILayout.Toggle("Auto Register", data.AutoRegister);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Cultures", EditorStyles.boldLabel);
            if (data.Cultures == null || data.Cultures.Length == 0)
            {
                EditorGUILayout.HelpBox("No cultures defined.", MessageType.Warning);
            }
            else
            {
                EditorGUI.BeginDisabledGroup(true);
                foreach (var c in data.Cultures)
                    EditorGUILayout.LabelField(c, EditorStyles.miniLabel);
                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Compiled Entries", EditorStyles.boldLabel);

            if (data.Entries == null || data.Entries.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No entries — set \"registered\": true and add entries to the .helex source.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField($"{data.Entries.Length} entries", EditorStyles.miniLabel);
            EditorGUI.BeginDisabledGroup(true);
            foreach (var entry in data.Entries)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(entry.Key, GUILayout.ExpandWidth(true));
                EditorGUILayout.LabelField(entry.Hint.ToString(), GUILayout.Width(60));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.EndDisabledGroup();
        }
    }
}
