using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Heathen.Lexicon.Editor
{
    [CustomPropertyDrawer(typeof(LexiconText))]
    public class LexiconTextDrawer : PropertyDrawer
    {
        private const float ModeButtonW = 36f;
        private const float Gap        = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => EditorGUIUtility.singleLineHeight;

        // Layout: [Label][          Field          ][Mode]
        //
        // Field — Literal/Invariant: editable text field
        //         Localised:         read-only, shows default-culture resolved value
        //
        // Mode button — "Lit" / "Inv" / "Loc" — opens popup:
        //   • mode switches (checkmark on current)
        //   • separator
        //   • "Key: dot.path.key"  (disabled info row, Localised only)
        //   • separator
        //   • all available keys from LexiconData assets, dot-path → submenu hierarchy
        //     clicking a key sets Localised mode + that key in one action
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var modeProp    = property.FindPropertyRelative("Mode");
            var kvProp      = property.FindPropertyRelative("_keyOrValue");
            var isLocalised = modeProp.enumValueIndex == (int)LexiconLocMode.Localised;

            var labelRect = new Rect(position.x, position.y,
                                     EditorGUIUtility.labelWidth, position.height);
            var modeRect  = new Rect(position.xMax - ModeButtonW, position.y,
                                     ModeButtonW, position.height);
            var fieldRect = new Rect(position.x + EditorGUIUtility.labelWidth, position.y,
                                     position.width - EditorGUIUtility.labelWidth - ModeButtonW - Gap,
                                     position.height);

            EditorGUI.LabelField(labelRect, label);

            if (isLocalised)
            {
                string preview;
                if (string.IsNullOrEmpty(kvProp.stringValue))
                    preview = "(no key selected)";
                else
                    preview = LexiconRegistry.ResolveString(kvProp.stringValue) ?? $"({kvProp.stringValue})";

                using (new EditorGUI.DisabledScope(true))
                    EditorGUI.TextField(fieldRect, preview);
            }
            else
            {
                EditorGUI.PropertyField(fieldRect, kvProp, GUIContent.none);
            }

            var modeLabel = modeProp.enumValueIndex switch
            {
                (int)LexiconLocMode.Localised => "Loc",
                (int)LexiconLocMode.Invariant => "Inv",
                _                             => "Lit",
            };

            if (GUI.Button(modeRect, modeLabel))
                ShowModeMenu(modeProp, kvProp);

            EditorGUI.EndProperty();
        }

        private static void ShowModeMenu(SerializedProperty modeProp, SerializedProperty kvProp)
        {
            var menu        = new GenericMenu();
            bool isLoc      = modeProp.enumValueIndex == (int)LexiconLocMode.Localised;
            string currentKey = kvProp.stringValue;

            // ── Mode switches ───────────────────────────────────────────────
            menu.AddItem(new GUIContent("Literal"),
                modeProp.enumValueIndex == (int)LexiconLocMode.Literal,
                () => ApplyMode(modeProp, LexiconLocMode.Literal));
            menu.AddItem(new GUIContent("Invariant"),
                modeProp.enumValueIndex == (int)LexiconLocMode.Invariant,
                () => ApplyMode(modeProp, LexiconLocMode.Invariant));
            menu.AddItem(new GUIContent("Localised"),
                isLoc,
                () => ApplyMode(modeProp, LexiconLocMode.Localised));

            // ── Current key info (Localised only) ───────────────────────────
            menu.AddSeparator("");
            if (isLoc && !string.IsNullOrEmpty(currentKey))
            {
                menu.AddDisabledItem(new GUIContent($"Key: {currentKey}"));
                menu.AddSeparator("");
            }

            // ── Key picker — all LexiconData assets, dot-path as submenu ────
            var guids  = AssetDatabase.FindAssets("t:LexiconData");
            var seen   = new HashSet<string>();
            bool found = false;

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<LexiconData>(path);
                if (data == null) continue;

                foreach (var entry in data.entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.key) || !seen.Add(entry.key)) continue;
                    found = true;
                    var k = entry.key;
                    bool isCurrent = isLoc && currentKey == k;
                    // Capture for lambda
                    var capMod = modeProp;
                    var capKv  = kvProp;
                    menu.AddItem(new GUIContent(k.Replace('.', '/')), isCurrent, () =>
                    {
                        capMod.enumValueIndex = (int)LexiconLocMode.Localised;
                        capKv.stringValue     = k;
                        capMod.serializedObject.ApplyModifiedProperties();
                    });
                }
            }

            if (!found)
                menu.AddDisabledItem(new GUIContent("(no LexiconData assets found)"));

            menu.ShowAsContext();
        }

        private static void ApplyMode(SerializedProperty modeProp, LexiconLocMode mode)
        {
            modeProp.enumValueIndex = (int)mode;
            modeProp.serializedObject.ApplyModifiedProperties();
        }
    }
}
