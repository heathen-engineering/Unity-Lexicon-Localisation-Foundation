using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Heathen.Lexicon.Editor
{
    [CustomPropertyDrawer(typeof(LexiconAsset))]
    public class LexiconAssetDrawer : PropertyDrawer
    {
        private const float ModeButtonW = 36f;
        private const float HintW      = 58f;
        private const float Gap        = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => EditorGUIUtility.singleLineHeight;

        // Layout: [Label] [Field/Preview] [Hint] [Mode]
        //
        // Hint is a compact enum dropdown so the generic LexiconAsset can still be typed
        // without needing to open a menu. For typed fields (LexiconSprite etc.) use those
        // dedicated types instead.
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var modeProp    = property.FindPropertyRelative("Mode");
            var hintProp    = property.FindPropertyRelative("Hint");
            var keyProp     = property.FindPropertyRelative("_key");
            var literalProp = property.FindPropertyRelative("_literalAsset");
            var isLocalised = modeProp.enumValueIndex == (int)LexiconLocMode.Localised;
            var hint        = (LexiconHintType)hintProp.enumValueIndex;

            var labelRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, position.height);
            var modeRect  = new Rect(position.xMax - ModeButtonW, position.y, ModeButtonW, position.height);
            var hintRect  = new Rect(position.xMax - ModeButtonW - HintW - Gap, position.y, HintW, position.height);
            var fieldRect = new Rect(position.x + EditorGUIUtility.labelWidth, position.y,
                                     position.width - EditorGUIUtility.labelWidth - HintW - ModeButtonW - Gap * 2, position.height);

            EditorGUI.LabelField(labelRect, label);

            if (isLocalised)
            {
                var key = keyProp.stringValue;
                var preview = string.IsNullOrEmpty(key) ? "(no key selected)" : key;
                using (new EditorGUI.DisabledScope(true))
                    EditorGUI.TextField(fieldRect, preview);
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                var obj = EditorGUI.ObjectField(fieldRect, literalProp.objectReferenceValue, HintToType(hint), false);
                if (EditorGUI.EndChangeCheck())
                    literalProp.objectReferenceValue = obj;
            }

            EditorGUI.PropertyField(hintRect, hintProp, GUIContent.none);

            var modeLabel = modeProp.enumValueIndex switch
            {
                (int)LexiconLocMode.Localised => "Loc",
                (int)LexiconLocMode.Invariant => "Inv",
                _                             => "Lit",
            };

            if (GUI.Button(modeRect, modeLabel))
                LexiconAssetMenuHelper.ShowMenu(modeProp, keyProp, hint);

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
    }
}
