using UnityEditor;
using UnityEngine;

namespace Heathen.Lexicon.Editor
{
    [CustomPropertyDrawer(typeof(LexiconSprite))]
    public class LexiconSpriteDrawer : PropertyDrawer
    {
        private const float ModeButtonW = 36f;
        private const float Gap        = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var modeProp    = property.FindPropertyRelative("Mode");
            var keyProp     = property.FindPropertyRelative("_key");
            var assetProp   = property.FindPropertyRelative("_literalSprite");
            var isLocalised = modeProp.enumValueIndex == (int)LexiconLocMode.Localised;

            var labelRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, position.height);
            var modeRect  = new Rect(position.xMax - ModeButtonW, position.y, ModeButtonW, position.height);
            var fieldRect = new Rect(position.x + EditorGUIUtility.labelWidth, position.y,
                                     position.width - EditorGUIUtility.labelWidth - ModeButtonW - Gap, position.height);

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
                var obj = EditorGUI.ObjectField(fieldRect, assetProp.objectReferenceValue, typeof(Sprite), false);
                if (EditorGUI.EndChangeCheck())
                    assetProp.objectReferenceValue = obj;
            }

            var modeLabel = modeProp.enumValueIndex switch
            {
                (int)LexiconLocMode.Localised => "Loc",
                (int)LexiconLocMode.Invariant => "Inv",
                _                             => "Lit",
            };

            if (GUI.Button(modeRect, modeLabel))
                LexiconAssetMenuHelper.ShowMenu(modeProp, keyProp, LexiconHintType.Sprite);

            EditorGUI.EndProperty();
        }
    }
}
