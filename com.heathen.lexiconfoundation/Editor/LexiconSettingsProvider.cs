using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Heathen.Lexicon.Editor
{
    public class LexiconSettingsProvider : SettingsProvider
    {
        private const string ActiveCultureKey  = "Heathen.Lexicon.ActiveCulture";
        private const string DefaultCultureKey = "Heathen.Lexicon.DefaultCulture";
        private const float  TreeWidth         = 220f;
        private const float  PanelHeight       = 520f;
        private const float  TypeColWidth      = 68f;
        private const float  KeyColWidth       = 170f;
        private const float  StatusColWidth    = 62f;

        public static string EditorActiveCulture
        {
            get => EditorPrefs.GetString(ActiveCultureKey, "");
            set { EditorPrefs.SetString(ActiveCultureKey, value); LexiconRegistry.LoadCulture(value); }
        }

        public static string EditorDefaultCulture
        {
            get => EditorPrefs.GetString(DefaultCultureKey, "");
            set { EditorPrefs.SetString(DefaultCultureKey, value); LexiconRegistry.SetDefaultCulture(value); }
        }

        [SettingsProvider]
        public static SettingsProvider Create() =>
            new LexiconSettingsProvider("Project/Localisation Lexicon", SettingsScope.Project)
            {
                keywords = new HashSet<string>(new[] { "lexicon", "localisation", "culture", "heathen", "translation" })
            };

        // ── Entry model ───────────────────────────────────────────────────────────

        private class EntryEdit
        {
            public LexiconHintType Hint;
            public string          StringValue;
            public Object          AssetValue;

            public static EntryEdit From(LexiconData.Entry e) => new()
            {
                Hint        = e.hint,
                StringValue = e.stringValue ?? "",
                AssetValue  = e.assetValue
            };

            public bool IsEmpty => Hint == LexiconHintType.String
                ? string.IsNullOrEmpty(StringValue)
                : AssetValue == null;
        }

        // ── Window mode ───────────────────────────────────────────────────────────

        private enum Mode { Workbench, Gather, Csv }
        private Mode _mode;

        // ── Workbench state ───────────────────────────────────────────────────────

        private LexiconData   _sourceAsset;
        private LexiconData   _activeAsset;
        private LexiconData[] _allData      = Array.Empty<LexiconData>();
        private string[]      _allDataNames = Array.Empty<string>();
        private List<string>                          _allKeys       = new();
        private Dictionary<string, LexiconData.Entry> _sourceEntries = new();
        private Dictionary<string, EntryEdit>         _editedEntries = new();
        private string  _selectedPrefix;
        private bool    _dirty;
        private string  _addKeyBuffer = "";
        private Vector2 _treeScroll;
        private Vector2 _workbenchScroll;

        // ── Gather state ──────────────────────────────────────────────────────────

        private LexiconData _gatherTarget;
        private List<LexiconGatherer.ScanResult> _gatherResults = new();
        private bool    _gatherScanned;
        private Vector2 _gatherScroll;

        // ── CSV state ─────────────────────────────────────────────────────────────

        private string _csvBuffer = "";

        // ── Constructor & activation ──────────────────────────────────────────────

        public LexiconSettingsProvider(string path, SettingsScope scope) : base(path, scope) { }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            Rebuild();
        }

        // ── Main GUI ──────────────────────────────────────────────────────────────

        public override void OnGUI(string searchContext)
        {
            DrawHeader();
            DrawModeBar();
            EditorGUILayout.Space(4);

            switch (_mode)
            {
                case Mode.Workbench: DrawWorkbench(); break;
                case Mode.Gather:    DrawGather();    break;
                case Mode.Csv:       DrawCsv();       break;
            }
        }

        // ── Header — culture selection ────────────────────────────────────────────

        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();

            // Source dropdown — all known Helix assets
            var srcIdx    = Array.IndexOf(_allData, _sourceAsset);
            EditorGUI.BeginChangeCheck();
            var newSrcIdx = EditorGUILayout.Popup("Source", Mathf.Max(0, srcIdx), _allDataNames);
            if (EditorGUI.EndChangeCheck() && newSrcIdx >= 0 && newSrcIdx < _allData.Length)
            {
                _sourceAsset = _allData[newSrcIdx];
                Rebuild();
            }

            // Active dropdown — "(none)" + all known Helix assets
            var actOptions = new string[_allDataNames.Length + 1];
            actOptions[0] = "(none)";
            _allDataNames.CopyTo(actOptions, 1);
            var actIdx    = Array.IndexOf(_allData, _activeAsset) + 1; // +1 for "(none)"
            EditorGUI.BeginChangeCheck();
            var newActIdx = EditorGUILayout.Popup("Active", Mathf.Max(0, actIdx), actOptions);
            if (EditorGUI.EndChangeCheck())
            {
                _activeAsset = newActIdx == 0 ? null : _allData[newActIdx - 1];
                _dirty = false; // switching asset discards unsaved edits
                Rebuild();
            }

            EditorGUILayout.EndHorizontal();

            var cultures = CollectCultureCodes();
            var arr      = cultures.ToArray();
            EditorGUILayout.BeginHorizontal();
            DrawCulturePicker("Preview Active",  EditorActiveCulture,  arr, v => EditorActiveCulture  = v);
            DrawCulturePicker("Preview Default", EditorDefaultCulture, arr, v => EditorDefaultCulture = v);
            if (GUILayout.Button("New Culture…", GUILayout.Width(108)))
                CreateNewCultureAsset();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // ── Mode / action bar ─────────────────────────────────────────────────────

        private void DrawModeBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Toggle(_mode == Mode.Workbench, "Workbench", EditorStyles.toolbarButton, GUILayout.Width(80)))
                _mode = Mode.Workbench;
            if (GUILayout.Toggle(_mode == Mode.Gather, "Gather", EditorStyles.toolbarButton, GUILayout.Width(60)))
                _mode = Mode.Gather;
            if (GUILayout.Toggle(_mode == Mode.Csv, "CSV", EditorStyles.toolbarButton, GUILayout.Width(44)))
                _mode = Mode.Csv;
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(!_dirty))
                if (GUILayout.Button("Save All", EditorStyles.toolbarButton, GUILayout.Width(60)))
                    SaveAll();
            EditorGUILayout.EndHorizontal();
        }

        // ── Workbench ─────────────────────────────────────────────────────────────

        private void DrawWorkbench()
        {
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(false));

            // Left: key tree
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(TreeWidth));
            DrawAddKeyRow();
            _treeScroll = EditorGUILayout.BeginScrollView(_treeScroll, GUILayout.Height(PanelHeight));
            DrawKeyTree();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            // Right: translation table
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawTableHeader();
            _workbenchScroll = EditorGUILayout.BeginScrollView(_workbenchScroll, GUILayout.Height(PanelHeight));
            DrawTableRows();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawAddKeyRow()
        {
            EditorGUILayout.BeginHorizontal();
            _addKeyBuffer = EditorGUILayout.TextField(_addKeyBuffer);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_addKeyBuffer)))
                if (GUILayout.Button("+", GUILayout.Width(22)))
                    AddKey(_addKeyBuffer.Trim());
            EditorGUILayout.EndHorizontal();
        }

        private void DrawKeyTree()
        {
            string lastTop   = null;
            bool   groupOpen = false;

            foreach (var key in _allKeys)
            {
                var top = key.Split('.')[0];

                if (top != lastTop)
                {
                    lastTop   = top;
                    groupOpen = _selectedPrefix == null
                             || _selectedPrefix == top
                             || _selectedPrefix.StartsWith(top + ".");

                    var s = groupOpen ? EditorStyles.boldLabel : EditorStyles.label;
                    if (GUILayout.Button(top, s))
                        _selectedPrefix = (_selectedPrefix == top) ? null : top;
                }

                if (!groupOpen) continue;

                var leaf   = key.Contains('.') ? key[(key.LastIndexOf('.') + 1)..] : key;
                var status = EntryStatus(key);
                var hint   = _editedEntries.TryGetValue(key, out var ee)
                           ? ee.Hint
                           : (_sourceEntries.TryGetValue(key, out var se) ? se.hint : LexiconHintType.String);
                var s2     = _selectedPrefix == key ? EditorStyles.boldLabel : EditorStyles.miniLabel;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(12);
                if (GUILayout.Button($"{StatusChar(status)} [{HintAbbrev(hint)}] {leaf}", s2))
                    _selectedPrefix = (_selectedPrefix == key) ? top : key;
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawTableHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Key",  EditorStyles.boldLabel, GUILayout.Width(KeyColWidth));
            GUILayout.Label("Type", EditorStyles.boldLabel, GUILayout.Width(TypeColWidth));
            if (_activeAsset != null)
            {
                GUILayout.Label("Source", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
                GUILayout.Label("Active", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
            }
            else
            {
                GUILayout.Label("Value", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
            }
            GUILayout.Label("", GUILayout.Width(StatusColWidth + 22));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTableRows()
        {
            foreach (var key in FilterKeys())
            {
                var status = EntryStatus(key);
                _sourceEntries.TryGetValue(key, out var src);
                _editedEntries.TryGetValue(key, out var act);

                // Use source hint as the canonical type; fall back to active
                var hint = src.hint != LexiconHintType.None ? src.hint
                         : act?.Hint ?? LexiconHintType.String;

                var prevBg = GUI.backgroundColor;
                if (status != LexiconEntryStatus.OK)
                    GUI.backgroundColor = StatusColour(status);

                EditorGUILayout.BeginHorizontal();

                GUILayout.Label(key, EditorStyles.miniLabel, GUILayout.Width(KeyColWidth));

                // Type — always a dropdown when we have an editable entry
                if (act != null)
                {
                    EditorGUI.BeginChangeCheck();
                    var newHint = (LexiconHintType)EditorGUILayout.EnumPopup(
                        act.Hint, GUILayout.Width(TypeColWidth));
                    if (EditorGUI.EndChangeCheck()) ChangeHint(act, newHint, key);
                }
                else
                {
                    GUILayout.Label(HintAbbrev(hint), EditorStyles.centeredGreyMiniLabel, GUILayout.Width(TypeColWidth));
                }

                if (_activeAsset != null)
                {
                    // Two-column mode: Source (read-only) + Active (editable)
                    DrawValueReadOnly(src.hint, src.stringValue, src.assetValue);
                    if (act != null)
                        DrawValueEditable(act, key);
                    else
                        GUILayout.Label("", GUILayout.ExpandWidth(true));
                }
                else
                {
                    // Single-column mode: source is the edit target
                    if (act != null)
                        DrawValueEditable(act, key);
                    else
                        GUILayout.Label("", GUILayout.ExpandWidth(true));
                }

                GUI.backgroundColor = prevBg;

                GUILayout.Label(StatusLabel(status), EditorStyles.miniLabel, GUILayout.Width(StatusColWidth));
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20)))
                    DeleteKey(key);

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawValueReadOnly(LexiconHintType hint, string strVal, Object assetVal)
        {
            if (hint == LexiconHintType.String || hint == LexiconHintType.None)
            {
                GUILayout.Label(strVal ?? "", EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
            }
            else
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField(assetVal, HintToType(hint), false, GUILayout.ExpandWidth(true));
            }
        }

        private void DrawValueEditable(EntryEdit entry, string key)
        {
            if (entry.Hint == LexiconHintType.String)
            {
                EditorGUI.BeginChangeCheck();
                var v = EditorGUILayout.TextField(entry.StringValue ?? "", GUILayout.ExpandWidth(true));
                if (EditorGUI.EndChangeCheck()) { entry.StringValue = v; _dirty = true; }
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                var obj = EditorGUILayout.ObjectField(
                    entry.AssetValue, HintToType(entry.Hint), false, GUILayout.ExpandWidth(true));
                if (EditorGUI.EndChangeCheck()) { entry.AssetValue = obj; _dirty = true; }
            }
        }

        // ── Gather ────────────────────────────────────────────────────────────────

        private void DrawGather()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.HelpBox(
                "Scans all open scenes and project prefabs for LexiconText fields in Literal mode. " +
                "Confirm or adjust the proposed keys, then commit — this writes entries to the target " +
                "LexiconData asset and patches the source fields to Localised mode.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            _gatherTarget = (LexiconData)EditorGUILayout.ObjectField(
                "Target Asset", _gatherTarget, typeof(LexiconData), false);
            if (GUILayout.Button("Scan", GUILayout.Width(60)))
            {
                _gatherResults = LexiconGatherer.Scan();
                _gatherScanned = true;
            }
            using (new EditorGUI.DisabledScope(!_gatherScanned || _gatherTarget == null || _gatherResults.Count == 0))
                if (GUILayout.Button("Commit Selected", GUILayout.Width(120)))
                {
                    LexiconGatherer.Commit(_gatherResults.Where(r => r.Confirmed).ToList(), _gatherTarget);
                    _gatherResults.RemoveAll(r => r.Confirmed);
                    Rebuild();
                }
            EditorGUILayout.EndHorizontal();

            if (!_gatherScanned) { EditorGUILayout.EndVertical(); return; }

            EditorGUILayout.Space(4);
            var confirmed = _gatherResults.Count(r => r.Confirmed);
            EditorGUILayout.LabelField($"{_gatherResults.Count} literal fields found — {confirmed} selected for commit");

            _gatherScroll = EditorGUILayout.BeginScrollView(_gatherScroll, GUILayout.Height(PanelHeight - 80));
            foreach (var r in _gatherResults)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(r.SourcePath + "  ›  " + r.FieldName, EditorStyles.miniLabel);
                EditorGUILayout.LabelField("Value", r.LiteralValue);
                r.ProposedKey = EditorGUILayout.TextField("Key", r.ProposedKey);
                r.Confirmed   = EditorGUILayout.Toggle("Commit", r.Confirmed);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ── CSV ───────────────────────────────────────────────────────────────────

        private void DrawCsv()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("CSV Interop", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_activeAsset == null))
            {
                if (GUILayout.Button("Export Active (Single)"))
                    _csvBuffer = LexiconCsvInterop.ExportSingle(_activeAsset);
                if (GUILayout.Button("Export All Cultures (Multi)"))
                    _csvBuffer = LexiconCsvInterop.ExportMulti(LoadAllLexiconData());
            }
            if (GUILayout.Button("Save to File…") && !string.IsNullOrEmpty(_csvBuffer))
            {
                var path = EditorUtility.SaveFilePanel("Save CSV", "", "lexicon_export", "csv");
                if (!string.IsNullOrEmpty(path)) System.IO.File.WriteAllText(path, _csvBuffer);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Load from File…"))
            {
                var path = EditorUtility.OpenFilePanel("Open CSV", "", "csv");
                if (!string.IsNullOrEmpty(path)) _csvBuffer = System.IO.File.ReadAllText(path);
            }
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_csvBuffer)))
                if (GUILayout.Button("Import (Multi)"))
                {
                    LexiconCsvInterop.ImportMulti(_csvBuffer, LoadAllLexiconData());
                    Rebuild();
                }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            _csvBuffer = EditorGUILayout.TextArea(_csvBuffer, GUILayout.Height(PanelHeight - 80));
            EditorGUILayout.EndVertical();
        }

        // ── Model ─────────────────────────────────────────────────────────────────

        private void RefreshAssetList()
        {
            _allData      = LoadAllLexiconData().ToArray();
            _allDataNames = _allData.Select(d => string.IsNullOrWhiteSpace(d.name) ? "(unnamed)" : d.name).ToArray();
        }

        private void Rebuild()
        {
            RefreshAssetList();

            // Auto-detect (or create) the Default asset when no source is pinned
            if (_sourceAsset == null)
                _sourceAsset = GetOrCreateDefault();

            _allKeys.Clear();
            _sourceEntries.Clear();

            if (_sourceAsset != null)
                foreach (var e in _sourceAsset.entries)
                    if (!string.IsNullOrWhiteSpace(e.key))
                        _sourceEntries[e.key] = e;

            // Populate editable entries from active when set, else fall back to source so
            // the table is always editable without requiring a separate active asset.
            var editTarget = _activeAsset ?? _sourceAsset;
            if (editTarget != null && !_dirty)
            {
                _editedEntries.Clear();
                foreach (var e in editTarget.entries)
                    if (!string.IsNullOrWhiteSpace(e.key))
                        _editedEntries[e.key] = EntryEdit.From(e);
            }

            var union = new HashSet<string>(_sourceEntries.Keys);
            union.UnionWith(_editedEntries.Keys);
            _allKeys = union.OrderBy(k => k).ToList();
        }

        private List<string> FilterKeys()
        {
            if (string.IsNullOrEmpty(_selectedPrefix)) return _allKeys;
            return _allKeys
                .Where(k => k == _selectedPrefix || k.StartsWith(_selectedPrefix + "."))
                .ToList();
        }

        private LexiconEntryStatus EntryStatus(string key)
        {
            var inSrc = _sourceEntries.ContainsKey(key);
            var inAct = _editedEntries.TryGetValue(key, out var act);

            if (inSrc && !inAct)         return LexiconEntryStatus.Missing;
            if (!inSrc && inAct)         return LexiconEntryStatus.Orphan;
            if (inAct && act.IsEmpty)    return LexiconEntryStatus.Empty;

            // Duplicate: same string value on two different keys (strings only)
            if (inAct && act.Hint == LexiconHintType.String && !string.IsNullOrEmpty(act.StringValue))
                foreach (var kv in _editedEntries)
                    if (kv.Key != key && kv.Value.Hint == LexiconHintType.String && kv.Value.StringValue == act.StringValue)
                        return LexiconEntryStatus.Duplicate;

            return LexiconEntryStatus.OK;
        }

        private void AddKey(string key)
        {
            if (_allKeys.Contains(key)) return;
            _editedEntries[key] = new EntryEdit { Hint = LexiconHintType.String };
            _allKeys = _allKeys.Append(key).OrderBy(k => k).ToList();
            _addKeyBuffer = "";
            _dirty = true;
            PropagateKeyToAll(key, LexiconHintType.String);
        }

        private void ChangeHint(EntryEdit entry, LexiconHintType newHint, string key)
        {
            if (newHint == entry.Hint) return;

            if (entry.Hint == LexiconHintType.String)
                entry.StringValue = "";
            else if (newHint == LexiconHintType.String)
                entry.AssetValue = null;
            else if (!IsValueCompatible(entry.AssetValue, newHint))
                entry.AssetValue = null;

            entry.Hint = newHint;
            _dirty = true;

            // Propagate the new hint to every other asset so the type stays consistent
            PropagateHintToAll(key, newHint);
        }

        // Updates the hint on every asset except the current edit target.
        // Clears incompatible values in place so no asset is left with a mismatched type.
        private void PropagateHintToAll(string key, LexiconHintType newHint)
        {
            var editTarget = _activeAsset ?? _sourceAsset;
            RefreshAssetList();
            bool anySaved = false;
            foreach (var data in _allData)
            {
                if (data == editTarget) continue;
                var idx = data.entries.FindIndex(e => e.key == key);
                if (idx < 0) continue;

                var so      = new SerializedObject(data);
                var entries = so.FindProperty("entries");
                var ep      = entries.GetArrayElementAtIndex(idx);
                var oldHint = (LexiconHintType)ep.FindPropertyRelative("hint").enumValueIndex;

                ep.FindPropertyRelative("hint").enumValueIndex = (int)newHint;

                // Clear value when switching between incompatible families
                if (oldHint == LexiconHintType.String && newHint != LexiconHintType.String)
                    ep.FindPropertyRelative("stringValue").stringValue = "";
                else if (oldHint != LexiconHintType.String && newHint == LexiconHintType.String)
                    ep.FindPropertyRelative("assetValue").objectReferenceValue = null;

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(data);
                anySaved = true;
            }
            if (anySaved) AssetDatabase.SaveAssets();
        }

        private static bool IsValueCompatible(Object value, LexiconHintType targetHint)
        {
            if (value == null) return true;
            return targetHint switch
            {
                LexiconHintType.Sound   => value is AudioClip,
                LexiconHintType.Texture => value is Texture2D,
                LexiconHintType.Sprite  => value is Sprite,
                LexiconHintType.Prefab  => value is GameObject,
                LexiconHintType.Asset   => true,
                _                       => false
            };
        }

        private void DeleteKey(string key)
        {
            _editedEntries.Remove(key);
            _allKeys.Remove(key);
            _dirty = true;
            PropagateDeleteToAll(key);
        }

        // Adds an empty-value entry for 'key' to every asset that doesn't already have it.
        // The current edit target is skipped — its entry lives in _editedEntries until SaveAll.
        private void PropagateKeyToAll(string key, LexiconHintType hint)
        {
            var editTarget = _activeAsset ?? _sourceAsset;
            RefreshAssetList();
            bool anySaved = false;
            foreach (var data in _allData)
            {
                if (data == editTarget) continue;
                if (data.entries.Any(e => e.key == key)) continue;

                var so      = new SerializedObject(data);
                var entries = so.FindProperty("entries");
                entries.arraySize++;
                var ep = entries.GetArrayElementAtIndex(entries.arraySize - 1);
                ep.FindPropertyRelative("key").stringValue      = key;
                ep.FindPropertyRelative("hint").enumValueIndex  = (int)hint;
                ep.FindPropertyRelative("stringValue").stringValue = "";
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(data);
                anySaved = true;
            }
            if (anySaved) AssetDatabase.SaveAssets();
        }

        // Removes the entry for 'key' from every asset except the current edit target,
        // which is tracked in _editedEntries and persisted on SaveAll.
        private void PropagateDeleteToAll(string key)
        {
            var editTarget = _activeAsset ?? _sourceAsset;
            RefreshAssetList();
            bool anySaved = false;
            foreach (var data in _allData)
            {
                if (data == editTarget) continue;
                var idx = data.entries.FindIndex(e => e.key == key);
                if (idx < 0) continue;

                var so      = new SerializedObject(data);
                var entries = so.FindProperty("entries");
                entries.DeleteArrayElementAtIndex(idx);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(data);
                anySaved = true;
            }
            if (anySaved) AssetDatabase.SaveAssets();
        }

        private void SaveAll()
        {
            var target = _activeAsset ?? _sourceAsset;
            if (target == null) return;
            target.entries = _editedEntries
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
                .Select(kv => new LexiconData.Entry
                {
                    key         = kv.Key,
                    hint        = kv.Value.Hint,
                    stringValue = kv.Value.StringValue,
                    assetValue  = kv.Value.AssetValue
                })
                .ToList();
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
            _dirty = false;
            LexiconDataEditor.ForceRefresh();
        }

        private void CreateNewCultureAsset()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "New Culture Data", "CultureData", "asset", "Create a new LexiconData asset");
            if (string.IsNullOrEmpty(path)) return;
            var asset = ScriptableObject.CreateInstance<LexiconData>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            _activeAsset = asset;
            _dirty = false;
            Rebuild();
        }

        // ── Static helpers ────────────────────────────────────────────────────────

        private static Type HintToType(LexiconHintType hint) => hint switch
        {
            LexiconHintType.Sound   => typeof(AudioClip),
            LexiconHintType.Texture => typeof(Texture2D),
            LexiconHintType.Sprite  => typeof(Sprite),
            LexiconHintType.Prefab  => typeof(GameObject),
            _                       => typeof(Object)
        };

        private static string HintAbbrev(LexiconHintType hint) => hint switch
        {
            LexiconHintType.String  => "Txt",
            LexiconHintType.Sound   => "Snd",
            LexiconHintType.Texture => "Tex",
            LexiconHintType.Sprite  => "Spr",
            LexiconHintType.Prefab  => "Pfb",
            LexiconHintType.Asset   => "Ast",
            _                       => "—"
        };

        private static string StatusChar(LexiconEntryStatus s) => s switch
        {
            LexiconEntryStatus.Missing   => "○",
            LexiconEntryStatus.Orphan    => "◆",
            LexiconEntryStatus.Duplicate => "▲",
            LexiconEntryStatus.Empty     => "□",
            _                            => "●"
        };

        private static string StatusLabel(LexiconEntryStatus s) => s == LexiconEntryStatus.OK ? "" : s.ToString();

        private static Color StatusColour(LexiconEntryStatus s) => s switch
        {
            LexiconEntryStatus.Missing   => new Color(0.9f, 0.5f, 0.5f),
            LexiconEntryStatus.Orphan    => new Color(0.5f, 0.7f, 1.0f),
            LexiconEntryStatus.Duplicate => new Color(1.0f, 0.85f, 0.3f),
            LexiconEntryStatus.Empty     => new Color(0.7f, 0.7f, 0.7f),
            _                            => Color.white
        };

        private static void DrawCulturePicker(string label, string current, string[] options, Action<string> onChanged)
        {
            if (options.Length == 0)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField(label, "(none registered)");
                return;
            }
            var idx    = Array.IndexOf(options, current);
            var newIdx = EditorGUILayout.Popup(label, Mathf.Max(0, idx), options);
            if (newIdx != idx && newIdx >= 0 && newIdx < options.Length)
                onChanged(options[newIdx]);
        }

        private static List<string> CollectCultureCodes()
        {
            var codes = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:LexiconData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<LexiconData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null) continue;
                foreach (var c in data.cultures)
                    if (!string.IsNullOrWhiteSpace(c)) codes.Add(c);
            }
            return new List<string>(codes);
        }

        private static IEnumerable<LexiconData> LoadAllLexiconData() =>
            AssetDatabase.FindAssets("t:LexiconData")
                .Select(g => AssetDatabase.LoadAssetAtPath<LexiconData>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(d => d != null);

        // ── Public editor API (used by OghamKeyEditWindow and similar tools) ─────

        // Returns every key present in any LexiconData asset, sorted.
        public static IEnumerable<string> GetAllLexiconKeys()
        {
            var keys = new System.Collections.Generic.HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:LexiconData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<LexiconData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null) continue;
                foreach (var e in data.entries)
                    if (!string.IsNullOrWhiteSpace(e.key))
                        keys.Add(e.key);
            }
            return keys.OrderBy(k => k);
        }

        // Creates or updates a String-hint entry across all LexiconData assets.
        // The default asset receives 'stringValue'; all other assets receive an empty
        // placeholder if they don't already have the key (so translators can fill it in).
        public static void UpsertStringEntry(string key, string stringValue)
        {
            bool anySaved = false;
            foreach (var guid in AssetDatabase.FindAssets("t:LexiconData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<LexiconData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null) continue;

                var so      = new SerializedObject(data);
                var entries = so.FindProperty("entries");
                var idx     = data.entries.FindIndex(e => e.key == key);

                if (idx < 0)
                {
                    entries.arraySize++;
                    var ep = entries.GetArrayElementAtIndex(entries.arraySize - 1);
                    ep.FindPropertyRelative("key").stringValue          = key;
                    ep.FindPropertyRelative("hint").enumValueIndex      = (int)LexiconHintType.String;
                    ep.FindPropertyRelative("stringValue").stringValue  =
                        LexiconRegistry.IsDefaultAsset(data) ? stringValue : "";
                    ep.FindPropertyRelative("assetValue").objectReferenceValue = null;
                }
                else if (LexiconRegistry.IsDefaultAsset(data))
                {
                    var ep = entries.GetArrayElementAtIndex(idx);
                    ep.FindPropertyRelative("stringValue").stringValue = stringValue;
                }

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(data);
                anySaved = true;
            }
            if (anySaved)
            {
                AssetDatabase.SaveAssets();
                LexiconDataEditor.ForceRefresh();
            }
        }

        private const string k_SettingsFolder      = "Assets/Settings";
        private const string k_DefaultAssetPath    = k_SettingsFolder + "/Default.asset";

        public static LexiconData GetOrCreateDefault()
        {
            // Return any existing asset that qualifies as the Default
            var existing = AssetDatabase.FindAssets("t:LexiconData")
                .Select(g => AssetDatabase.LoadAssetAtPath<LexiconData>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(d => LexiconRegistry.IsDefaultAsset(d));
            if (existing != null) return existing;

            // None found — create one at Assets/Settings/Default.asset
            if (!AssetDatabase.IsValidFolder(k_SettingsFolder))
            {
                var parent = k_SettingsFolder[..k_SettingsFolder.LastIndexOf('/')];
                var folder = k_SettingsFolder[(k_SettingsFolder.LastIndexOf('/') + 1)..];
                AssetDatabase.CreateFolder(parent, folder);
            }
            var asset = ScriptableObject.CreateInstance<LexiconData>();
            asset.assetId = "default";
            AssetDatabase.CreateAsset(asset, k_DefaultAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(k_DefaultAssetPath, ImportAssetOptions.ForceUpdate);
            return asset;
        }
    }
}
