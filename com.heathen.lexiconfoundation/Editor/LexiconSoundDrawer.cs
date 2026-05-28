using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Heathen.Lexicon.Editor
{
    [CustomPropertyDrawer(typeof(LexiconSound))]
    public class LexiconSoundDrawer : PropertyDrawer
    {
        private const float LineH = 18f;
        private const float Gap = 2f;
        private const float RowH = LineH + Gap;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => 2 * RowH;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var modeProp = property.FindPropertyRelative("Mode");
            var keyProp  = property.FindPropertyRelative("_key");
            var clipProp = property.FindPropertyRelative("_literalClip");
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
                // Row 2: read-only key + Pick button (Sound entries only)
                const float pickW = 50f;
                var prevEnabled = GUI.enabled;
                GUI.enabled = false;
                EditorGUI.PropertyField(new Rect(line.x, line.y, line.width - pickW - 2f, line.height),
                    keyProp, new GUIContent("Key"));
                GUI.enabled = prevEnabled;
                if (GUI.Button(new Rect(line.x + line.width - pickW, line.y, pickW, line.height), "Pick"))
                    ShowKeyPicker(keyProp);
            }
            else
            {
                // Row 2: AudioClip field
                EditorGUI.BeginChangeCheck();
                var clip = EditorGUI.ObjectField(line, new GUIContent("Clip"),
                    clipProp.objectReferenceValue, typeof(AudioClip), false) as AudioClip;
                if (EditorGUI.EndChangeCheck())
                    clipProp.objectReferenceValue = clip;
            }

            EditorGUI.EndProperty();
        }

        private static void ShowKeyPicker(SerializedProperty keyProp)
        {
            var menu  = new GenericMenu();
            var keys  = LexiconSettingsProvider.GetAllLexiconKeys();
            int count = 0;
            foreach (var k in keys)
            {
                count++;
                menu.AddItem(new GUIContent(k.Replace('.', '/')), keyProp.stringValue == k, () =>
                {
                    keyProp.stringValue = k;
                    keyProp.serializedObject.ApplyModifiedProperties();
                });
            }

            if (count == 0)
                menu.AddDisabledItem(new GUIContent("(no .helex files found)"));

            menu.ShowAsContext();
        }
    }
}
