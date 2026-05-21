using UnityEditor;
using UnityEngine;

namespace Heathen.Lexicon.Editor
{
    [InitializeOnLoad]
    [CustomEditor(typeof(LexiconData))]
    public class LexiconDataEditor : UnityEditor.Editor
    {
        static LexiconDataEditor()
        {
            EditorApplication.delayCall += RefreshEditorRegistry;
        }

        public static void ForceRefresh() => RefreshEditorRegistry();

        private static void RefreshEditorRegistry()
        {
            // Ensure the Default asset exists before building the registry
            LexiconSettingsProvider.GetOrCreateDefault();

            var guids = AssetDatabase.FindAssets("t:LexiconData");
            var assets = new System.Collections.Generic.List<LexiconData>(guids.Length);
            foreach (var guid in guids)
            {
                var asset = AssetDatabase.LoadAssetAtPath<LexiconData>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.autoRegister) assets.Add(asset);
            }
            // Register "Default" first so it becomes the unconditional fallback
            foreach (var a in assets) if (LexiconRegistry.IsDefaultAsset(a))  LexiconRegistry.Register(a);
            foreach (var a in assets) if (!LexiconRegistry.IsDefaultAsset(a)) LexiconRegistry.Register(a);
        }

        private SerializedProperty _assetId;
        private SerializedProperty _cultures;
        private SerializedProperty _autoRegister;
        private SerializedProperty _entries;

        private void OnEnable()
        {
            _assetId = serializedObject.FindProperty("assetId");
            _cultures = serializedObject.FindProperty("cultures");
            _autoRegister = serializedObject.FindProperty("autoRegister");
            _entries = serializedObject.FindProperty("entries");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_assetId);
            EditorGUILayout.PropertyField(_cultures, includeChildren: true);
            EditorGUILayout.PropertyField(_autoRegister);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Entries", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_entries, includeChildren: true);

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            var errors = ((LexiconData)target).GetValidationErrors();
            if (errors.Count > 0)
                EditorGUILayout.HelpBox(string.Join("\n", errors), MessageType.Warning);

            if (GUILayout.Button("Refresh Editor Registry"))
                RefreshEditorRegistry();
        }
    }
}
