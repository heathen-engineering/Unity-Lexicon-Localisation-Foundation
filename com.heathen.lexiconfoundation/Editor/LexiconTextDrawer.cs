using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Heathen.Lexicon.Editor
{
    [CustomPropertyDrawer(typeof(LexiconText))]
    public class LexiconTextDrawer : PropertyDrawer
    {
        private const float LineH = 18f;
        private const float Gap = 2f;
        private const float RowH = LineH + Gap;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var modeProp = property.FindPropertyRelative("Mode");
            var isLocalised = modeProp.enumValueIndex == (int)LexiconLocMode.Localised;
            return isLocalised ? 3 * RowH : 2 * RowH;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var modeProp = property.FindPropertyRelative("Mode");
            var keyValueProp = property.FindPropertyRelative("_keyOrValue");
            var isLocalised = modeProp.enumValueIndex == (int)LexiconLocMode.Localised;

            var line = new Rect(position.x, position.y, position.width, LineH);

            // Row 1: label + mode dropdown
            EditorGUI.LabelField(new Rect(line.x, line.y, EditorGUIUtility.labelWidth, line.height), label);
            EditorGUI.PropertyField(
                new Rect(line.x + EditorGUIUtility.labelWidth, line.y, line.width - EditorGUIUtility.labelWidth, line.height),
                modeProp, GUIContent.none);
            line.y += RowH;

            if (isLocalised)
            {
                // Row 2: read-only key + Pick button
                const float pickW = 50f;
                var prevEnabled = GUI.enabled;
                GUI.enabled = false;
                EditorGUI.PropertyField(new Rect(line.x, line.y, line.width - pickW - 2f, line.height),
                    keyValueProp, new GUIContent("Key"));
                GUI.enabled = prevEnabled;
                if (GUI.Button(new Rect(line.x + line.width - pickW, line.y, pickW, line.height), "Pick"))
                    ShowKeyPicker(keyValueProp);
                line.y += RowH;

                // Row 3: resolved preview
                var resolved = string.IsNullOrEmpty(keyValueProp.stringValue)
                    ? "(no key set)"
                    : LexiconRegistry.ResolveString(keyValueProp.stringValue) ?? "(not found in active culture)";
                EditorGUI.LabelField(line, new GUIContent("Preview"), new GUIContent(resolved));
            }
            else
            {
                // Row 2: editable value field
                EditorGUI.PropertyField(line, keyValueProp, new GUIContent("Value"));
            }

            EditorGUI.EndProperty();
        }

        private static void ShowKeyPicker(SerializedProperty keyProp)
        {
            var guids = AssetDatabase.FindAssets("t:LexiconData");
            var menu = new GenericMenu();
            var seen = new HashSet<string>();

            foreach (var guid in guids)
            {
                var data = AssetDatabase.LoadAssetAtPath<LexiconData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null) continue;
                foreach (var entry in data.entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.key) || !seen.Add(entry.key)) continue;
                    var k = entry.key;
                    menu.AddItem(new GUIContent(k.Replace('.', '/')), keyProp.stringValue == k, () =>
                    {
                        keyProp.stringValue = k;
                        keyProp.serializedObject.ApplyModifiedProperties();
                    });
                }
            }

            if (seen.Count == 0)
                menu.AddDisabledItem(new GUIContent("(no keys found — create a LexiconData asset)"));

            menu.ShowAsContext();
        }
    }
}
