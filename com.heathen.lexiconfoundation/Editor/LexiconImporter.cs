using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
                ParseHelex(json, ctx, compiled);
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

        private static void ParseHelex(string json, AssetImportContext ctx, LexiconCompiledData compiled)
        {
            compiled.AutoRegister = true; // .helex default
            compiled.Cultures     = Array.Empty<string>();
            compiled.Entries      = Array.Empty<CompiledLexiconEntry>();

            int i = 0;
            JsonScanner.SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '{') return;
            i++; // skip root '{'

            while (i < json.Length)
            {
                JsonScanner.SkipWs(json, ref i);
                if (i >= json.Length || json[i] == '}') break;
                if (json[i] == ',') { i++; continue; }
                if (json[i] != '"') { i++; continue; }

                var key = JsonScanner.ReadString(json, ref i);
                JsonScanner.SkipWs(json, ref i);
                if (i >= json.Length || json[i] != ':') continue;
                i++; // skip ':'
                JsonScanner.SkipWs(json, ref i);

                switch (key)
                {
                    case "assetId":
                        compiled.AssetId = JsonScanner.ReadString(json, ref i) ?? "";
                        break;

                    case "registered":
                        compiled.AutoRegister = JsonScanner.ReadBool(json, ref i, true);
                        break;

                    case "cultures":
                        compiled.Cultures = JsonScanner.ReadStringArray(json, ref i);
                        break;

                    case "entries":
                        compiled.Entries = ParseEntries(json, ref i, ctx);
                        break;

                    default:
                        JsonScanner.SkipValue(json, ref i);
                        break;
                }
            }
        }

        private static CompiledLexiconEntry[] ParseEntries(string json, ref int i, AssetImportContext ctx)
        {
            var result = new List<CompiledLexiconEntry>();

            JsonScanner.SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '{') return result.ToArray();
            i++; // skip '{'

            while (i < json.Length)
            {
                JsonScanner.SkipWs(json, ref i);
                if (i >= json.Length || json[i] == '}') { i++; break; }
                if (json[i] == ',') { i++; continue; }
                if (json[i] != '"') { i++; continue; }

                var key = JsonScanner.ReadString(json, ref i);
                if (string.IsNullOrWhiteSpace(key)) continue;
                key = key.Trim();

                JsonScanner.SkipWs(json, ref i);
                if (i >= json.Length || json[i] != ':') continue;
                i++; // skip ':'
                JsonScanner.SkipWs(json, ref i);
                if (i >= json.Length) break;

                if (json[i] == '"')
                {
                    var value = JsonScanner.ReadString(json, ref i);
                    result.Add(new CompiledLexiconEntry
                    {
                        Hash        = LexiconRegistry.Hash(key),
                        Key         = key,
                        Hint        = LexiconHintType.String,
                        StringValue = value ?? "",
                    });
                }
                else if (json[i] == '{')
                {
                    var path = JsonScanner.ExtractStringProp(json, ref i, "path");
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
                    JsonScanner.SkipValue(json, ref i);
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

    // Minimal JSON scanner for .NET Standard 2.0. Handles the subset of JSON used in .helex files.
    internal static class JsonScanner
    {
        public static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n'))
                i++;
        }

        // Read a JSON string. i must point at opening '"'. Advances i past closing '"'.
        public static string ReadString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') return null;
            i++; // skip opening '"'
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\')
                {
                    i++;
                    if (i < s.Length) { sb.Append(Unescape(s[i])); i++; }
                }
                else
                {
                    sb.Append(s[i]); i++;
                }
            }
            if (i < s.Length) i++; // skip closing '"'
            return sb.ToString();
        }

        // Read a JSON boolean literal. Returns defaultValue if not a bool literal.
        public static bool ReadBool(string s, ref int i, bool defaultValue)
        {
            SkipWs(s, ref i);
            if (i + 4 <= s.Length && s.Substring(i, 4) == "true")  { i += 4; return true;  }
            if (i + 5 <= s.Length && s.Substring(i, 5) == "false") { i += 5; return false; }
            SkipValue(s, ref i);
            return defaultValue;
        }

        // Read a JSON string array. i must point at '['.
        public static string[] ReadStringArray(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != '[') return Array.Empty<string>();
            i++; // skip '['
            var list = new List<string>();
            while (i < s.Length)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] == ']') { if (i < s.Length) i++; break; }
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '"') { var v = ReadString(s, ref i); if (v != null) list.Add(v); }
                else SkipValue(s, ref i);
            }
            return list.ToArray();
        }

        // Find a string-valued property named propName inside the object at i (which must point at '{').
        // Advances i past the closing '}'. Returns null if not found.
        public static string ExtractStringProp(string s, ref int i, string propName)
        {
            if (i >= s.Length || s[i] != '{') return null;
            i++; // skip '{'
            string found = null;
            while (i < s.Length)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] == '}') { i++; break; }
                if (s[i] == ',') { i++; continue; }
                if (s[i] != '"') { i++; continue; }

                var key = ReadString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') continue;
                i++;
                SkipWs(s, ref i);

                if (key == propName && i < s.Length && s[i] == '"')
                    found = ReadString(s, ref i);
                else
                    SkipValue(s, ref i);
            }
            return found;
        }

        // Skip any JSON value at position i. Advances i past the value.
        public static void SkipValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return;
            switch (s[i])
            {
                case '"':  ReadString(s, ref i); return;
                case '{':  SkipBlock(s, ref i, '{', '}'); return;
                case '[':  SkipBlock(s, ref i, '[', ']'); return;
                default:
                    while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']'
                           && s[i] != ' ' && s[i] != '\t' && s[i] != '\r' && s[i] != '\n')
                        i++;
                    return;
            }
        }

        private static void SkipBlock(string s, ref int i, char open, char close)
        {
            if (i >= s.Length || s[i] != open) return;
            i++; int depth = 1;
            while (i < s.Length && depth > 0)
            {
                if (s[i] == '"') { int j = i; ReadString(s, ref j); i = j; continue; }
                if (s[i] == open)  depth++;
                if (s[i] == close) { depth--; if (depth == 0) { i++; return; } }
                i++;
            }
        }

        private static char Unescape(char c)
        {
            switch (c)
            {
                case '"':  return '"';
                case '\\': return '\\';
                case '/':  return '/';
                case 'n':  return '\n';
                case 'r':  return '\r';
                case 't':  return '\t';
                case 'b':  return '\b';
                case 'f':  return '\f';
                default:   return c;
            }
        }
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
