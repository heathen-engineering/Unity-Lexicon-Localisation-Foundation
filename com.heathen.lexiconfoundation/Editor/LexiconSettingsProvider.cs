using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
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

        // ── Window mode ───────────────────────────────────────────────────────────

        private enum Mode { Workbench, Gather, Csv }
        private Mode _mode;

        // ── Workbench state ───────────────────────────────────────────────────────

        private List<HelexDocument> _allDocs     = new();
        private string[]            _allDocNames = Array.Empty<string>();
        private HelexDocument       _sourceDoc;
        private HelexDocument       _activeDoc;

        private List<string>                     _allKeys       = new();
        private Dictionary<string, HelexEntry>   _editedEntries = new();
        private string  _selectedPrefix;
        private bool    _dirty;
        private bool    _savePending;
        private string  _addKeyBuffer = "";
        private Vector2 _treeScroll;
        private Vector2 _workbenchScroll;

        // ── Gather state ──────────────────────────────────────────────────────────

        private string _gatherTargetPath;
        private List<LexiconGatherer.ScanResult> _gatherResults = new();
        private bool    _gatherScanned;
        private Vector2 _gatherScroll;

        // ── CSV state ─────────────────────────────────────────────────────────────

        private string _csvBuffer = "";

        // ── Constructor & activation ──────────────────────────────────────────────

        public LexiconSettingsProvider(string path, SettingsScope scope) : base(path, scope) { }

        public override void OnActivate(string searchContext, VisualElement rootElement) => Rebuild();

        public override void OnDeactivate()
        {
            if (_dirty) CommitEditedToDoc();
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

        // ── Header ────────────────────────────────────────────────────────────────

        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();

            var srcIdx    = _sourceDoc == null ? 0 : _allDocs.IndexOf(_sourceDoc);
            EditorGUI.BeginChangeCheck();
            var newSrcIdx = EditorGUILayout.Popup("Source", Mathf.Max(0, srcIdx), _allDocNames);
            if (EditorGUI.EndChangeCheck() && newSrcIdx >= 0 && newSrcIdx < _allDocs.Count)
            {
                _sourceDoc = _allDocs[newSrcIdx];
                Rebuild();
            }

            var actOptions = new string[_allDocNames.Length + 1];
            actOptions[0] = "(none)";
            _allDocNames.CopyTo(actOptions, 1);
            var actIdx    = _activeDoc == null ? 0 : _allDocs.IndexOf(_activeDoc) + 1;
            EditorGUI.BeginChangeCheck();
            var newActIdx = EditorGUILayout.Popup("Active", Mathf.Max(0, actIdx), actOptions);
            if (EditorGUI.EndChangeCheck())
            {
                if (_dirty) CommitEditedToDoc();
                _activeDoc = newActIdx == 0 ? null : _allDocs[newActIdx - 1];
                _dirty = false;
                Rebuild();
            }

            EditorGUILayout.EndHorizontal();

            var cultures = CollectCultureCodes();
            var arr      = cultures.ToArray();
            EditorGUILayout.BeginHorizontal();
            DrawCulturePicker("Preview Active",  EditorActiveCulture,  arr, v => EditorActiveCulture  = v);
            DrawCulturePicker("Preview Default", EditorDefaultCulture, arr, v => EditorDefaultCulture = v);
            if (GUILayout.Button("New Culture…", GUILayout.Width(108)))
                CreateNewCultureDocument();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // ── Mode bar ──────────────────────────────────────────────────────────────

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
            EditorGUILayout.EndHorizontal();
        }

        // ── Workbench ─────────────────────────────────────────────────────────────

        private void DrawWorkbench()
        {
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(false));

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(TreeWidth));
            DrawAddKeyRow();
            _treeScroll = EditorGUILayout.BeginScrollView(_treeScroll, GUILayout.Height(PanelHeight));
            DrawKeyTree();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

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
                var hint   = _editedEntries.TryGetValue(key, out var ee) ? ee.Hint : LexiconHintType.String;
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
            if (_activeDoc != null)
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
                _editedEntries.TryGetValue(key, out var act);

                var srcEntry = _sourceDoc?.Entries.FirstOrDefault(e => e.Key == key);
                var hint     = act.Hint != LexiconHintType.None ? act.Hint
                             : (srcEntry.HasValue ? srcEntry.Value.Hint : LexiconHintType.String);

                var prevBg = GUI.backgroundColor;
                if (status != LexiconEntryStatus.OK)
                    GUI.backgroundColor = StatusColour(status);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(key, EditorStyles.miniLabel, GUILayout.Width(KeyColWidth));

                if (_editedEntries.ContainsKey(key))
                {
                    EditorGUI.BeginChangeCheck();
                    var newHint = (LexiconHintType)EditorGUILayout.EnumPopup(act.Hint, GUILayout.Width(TypeColWidth));
                    if (EditorGUI.EndChangeCheck()) ChangeHint(act, newHint, key);
                }
                else
                {
                    GUILayout.Label(HintAbbrev(hint), EditorStyles.centeredGreyMiniLabel, GUILayout.Width(TypeColWidth));
                }

                if (_activeDoc != null)
                {
                    if (srcEntry.HasValue) DrawEntryReadOnly(srcEntry.Value);
                    else GUILayout.Label("", GUILayout.ExpandWidth(true));

                    if (_editedEntries.ContainsKey(key)) DrawEntryEditable(ref act, key);
                    else GUILayout.Label("", GUILayout.ExpandWidth(true));
                }
                else
                {
                    if (_editedEntries.ContainsKey(key)) DrawEntryEditable(ref act, key);
                    else GUILayout.Label("", GUILayout.ExpandWidth(true));
                }

                GUI.backgroundColor = prevBg;

                GUILayout.Label(StatusLabel(status), EditorStyles.miniLabel, GUILayout.Width(StatusColWidth));
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20)))
                    DeleteKey(key);

                EditorGUILayout.EndHorizontal();
            }
        }

        private static void DrawEntryReadOnly(HelexEntry entry)
        {
            if (entry.Hint == LexiconHintType.String || entry.Hint == LexiconHintType.None)
                GUILayout.Label(entry.StringValue ?? "", EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
            else
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    var asset = string.IsNullOrEmpty(entry.AssetPath) ? null : AssetDatabase.LoadAssetAtPath<Object>(entry.AssetPath);
                    EditorGUILayout.ObjectField(asset, HintToType(entry.Hint), false, GUILayout.ExpandWidth(true));
                }
            }
        }

        private void DrawEntryEditable(ref HelexEntry entry, string key)
        {
            if (entry.Hint == LexiconHintType.String)
            {
                EditorGUI.BeginChangeCheck();
                var v = EditorGUILayout.TextField(entry.StringValue ?? "", GUILayout.ExpandWidth(true));
                if (EditorGUI.EndChangeCheck())
                {
                    entry.StringValue   = v;
                    _editedEntries[key] = entry;
                    MarkDirty();
                }
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                var current = string.IsNullOrEmpty(entry.AssetPath) ? null : AssetDatabase.LoadAssetAtPath<Object>(entry.AssetPath);
                var obj     = EditorGUILayout.ObjectField(current, HintToType(entry.Hint), false, GUILayout.ExpandWidth(true));
                if (EditorGUI.EndChangeCheck())
                {
                    entry.AssetPath     = obj == null ? "" : AssetDatabase.GetAssetPath(obj);
                    entry.Hint          = obj == null ? entry.Hint : HintFromAsset(obj);
                    _editedEntries[key] = entry;
                    MarkDirty();
                }
            }
        }

        // ── Gather ────────────────────────────────────────────────────────────────

        private void DrawGather()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.HelpBox(
                "Scans all open scenes and project prefabs for LexiconText fields in Literal mode. " +
                "Confirm or adjust the proposed keys, then commit — this writes entries to the target " +
                ".helex source file and patches the source fields to Localised mode.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (_allDocNames.Length > 0)
            {
                var targetIdx = string.IsNullOrEmpty(_gatherTargetPath)
                    ? 0
                    : _allDocs.FindIndex(d => d.Path == _gatherTargetPath);
                EditorGUI.BeginChangeCheck();
                var newIdx = EditorGUILayout.Popup("Target File", Mathf.Max(0, targetIdx), _allDocNames);
                if (EditorGUI.EndChangeCheck() && newIdx >= 0 && newIdx < _allDocs.Count)
                    _gatherTargetPath = _allDocs[newIdx].Path;
            }
            else
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField("Target File", "(no .helex files)");
            }

            if (GUILayout.Button("Scan", GUILayout.Width(60)))
            {
                _gatherResults = LexiconGatherer.Scan();
                _gatherScanned = true;
            }
            using (new EditorGUI.DisabledScope(!_gatherScanned || string.IsNullOrEmpty(_gatherTargetPath) || _gatherResults.Count == 0))
                if (GUILayout.Button("Commit Selected", GUILayout.Width(120)))
                {
                    LexiconGatherer.CommitToHelex(_gatherResults.Where(r => r.Confirmed).ToList(), _gatherTargetPath);
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
            using (new EditorGUI.DisabledScope(_activeDoc == null))
            {
                if (GUILayout.Button("Export Active (Single)"))
                    _csvBuffer = LexiconCsvInterop.ExportSingle(_activeDoc);
                if (GUILayout.Button("Export All Cultures (Multi)"))
                    _csvBuffer = LexiconCsvInterop.ExportMulti(_allDocs);
            }
            if (GUILayout.Button("Save to File…") && !string.IsNullOrEmpty(_csvBuffer))
            {
                var path = EditorUtility.SaveFilePanel("Save CSV", "", "lexicon_export", "csv");
                if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, _csvBuffer);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Load from File…"))
            {
                var path = EditorUtility.OpenFilePanel("Open CSV", "", "csv");
                if (!string.IsNullOrEmpty(path)) _csvBuffer = File.ReadAllText(path);
            }
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_csvBuffer)))
                if (GUILayout.Button("Import (Multi)"))
                {
                    LexiconCsvInterop.ImportMulti(_csvBuffer, _allDocs);
                    Rebuild();
                }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            _csvBuffer = EditorGUILayout.TextArea(_csvBuffer, GUILayout.Height(PanelHeight - 80));
            EditorGUILayout.EndVertical();
        }

        // ── Model ─────────────────────────────────────────────────────────────────

        private void RefreshDocList()
        {
            _allDocs.Clear();
            var guids = AssetDatabase.FindAssets("t:LexiconCompiledData");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".helex", StringComparison.OrdinalIgnoreCase)) continue;
                try { _allDocs.Add(ReadHelexDoc(path)); } catch { }
            }
            _allDocNames = _allDocs.Select(d => d.DisplayName).ToArray();
        }

        private void Rebuild()
        {
            RefreshDocList();

            // Ensure default exists
            if (_allDocs.Count == 0)
            {
                GetOrCreateDefault();
                RefreshDocList();
            }

            // Re-sync references after refresh
            if (_sourceDoc != null)
                _sourceDoc = _allDocs.FirstOrDefault(d => d.Path == _sourceDoc.Path);
            if (_sourceDoc == null && _allDocs.Count > 0)
                _sourceDoc = _allDocs.First();

            if (_activeDoc != null)
                _activeDoc = _allDocs.FirstOrDefault(d => d.Path == _activeDoc.Path);

            _allKeys.Clear();
            var editTarget = _activeDoc ?? _sourceDoc;
            if (!_dirty && editTarget != null)
            {
                _editedEntries.Clear();
                foreach (var e in editTarget.Entries)
                    if (!string.IsNullOrWhiteSpace(e.Key))
                        _editedEntries[e.Key] = e;
            }

            var union = new HashSet<string>(_editedEntries.Keys);
            if (_sourceDoc != null)
                foreach (var e in _sourceDoc.Entries)
                    if (!string.IsNullOrWhiteSpace(e.Key)) union.Add(e.Key);
            _allKeys = union.OrderBy(k => k).ToList();
        }

        private List<string> FilterKeys()
        {
            if (string.IsNullOrEmpty(_selectedPrefix)) return _allKeys;
            return _allKeys.Where(k => k == _selectedPrefix || k.StartsWith(_selectedPrefix + ".")).ToList();
        }

        private LexiconEntryStatus EntryStatus(string key)
        {
            var srcExists = _sourceDoc?.Entries.Any(e => e.Key == key) ?? false;
            _editedEntries.TryGetValue(key, out var act);
            var actExists = _editedEntries.ContainsKey(key);

            if (srcExists && !actExists)      return LexiconEntryStatus.Missing;
            if (!srcExists && actExists)      return LexiconEntryStatus.Orphan;
            if (actExists && act.IsEmpty)     return LexiconEntryStatus.Empty;

            if (actExists && act.Hint == LexiconHintType.String && !string.IsNullOrEmpty(act.StringValue))
                foreach (var kv in _editedEntries)
                    if (kv.Key != key && kv.Value.Hint == LexiconHintType.String && kv.Value.StringValue == act.StringValue)
                        return LexiconEntryStatus.Duplicate;

            return LexiconEntryStatus.OK;
        }

        private void AddKey(string key)
        {
            if (_allKeys.Contains(key)) return;
            var newEntry = new HelexEntry { Key = key, Hint = LexiconHintType.String };
            _editedEntries[key] = newEntry;
            _allKeys = _allKeys.Append(key).OrderBy(k => k).ToList();
            _addKeyBuffer = "";
            PropagateKeyToAll(key);
            CommitEditedToDoc();
        }

        private void ChangeHint(HelexEntry entry, LexiconHintType newHint, string key)
        {
            if (newHint == entry.Hint) return;
            entry.Hint = newHint;
            if (newHint == LexiconHintType.String) entry.AssetPath = "";
            else entry.StringValue = "";
            _editedEntries[key] = entry;
            _dirty = true;
            PropagateHintToAll(key, newHint);
            CommitEditedToDoc();
        }

        private void DeleteKey(string key)
        {
            _editedEntries.Remove(key);
            _allKeys.Remove(key);
            PropagateDeleteToAll(key);
            CommitEditedToDoc();
        }

        // ── Save / flush ──────────────────────────────────────────────────────────

        private void MarkDirty()
        {
            _dirty = true;
            if (_savePending) return;
            _savePending = true;
            EditorApplication.delayCall += FlushSave;
        }

        private void FlushSave()
        {
            _savePending = false;
            if (_dirty) CommitEditedToDoc();
        }

        private void CommitEditedToDoc()
        {
            var target = _activeDoc ?? _sourceDoc;
            if (target == null) return;
            target.Entries.Clear();
            foreach (var kv in _editedEntries.OrderBy(kv => kv.Key))
                target.Entries.Add(kv.Value);
            WriteHelexDoc(target);
            _dirty = false;
        }

        // ── Propagation ───────────────────────────────────────────────────────────

        private void PropagateKeyToAll(string key)
        {
            var editTarget = _activeDoc ?? _sourceDoc;
            foreach (var doc in _allDocs)
            {
                if (doc == editTarget) continue;
                if (doc.Entries.Any(e => e.Key == key)) continue;
                doc.Entries.Add(new HelexEntry { Key = key, Hint = LexiconHintType.String });
                WriteHelexDoc(doc);
            }
        }

        private void PropagateHintToAll(string key, LexiconHintType newHint)
        {
            var editTarget = _activeDoc ?? _sourceDoc;
            foreach (var doc in _allDocs)
            {
                if (doc == editTarget) continue;
                var idx = doc.Entries.FindIndex(e => e.Key == key);
                if (idx < 0) continue;
                var e = doc.Entries[idx];
                if (newHint == LexiconHintType.String) e.AssetPath   = "";
                else                                   e.StringValue = "";
                e.Hint          = newHint;
                doc.Entries[idx] = e;
                WriteHelexDoc(doc);
            }
        }

        private void PropagateDeleteToAll(string key)
        {
            var editTarget = _activeDoc ?? _sourceDoc;
            foreach (var doc in _allDocs)
            {
                if (doc == editTarget) continue;
                var idx = doc.Entries.FindIndex(e => e.Key == key);
                if (idx >= 0) { doc.Entries.RemoveAt(idx); WriteHelexDoc(doc); }
            }
        }

        // ── Create new culture document ───────────────────────────────────────────

        private void CreateNewCultureDocument()
        {
            if (_dirty) CommitEditedToDoc();
            var path = EditorUtility.SaveFilePanelInProject(
                "New Culture Data", "CultureData", "helex", "Create a new .helex culture file");
            if (string.IsNullOrEmpty(path)) return;

            var doc = new HelexDocument
            {
                Path         = path,
                AssetId      = Path.GetFileNameWithoutExtension(path),
                AutoRegister = true,
            };
            if (_sourceDoc != null)
                foreach (var e in _sourceDoc.Entries)
                    doc.Entries.Add(new HelexEntry { Key = e.Key, Hint = e.Hint });
            WriteHelexDoc(doc);
            _dirty = false;
            Rebuild();
            _activeDoc = _allDocs.FirstOrDefault(d => d.Path == path);
        }

        // ── Public editor API ─────────────────────────────────────────────────────

        public static IEnumerable<string> GetAllLexiconKeys()
        {
            var keys  = new HashSet<string>();
            var guids = AssetDatabase.FindAssets("t:LexiconCompiledData");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".helex", StringComparison.OrdinalIgnoreCase)) continue;
                try { foreach (var e in ReadHelexDoc(path).Entries) if (!string.IsNullOrWhiteSpace(e.Key)) keys.Add(e.Key); }
                catch { }
            }
            return keys.OrderBy(k => k);
        }

        public static void UpsertStringEntry(string key, string stringValue)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            var guids = AssetDatabase.FindAssets("t:LexiconCompiledData");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".helex", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var doc     = ReadHelexDoc(path);
                    var idx     = doc.Entries.FindIndex(e => e.Key == key);
                    bool isDef  = string.Equals(doc.AssetId, "default", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(Path.GetFileNameWithoutExtension(path), "Default", StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        if (isDef)
                        { var e = doc.Entries[idx]; e.StringValue = stringValue; doc.Entries[idx] = e; }
                    }
                    else
                    {
                        doc.Entries.Add(new HelexEntry
                        {
                            Key         = key,
                            Hint        = LexiconHintType.String,
                            StringValue = isDef ? stringValue : ""
                        });
                    }
                    WriteHelexDoc(doc);
                }
                catch { }
            }
        }

        // Returns the path of the default .helex file, creating it if absent.
        public static string GetOrCreateDefault()
        {
            var guids = AssetDatabase.FindAssets("t:LexiconCompiledData");
            foreach (var guid in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (!p.EndsWith(".helex", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(Path.GetFileNameWithoutExtension(p), "Default", StringComparison.OrdinalIgnoreCase))
                    return p;
                try
                {
                    var root = JObject.Parse(File.ReadAllText(p));
                    if (string.Equals(root["assetId"]?.Value<string>(), "default", StringComparison.OrdinalIgnoreCase))
                        return p;
                }
                catch { }
            }

            const string defaultPath = "Assets/Settings/Default.helex";
            Directory.CreateDirectory("Assets/Settings");
            WriteHelexDoc(new HelexDocument { Path = defaultPath, AssetId = "default", AutoRegister = true });
            AssetDatabase.Refresh();
            return defaultPath;
        }

        // ── .helex I/O (accessible from LexiconGatherer, LexiconCsvInterop) ───────

        internal static HelexDocument ReadHelexDoc(string assetPath)
        {
            var root = JObject.Parse(File.ReadAllText(assetPath));
            var doc  = new HelexDocument
            {
                Path         = assetPath,
                AssetId      = root["assetId"]?.Value<string>() ?? Path.GetFileNameWithoutExtension(assetPath),
                AutoRegister = root["registered"]?.Value<bool>() ?? true,
                Cultures     = root["cultures"]?.ToObject<List<string>>() ?? new List<string>(),
            };
            if (root["entries"] is JObject entries)
                foreach (var prop in entries.Properties())
                {
                    var key = prop.Name.Trim();
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    if (prop.Value.Type == JTokenType.String)
                        doc.Entries.Add(new HelexEntry { Key = key, Hint = LexiconHintType.String, StringValue = prop.Value.Value<string>() ?? "" });
                    else if (prop.Value is JObject assetObj)
                    {
                        var ap   = assetObj["path"]?.Value<string>() ?? "";
                        var hint = LexiconHintType.Asset;
                        if (!string.IsNullOrEmpty(ap))
                        {
                            var asset = AssetDatabase.LoadAssetAtPath<Object>(ap);
                            hint = asset switch {
                                AudioClip  _ => LexiconHintType.Sound,
                                Texture2D  _ => LexiconHintType.Texture,
                                Sprite     _ => LexiconHintType.Sprite,
                                GameObject _ => LexiconHintType.Prefab,
                                _            => LexiconHintType.Asset,
                            };
                        }
                        doc.Entries.Add(new HelexEntry { Key = key, Hint = hint, AssetPath = ap });
                    }
                }
            return doc;
        }

        internal static void WriteHelexDoc(HelexDocument doc)
        {
            var entries = new JObject();
            foreach (var e in doc.Entries)
            {
                if (string.IsNullOrWhiteSpace(e.Key)) continue;
                if (e.Hint == LexiconHintType.String || e.Hint == LexiconHintType.None)
                    entries[e.Key] = e.StringValue ?? "";
                else
                    entries[e.Key] = new JObject { ["path"] = e.AssetPath ?? "" };
            }
            var root = new JObject
            {
                ["assetId"]    = doc.AssetId,
                ["registered"] = doc.AutoRegister,
                ["cultures"]   = JArray.FromObject(doc.Cultures ?? new List<string>()),
                ["entries"]    = entries
            };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(doc.Path)) ?? ".");
            File.WriteAllText(doc.Path, root.ToString(Newtonsoft.Json.Formatting.Indented));
            AssetDatabase.ImportAsset(doc.Path, ImportAssetOptions.ForceUpdate);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static LexiconHintType HintFromAsset(Object asset) => asset switch {
            AudioClip  _ => LexiconHintType.Sound,
            Texture2D  _ => LexiconHintType.Texture,
            Sprite     _ => LexiconHintType.Sprite,
            GameObject _ => LexiconHintType.Prefab,
            _            => LexiconHintType.Asset,
        };

        private static Type HintToType(LexiconHintType hint) => hint switch {
            LexiconHintType.Sound   => typeof(AudioClip),
            LexiconHintType.Texture => typeof(Texture2D),
            LexiconHintType.Sprite  => typeof(Sprite),
            LexiconHintType.Prefab  => typeof(GameObject),
            _                       => typeof(Object)
        };

        private static string HintAbbrev(LexiconHintType hint) => hint switch {
            LexiconHintType.String  => "Txt",
            LexiconHintType.Sound   => "Snd",
            LexiconHintType.Texture => "Tex",
            LexiconHintType.Sprite  => "Spr",
            LexiconHintType.Prefab  => "Pfb",
            LexiconHintType.Asset   => "Ast",
            _                       => "—"
        };

        private static string StatusChar(LexiconEntryStatus s) => s switch {
            LexiconEntryStatus.Missing   => "○",
            LexiconEntryStatus.Orphan    => "◆",
            LexiconEntryStatus.Duplicate => "▲",
            LexiconEntryStatus.Empty     => "□",
            _                            => "●"
        };

        private static string StatusLabel(LexiconEntryStatus s) => s == LexiconEntryStatus.OK ? "" : s.ToString();

        private static Color StatusColour(LexiconEntryStatus s) => s switch {
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
            var guids = AssetDatabase.FindAssets("t:LexiconCompiledData");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".helex", StringComparison.OrdinalIgnoreCase)) continue;
                try { foreach (var c in ReadHelexDoc(path).Cultures) if (!string.IsNullOrWhiteSpace(c)) codes.Add(c); }
                catch { }
            }
            return new List<string>(codes);
        }
    }

    // ── Shared .helex document model (accessible across the editor assembly) ──────

    internal class HelexDocument
    {
        public string       Path;
        public string       AssetId;
        public bool         AutoRegister = true;
        public List<string> Cultures     = new();
        public List<HelexEntry> Entries  = new();

        public string DisplayName => string.IsNullOrWhiteSpace(AssetId)
            ? System.IO.Path.GetFileNameWithoutExtension(Path)
            : AssetId;
    }

    internal struct HelexEntry
    {
        public string          Key;
        public LexiconHintType Hint;
        public string          StringValue;
        public string          AssetPath;

        public bool IsEmpty => Hint == LexiconHintType.String
            ? string.IsNullOrEmpty(StringValue)
            : string.IsNullOrEmpty(AssetPath);
    }
}
