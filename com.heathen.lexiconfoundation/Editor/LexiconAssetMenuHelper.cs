using UnityEditor;
using UnityEngine;

namespace Heathen.Lexicon.Editor
{
    // Shared context-menu logic for all single-line Lexicon asset property drawers.
    internal static class LexiconAssetMenuHelper
    {
        internal static void ShowMenu(SerializedProperty modeProp, SerializedProperty keyProp, LexiconHintType hint)
        {
            var menu      = new GenericMenu();
            bool isLoc    = modeProp.enumValueIndex == (int)LexiconLocMode.Localised;
            string curKey = keyProp.stringValue;

            menu.AddItem(new GUIContent("Literal"),
                modeProp.enumValueIndex == (int)LexiconLocMode.Literal,
                () => ApplyMode(modeProp, LexiconLocMode.Literal));
            menu.AddItem(new GUIContent("Invariant"),
                modeProp.enumValueIndex == (int)LexiconLocMode.Invariant,
                () => ApplyMode(modeProp, LexiconLocMode.Invariant));
            menu.AddItem(new GUIContent("Localised"),
                isLoc,
                () => ApplyMode(modeProp, LexiconLocMode.Localised));

            menu.AddSeparator("");
            if (isLoc && !string.IsNullOrEmpty(curKey))
            {
                menu.AddDisabledItem(new GUIContent($"Key: {curKey}"));
                menu.AddSeparator("");
            }

            var keys  = LexiconSettingsProvider.GetAllLexiconKeys(hint);
            bool found = false;
            foreach (var k in keys)
            {
                found = true;
                bool isCurrent = isLoc && curKey == k;
                var capMod = modeProp;
                var capKey = keyProp;
                menu.AddItem(new GUIContent(k.Replace('.', '/')), isCurrent, () =>
                {
                    capMod.enumValueIndex = (int)LexiconLocMode.Localised;
                    capKey.stringValue    = k;
                    capMod.serializedObject.ApplyModifiedProperties();
                });
            }

            if (!found)
                menu.AddDisabledItem(new GUIContent("(no matching keys found)"));

            menu.ShowAsContext();
        }

        private static void ApplyMode(SerializedProperty modeProp, LexiconLocMode mode)
        {
            modeProp.enumValueIndex = (int)mode;
            modeProp.serializedObject.ApplyModifiedProperties();
        }
    }
}
