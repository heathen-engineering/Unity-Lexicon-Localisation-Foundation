using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Heathen.Lexicon.Editor
{
    /// <summary>
    /// Custom property drawer for <see cref="LexiconAsset"/> fields. Renders a single-line layout
    /// containing a label, an object field or read-only key preview, a compact hint type dropdown,
    /// and a mode toggle button that opens the key picker context menu.
    /// </summary>
    [CustomPropertyDrawer(typeof(LexiconAsset))]
    public class LexiconAssetDrawer : PropertyDrawer
    {
        private const float ModeButtonW = 36f;
        private const float HintW      = 58f;
        private const float Gap        = 2f;

        /// <summary>
        /// Returns the height required to draw this property, which is always a single line.
        /// </summary>
        /// <param name="property">The serialised property being drawn.</param>
        /// <param name="label">The label associated with the property.</param>
        /// <returns>The height in pixels of a single Editor line.</returns>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => EditorGUIUtility.singleLineHeight;

        /// <summary>
        /// Draws the <see cref="LexiconAsset"/> field in the Inspector.
        /// Layout: [Label] [Field/Preview] [Hint] [Mode]
        /// When localised, the field area shows a read-only key preview.
        /// When literal or invariant, it shows an object picker filtered by the current hint type.
        /// </summary>
        /// <param name="position">The screen rectangle allocated for this property.</param>
        /// <param name="property">The serialised property being drawn.</param>
        /// <param name="label">The label to display for this property.</param>
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
