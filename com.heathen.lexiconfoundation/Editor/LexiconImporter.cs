using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
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
            try { json = File.ReadAllText(ctx.assetPath); }
            catch (Exception e) { ctx.LogImportError($"Failed to read .helex: {e.Message}"); return; }

            var compiled = ScriptableObject.CreateInstance<LexiconCompiledData>();

            try
            {
                var root = JObject.Parse(json);
                compiled.AssetId      = root["assetId"]?.Value<string>() ?? "";
                compiled.AutoRegister = root["registered"]?.Value<bool>() ?? true;
                compiled.Cultures     = root["cultures"]?.ToObject<string[]>() ?? Array.Empty<string>();
                compiled.Entries      = root["entries"] is JObject entries
                    ? ParseEntries(entries, ctx)
                    : Array.Empty<CompiledLexiconEntry>();
            }
            catch (Exception e)
            {
                ctx.LogImportError($"Failed to parse .helex JSON: {e.Message}");
                compiled.Entries  = Array.Empty<CompiledLexiconEntry>();
                compiled.Cultures = Array.Empty<string>();
            }

            if (string.IsNullOrWhiteSpace(compiled.AssetId))
                compiled.AssetId = Path.GetFileNameWithoutExtension(ctx.assetPath);

            ctx.AddObjectToAsset("main", compiled);
            ctx.SetMainObject(compiled);

            if (compiled.AutoRegister)
                LexiconRegistry.Register(compiled);
        }

        private static CompiledLexiconEntry[] ParseEntries(JObject entries, AssetImportContext ctx)
        {
            var result = new List<CompiledLexiconEntry>();
            foreach (var prop in entries.Properties())
            {
                var key = prop.Name.Trim();
                if (string.IsNullOrWhiteSpace(key)) continue;

                if (prop.Value.Type == JTokenType.String)
                {
                    result.Add(new CompiledLexiconEntry
                    {
                        Hash        = LexiconRegistry.Hash(key),
                        Key         = key,
                        Hint        = LexiconHintType.String,
                        StringValue = prop.Value.Value<string>() ?? "",
                    });
                }
                else if (prop.Value is JObject assetObj)
                {
                    var path = assetObj["path"]?.Value<string>();
                    if (path == null)
                    {
                        ctx.LogImportWarning($"Asset entry '{key}' has no 'path' field — skipped.");
                        continue;
                    }
                    var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
                    if (asset == null)
                    {
                        ctx.LogImportWarning($"Asset not found at '{path}' for key '{key}' — skipped.");
                        continue;
                    }
                    ctx.DependsOnSourceAsset(path);
                    result.Add(new CompiledLexiconEntry
                    {
                        Hash       = LexiconRegistry.Hash(key),
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

    [InitializeOnLoad]
    internal static class LexiconCompiledDataRefresh
    {
        static LexiconCompiledDataRefresh() => EditorApplication.delayCall += Refresh;

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
                foreach (var c in data.Cultures) EditorGUILayout.LabelField(c, EditorStyles.miniLabel);
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
