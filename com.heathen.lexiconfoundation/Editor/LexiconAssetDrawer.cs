using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Heathen.Lexicon.Editor
{
    [CustomPropertyDrawer(typeof(LexiconAsset))]
    public class LexiconAssetDrawer : PropertyDrawer
    {
        private const float LineH = 18f;
        private const float Gap = 2f;
        private const float RowH = LineH + Gap;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => 2 * RowH;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var modeProp    = property.FindPropertyRelative("Mode");
            var hintProp    = property.FindPropertyRelative("Hint");
            var keyProp     = property.FindPropertyRelative("_key");
            var literalProp = property.FindPropertyRelative("_literalAsset");
            var isLocalised = modeProp.enumValueIndex == (int)LexiconLocMode.Localised;
            var hint        = (LexiconHintType)hintProp.enumValueIndex;

            var line = new Rect(position.x, position.y, position.width, LineH);
            var labelW = EditorGUIUtility.labelWidth;
            var rightW = line.width - labelW;
            var halfW  = rightW * 0.5f - 1f;

            // Row 1: label + mode + hint
            EditorGUI.LabelField(new Rect(line.x, line.y, labelW, line.height), label);
            EditorGUI.PropertyField(new Rect(line.x + labelW, line.y, halfW, line.height), modeProp, GUIContent.none);
            EditorGUI.PropertyField(new Rect(line.x + labelW + halfW + 2f, line.y, halfW, line.height), hintProp, GUIContent.none);
            line.y += RowH;

            if (isLocalised)
            {
                // Row 2: read-only key + Pick button (filtered by hint)
                const float pickW = 50f;
                var prevEnabled = GUI.enabled;
                GUI.enabled = false;
                EditorGUI.PropertyField(new Rect(line.x, line.y, line.width - pickW - 2f, line.height),
                    keyProp, new GUIContent("Key"));
                GUI.enabled = prevEnabled;
                if (GUI.Button(new Rect(line.x + line.width - pickW, line.y, pickW, line.height), "Pick"))
                    ShowKeyPicker(keyProp, hint);
            }
            else
            {
                // Row 2: typed asset field
                EditorGUI.BeginChangeCheck();
                var obj = EditorGUI.ObjectField(line, new GUIContent("Asset"),
                    literalProp.objectReferenceValue, HintToType(hint), false);
                if (EditorGUI.EndChangeCheck())
                    literalProp.objectReferenceValue = obj;
            }

            EditorGUI.EndProperty();
        }

        private static Type HintToType(LexiconHintType hint) => hint switch
        {
            LexiconHintType.Sound   => typeof(AudioClip),
            LexiconHintType.Texture => typeof(Texture2D),
            LexiconHintType.Sprite  => typeof(Sprite),
            LexiconHintType.Prefab  => typeof(GameObject),
            _                       => typeof(Object),
        };

        private static void ShowKeyPicker(SerializedProperty keyProp, LexiconHintType hint)
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
                    if (string.IsNullOrWhiteSpace(entry.key)) continue;
                    if (hint != LexiconHintType.None && entry.hint != hint) continue;
                    if (!seen.Add(entry.key)) continue;
                    var k = entry.key;
                    menu.AddItem(new GUIContent(k.Replace('.', '/')), keyProp.stringValue == k, () =>
                    {
                        keyProp.stringValue = k;
                        keyProp.serializedObject.ApplyModifiedProperties();
                    });
                }
            }

            if (seen.Count == 0)
                menu.AddDisabledItem(new GUIContent("(no matching keys found)"));

            menu.ShowAsContext();
        }
    }
}
