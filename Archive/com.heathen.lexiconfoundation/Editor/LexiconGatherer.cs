using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Heathen.Lexicon.Editor
{
    public static class LexiconGatherer
    {
        public class ScanResult
        {
            public string        SourcePath;
            public bool          IsPrefab;
            public int           ComponentIndex;
            public MonoBehaviour LiveComp;       // non-null for scene objects
            public string        FieldName;
            public string        LiteralValue;
            public string        ProposedKey;
            public bool          Confirmed = true;
        }

        public static List<ScanResult> Scan()
        {
            var results = new List<ScanResult>();

            // All prefabs in the project
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                ScanGameObject(root, path, isPrefab: true, results);
                PrefabUtility.UnloadPrefabContents(root);
            }

            // All currently open scenes
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    ScanGameObject(root, scene.path, isPrefab: false, results);
            }

            return results;
        }

        public static void CommitToHelex(List<ScanResult> confirmed, string helexPath)
        {
            if (string.IsNullOrEmpty(helexPath) || confirmed.Count == 0) return;

            var doc         = LexiconSettingsProvider.ReadHelexDoc(helexPath);
            var dirtyScenes = new HashSet<string>();

            foreach (var r in confirmed)
            {
                if (string.IsNullOrWhiteSpace(r.ProposedKey)) continue;

                var idx = doc.Entries.FindIndex(e => e.Key == r.ProposedKey);
                if (idx >= 0)
                {
                    var e = doc.Entries[idx];
                    e.StringValue    = r.LiteralValue;
                    doc.Entries[idx] = e;
                }
                else
                {
                    doc.Entries.Add(new HelexEntry
                    {
                        Key         = r.ProposedKey,
                        Hint        = LexiconHintType.String,
                        StringValue = r.LiteralValue
                    });
                }

                if (r.IsPrefab)
                    PatchPrefabField(r.SourcePath, r.ComponentIndex, r.FieldName, r.ProposedKey);
                else if (r.LiveComp != null)
                {
                    PatchLiveField(r.LiveComp, r.FieldName, r.ProposedKey);
                    dirtyScenes.Add(r.SourcePath);
                }
            }

            LexiconSettingsProvider.WriteHelexDoc(doc);

            foreach (var scenePath in dirtyScenes)
            {
                var scene = EditorSceneManager.GetSceneByPath(scenePath);
                if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
            }

            LexiconDataEditor.ForceRefresh();
            Debug.Log($"[Lexicon] Committed {confirmed.Count} entries to {System.IO.Path.GetFileName(helexPath)}.");
        }

        private static void ScanGameObject(GameObject root, string sourcePath, bool isPrefab, List<ScanResult> results)
        {
            var components = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < components.Length; i++)
            {
                var comp = components[i];
                if (comp == null) continue;

                foreach (var field in comp.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (!typeof(LexiconText).IsAssignableFrom(field.FieldType)) continue;
                    var lt = field.GetValue(comp) as LexiconText;
                    if (lt == null || lt.Mode != LexiconLocMode.Literal || string.IsNullOrWhiteSpace(lt.KeyOrValue)) continue;

                    results.Add(new ScanResult
                    {
                        SourcePath     = sourcePath,
                        IsPrefab       = isPrefab,
                        ComponentIndex = i,
                        LiveComp       = isPrefab ? null : comp,
                        FieldName      = field.Name,
                        LiteralValue   = lt.KeyOrValue,
                        ProposedKey    = GenerateKey(comp.GetType().Name, field.Name),
                        Confirmed      = true
                    });
                }
            }
        }

        private static void PatchPrefabField(string path, int compIdx, string fieldName, string key)
        {
            var root  = PrefabUtility.LoadPrefabContents(path);
            var comps = root.GetComponentsInChildren<MonoBehaviour>(true);
            if (compIdx < comps.Length && comps[compIdx] != null)
                ApplyPatch(new SerializedObject(comps[compIdx]), fieldName, key);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        private static void PatchLiveField(MonoBehaviour comp, string fieldName, string key) =>
            ApplyPatch(new SerializedObject(comp), fieldName, key);

        private static void ApplyPatch(SerializedObject so, string fieldName, string key)
        {
            var prop = so.FindProperty(fieldName);
            if (prop == null) return;
            prop.FindPropertyRelative("Mode").enumValueIndex     = (int)LexiconLocMode.Localised;
            prop.FindPropertyRelative("_keyOrValue").stringValue = key;
            so.ApplyModifiedProperties();
        }

        // Patches scene objects and prefab fields to Localised mode — no JSON writing.
        // Use this when the settings provider handles all .helex writes.
        public static void PatchFields(List<ScanResult> confirmed)
        {
            if (confirmed == null || confirmed.Count == 0) return;
            var dirtyScenes = new HashSet<string>();
            foreach (var r in confirmed)
            {
                if (string.IsNullOrWhiteSpace(r.ProposedKey)) continue;
                if (r.IsPrefab)
                    PatchPrefabField(r.SourcePath, r.ComponentIndex, r.FieldName, r.ProposedKey);
                else if (r.LiveComp != null)
                {
                    PatchLiveField(r.LiveComp, r.FieldName, r.ProposedKey);
                    dirtyScenes.Add(r.SourcePath);
                }
            }
            foreach (var scenePath in dirtyScenes)
            {
                var scene = EditorSceneManager.GetSceneByPath(scenePath);
                if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        private static string GenerateKey(string typeName, string fieldName)
        {
            var seg = System.Text.RegularExpressions.Regex.Replace(fieldName, @"^[_m]_?", "");
            seg     = System.Text.RegularExpressions.Regex.Replace(seg, @"[^A-Za-z0-9_]", "");
            if (string.IsNullOrEmpty(seg)) seg = fieldName;
            return $"{typeName}.{char.ToUpper(seg[0])}{seg[1..]}";
        }
    }
}
