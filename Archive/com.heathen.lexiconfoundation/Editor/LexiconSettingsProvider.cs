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
        private const float  TreeWidth         = 220f;
        private const float  PanelHeight       = 500f;
        private const float  TypeColWidth      = 68f;
        private const float  KeyColWidth       = 170f;
        private const float  DeleteBtnW        = 22f;

        // Hint options excluding None, sorted alphabetically
        private static readonly LexiconHintType[] HintOptions =
        {
            LexiconHintType.Asset, LexiconHintType.Prefab, LexiconHintType.Sound,
            LexiconHintType.Sprite, LexiconHintType.String, LexiconHintType.Texture,
        };
        private static readonly string[] HintOptionNames = HintOptions.Select(h => h.ToString()).ToArray();

        [SettingsProvider]
        public static SettingsProvider Create() =>
            new LexiconSettingsProvider("Project/Subsystems/Localisation Lexicon", SettingsScope.Project)
            {
                keywords = new HashSet<string>(new[] { "lexicon", "localisation", "culture", "heathen", "translation" })
            };

        // ── Mode ──────────────────────────────────────────────────────────────────

        private enum Mode { Workbench, Gather, Csv }
        private Mode _mode;

        // ── Document model ────────────────────────────────────────────────────────

        private List<HelexDocument> _allDocs     = new();
        private HelexDocument       _defaultDoc;
        private List<HelexDocument> _extraCols   = new(); // additional columns in workbench

        // ── Workbench state ───────────────────────────────────────────────────────

        private List<string>                   _allKeys       = new();
        private Dictionary<string, HelexEntry> _editedEntries = new(); // working copy of default doc
        private string  _selectedPrefix;
        private string  _addKeyBuffer = "";
        private Vector2 _treeScroll;
        private Vector2 _workbenchScroll;
        private bool    _culturesExpanded = true;
        private Rect    _plusBtnRect;
        private Dictionary<string, string> _cultureAddBuffers = new();

        // ── Pending writes ────────────────────────────────────────────────────────

        private readonly HashSet<string> _pendingWrites = new();
        private bool _writePending;
        private bool _dirty;

        // ── Gather state ──────────────────────────────────────────────────────────

        private List<LexiconGatherer.ScanResult> _gatherResults = new();
        private bool    _gatherScanned;
        private Vector2 _gatherScroll;

        // ── CSV state ─────────────────────────────────────────────────────────────

        private bool   _csvTextOnly     = true;
        private bool   _csvSingleDoc    = false;
        private int    _csvSingleDocIdx = 0;
        private string _csvBuffer       = "";

        // ── Constructor & activation ──────────────────────────────────────────────

        public LexiconSettingsProvider(string path, SettingsScope scope) : base(path, scope) { }

        public override void OnActivate(string searchContext, VisualElement rootElement) => Rebuild();

        public override void OnDeactivate()
        {
            if (_dirty) FlushDefaultDoc();
            FlushPendingWrites();
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

            // Per-file cultures
            _culturesExpanded = EditorGUILayout.Foldout(_culturesExpanded, "Source Files");
            if (_culturesExpanded)
            {
                EditorGUI.indentLevel++;
                foreach (var doc in _allDocs)
                {
                    if (!_cultureAddBuffers.ContainsKey(doc.Path))
                        _cultureAddBuffers[doc.Path] = "";

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField(doc.DisplayName, EditorStyles.boldLabel);

                    // Chip strip
                    EditorGUILayout.BeginHorizontal();
                    foreach (var culture in doc.Cultures.ToList())
                    {
                        var prevBg = GUI.backgroundColor;
                        GUI.backgroundColor = new Color(0.65f, 0.85f, 1f);
                        if (GUILayout.Button(culture + "  ✕", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                        {
                            GUI.backgroundColor = prevBg;
                            doc.Cultures.Remove(culture);
                            MarkDocPending(doc.Path);
                            GUIUtility.ExitGUI();
                        }
                        GUI.backgroundColor = prevBg;
                    }
                    if (doc.Cultures.Count == 0)
                    {
                        var hint = IsDefaultDoc(doc)
                            ? "(fallback for unmatched cultures — add a code e.g. \"en\" to also route explicitly)"
                            : "(inactive — add at least one culture code)";
                        GUILayout.Label(hint, EditorStyles.miniLabel);
                    }
                    EditorGUILayout.EndHorizontal();

                    // Filter + add row
                    var ctrlName = "CultureAdd_" + doc.Path.GetHashCode().ToString();
                    EditorGUILayout.BeginHorizontal();
                    GUI.SetNextControlName(ctrlName);
                    _cultureAddBuffers[doc.Path] = EditorGUILayout.TextField(
                        _cultureAddBuffers[doc.Path], GUILayout.ExpandWidth(true));
                    // Placeholder text when empty and unfocused
                    if (string.IsNullOrEmpty(_cultureAddBuffers[doc.Path])
                        && GUI.GetNameOfFocusedControl() != ctrlName
                        && Event.current.type == EventType.Repaint)
                    {
                        var r = GUILayoutUtility.GetLastRect();
                        EditorGUI.LabelField(new Rect(r.x + 3, r.y, r.width - 3, r.height),
                            "type to search (e.g. fr, French, Canada)…",
                            new GUIStyle(EditorStyles.label) { normal = new GUIStyleState { textColor = new Color(0.5f, 0.5f, 0.5f) } });
                    }
                    bool enterPressed = Event.current.type == EventType.KeyDown
                        && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                        && GUI.GetNameOfFocusedControl() == ctrlName;
                    if (GUILayout.Button("+", GUILayout.Width(22)) || enterPressed)
                    {
                        if (enterPressed) Event.current.Use();
                        ShowCulturePicker(doc, _cultureAddBuffers[doc.Path]);
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.EndVertical();
                }
                if (GUILayout.Button("New Culture File…", GUILayout.Width(140)))
                    CreateNewCultureDocument();
                EditorGUI.indentLevel--;
            }

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
            const string ctrlName = "LexiconAddKey";
            EditorGUILayout.BeginHorizontal();
            GUI.SetNextControlName(ctrlName);
            _addKeyBuffer = EditorGUILayout.TextField(_addKeyBuffer);

            bool enterPressed = Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                && GUI.GetNameOfFocusedControl() == ctrlName;

            bool doAdd = !string.IsNullOrWhiteSpace(_addKeyBuffer)
                      && (GUILayout.Button("+", GUILayout.Width(22)) || enterPressed);

            if (doAdd)
            {
                if (enterPressed) Event.current.Use();
                AddKey(_addKeyBuffer.Trim());
                _addKeyBuffer = "";
                GUI.FocusControl(ctrlName);
            }
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

                var leaf = key.Contains('.') ? key[(key.LastIndexOf('.') + 1)..] : key;
                var hint = _editedEntries.TryGetValue(key, out var ee) ? ee.Hint : LexiconHintType.String;
                var s2   = _selectedPrefix == key ? EditorStyles.boldLabel : EditorStyles.miniLabel;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(12);
                if (GUILayout.Button($"{EntryStatusChar(key)} [{HintAbbrev(hint)}] {leaf}", s2))
                    _selectedPrefix = (_selectedPrefix == key) ? top : key;
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawTableHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("", GUILayout.Width(DeleteBtnW));
            GUILayout.Label("Key",  EditorStyles.boldLabel, GUILayout.Width(KeyColWidth));
            GUILayout.Label("Type", EditorStyles.boldLabel, GUILayout.Width(TypeColWidth));
            GUILayout.Label("Default", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));

            foreach (var col in _extraCols)
                GUILayout.Label(col.DisplayName, EditorStyles.boldLabel, GUILayout.ExpandWidth(true));

            // [+] only when there are other helex files to add as columns
            if (_allDocs.Count > 1)
            {
                if (GUILayout.Button("+", EditorStyles.toolbarButton, GUILayout.Width(20)))
                    UnityEditor.PopupWindow.Show(_plusBtnRect, new DocColPicker(_allDocs, _defaultDoc, _extraCols, RebuildKeyList));
                if (Event.current.type == EventType.Repaint)
                    _plusBtnRect = GUILayoutUtility.GetLastRect();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawTableRows()
        {
            foreach (var key in FilterKeys())
            {
                _editedEntries.TryGetValue(key, out var defEntry);
                var hint = defEntry.Hint != LexiconHintType.None ? defEntry.Hint : LexiconHintType.String;

                EditorGUILayout.BeginHorizontal();

                // Delete button
                var prevColor = GUI.contentColor;
                GUI.contentColor = Color.red;
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(DeleteBtnW)))
                { GUI.contentColor = prevColor; DeleteKey(key); EditorGUILayout.EndHorizontal(); return; }
                GUI.contentColor = prevColor;

                // Key label
                GUILayout.Label(key, EditorStyles.miniLabel, GUILayout.Width(KeyColWidth));

                // Type dropdown (no None, alphabetical)
                if (_editedEntries.ContainsKey(key))
                {
                    int hintIdx = Array.IndexOf(HintOptions, hint < LexiconHintType.String ? LexiconHintType.String : hint);
                    if (hintIdx < 0) hintIdx = Array.IndexOf(HintOptions, LexiconHintType.String);
                    EditorGUI.BeginChangeCheck();
                    int newIdx = EditorGUILayout.Popup(hintIdx, HintOptionNames, GUILayout.Width(TypeColWidth));
                    if (EditorGUI.EndChangeCheck()) ChangeHint(defEntry, HintOptions[newIdx], key);
                }
                else
                {
                    GUILayout.Label(HintAbbrev(hint), EditorStyles.centeredGreyMiniLabel, GUILayout.Width(TypeColWidth));
                }

                // Default column
                if (_editedEntries.ContainsKey(key))
                {
                    var isEmpty = defEntry.IsEmpty;
                    var prevBg  = GUI.backgroundColor;
                    if (isEmpty) GUI.backgroundColor = new Color(0.75f, 0.75f, 0.75f);
                    DrawEntryEditable(ref defEntry, key);
                    GUI.backgroundColor = prevBg;
                }
                else
                {
                    GUILayout.Label("", GUILayout.ExpandWidth(true));
                }

                // Extra columns
                foreach (var col in _extraCols)
                    DrawExtraColCell(col, key, hint);

                EditorGUILayout.EndHorizontal();
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
                var cur = string.IsNullOrEmpty(entry.AssetPath) ? null : AssetDatabase.LoadAssetAtPath<Object>(entry.AssetPath);
                var obj = EditorGUILayout.ObjectField(cur, HintToType(entry.Hint), false, GUILayout.ExpandWidth(true));
                if (EditorGUI.EndChangeCheck())
                {
                    entry.AssetPath     = obj == null ? "" : AssetDatabase.GetAssetPath(obj);
                    _editedEntries[key] = entry;
                    MarkDirty();
                }
            }
        }

        private void DrawExtraColCell(HelexDocument doc, string key, LexiconHintType hint)
        {
            var idx   = doc.Entries.FindIndex(e => e.Key == key);
            var entry = idx >= 0 ? doc.Entries[idx] : new HelexEntry { Key = key, Hint = hint };

            var prevBg = GUI.backgroundColor;
            if (entry.IsEmpty) GUI.backgroundColor = new Color(1f, 0.95f, 0.7f);

            if (hint == LexiconHintType.String)
            {
                EditorGUI.BeginChangeCheck();
                var v = EditorGUILayout.TextField(entry.StringValue ?? "", GUILayout.ExpandWidth(true));
                if (EditorGUI.EndChangeCheck())
                {
                    entry.StringValue = v;
                    if (idx >= 0) doc.Entries[idx] = entry; else doc.Entries.Add(entry);
                    MarkDocPending(doc.Path);
                }
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                var cur = string.IsNullOrEmpty(entry.AssetPath) ? null : AssetDatabase.LoadAssetAtPath<Object>(entry.AssetPath);
                var obj = EditorGUILayout.ObjectField(cur, HintToType(hint), false, GUILayout.ExpandWidth(true));
                if (EditorGUI.EndChangeCheck())
                {
                    entry.AssetPath = obj == null ? "" : AssetDatabase.GetAssetPath(obj);
                    if (idx >= 0) doc.Entries[idx] = entry; else doc.Entries.Add(entry);
                    MarkDocPending(doc.Path);
                }
            }

            GUI.backgroundColor = prevBg;
        }

        // ── Gather ────────────────────────────────────────────────────────────────

        private void DrawGather()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.HelpBox(
                "Scans open scenes and project prefabs for LexiconText fields in Literal mode. " +
                "Confirm items and assign keys — each key is added to all .helex files; the " +
                "literal value is set in the Default file and left empty in culture files.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Scan", GUILayout.Width(60)))
            {
                _gatherResults = LexiconGatherer.Scan();
                _gatherScanned = true;
            }
            var confirmed = _gatherResults.Count(r => r.Confirmed);
            using (new EditorGUI.DisabledScope(!_gatherScanned || confirmed == 0 || _defaultDoc == null))
            {
                if (GUILayout.Button($"Commit {confirmed} Selected", GUILayout.Width(140)))
                    CommitGather(_gatherResults.Where(r => r.Confirmed).ToList());
            }
            EditorGUILayout.EndHorizontal();

            if (!_gatherScanned) { EditorGUILayout.EndVertical(); return; }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"{_gatherResults.Count} literal fields found — {confirmed} selected");

            _gatherScroll = EditorGUILayout.BeginScrollView(_gatherScroll, GUILayout.Height(PanelHeight - 80));
            foreach (var r in _gatherResults)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(r.SourcePath + "  ›  " + r.FieldName, EditorStyles.miniLabel);
                EditorGUILayout.LabelField("Value", r.LiteralValue);
                r.ProposedKey = EditorGUILayout.TextField("Key", r.ProposedKey);
                r.Confirmed   = EditorGUILayout.Toggle("Include", r.Confirmed);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void CommitGather(List<LexiconGatherer.ScanResult> confirmed)
        {
            if (_defaultDoc == null || confirmed.Count == 0) return;

            foreach (var r in confirmed)
            {
                if (string.IsNullOrWhiteSpace(r.ProposedKey)) continue;

                // Set value in default doc
                var didx = _defaultDoc.Entries.FindIndex(e => e.Key == r.ProposedKey);
                var defEntry = new HelexEntry { Key = r.ProposedKey, Hint = LexiconHintType.String, StringValue = r.LiteralValue };
                if (didx >= 0) _defaultDoc.Entries[didx] = defEntry;
                else           _defaultDoc.Entries.Add(defEntry);

                // Add empty entry to all other docs
                foreach (var doc in _allDocs)
                {
                    if (doc == _defaultDoc) continue;
                    if (doc.Entries.Any(e => e.Key == r.ProposedKey)) continue;
                    doc.Entries.Add(new HelexEntry { Key = r.ProposedKey, Hint = LexiconHintType.String });
                    MarkDocPending(doc.Path);
                }
            }

            MarkDocPending(_defaultDoc.Path);
            FlushPendingWrites();

            // Patch scene/prefab LexiconText fields to Localised mode
            LexiconGatherer.PatchFields(confirmed);

            _gatherResults.RemoveAll(r => r.Confirmed);
            LexiconDataEditor.ForceRefresh();
            Rebuild();
            Debug.Log($"[Lexicon] Gathered {confirmed.Count} entries into all .helex files.");
        }

        // ── CSV ───────────────────────────────────────────────────────────────────

        private void DrawCsv()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Export", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            _csvTextOnly  = GUILayout.Toggle(_csvTextOnly,  "Text Only",  EditorStyles.toolbarButton);
            _csvTextOnly  = !GUILayout.Toggle(!_csvTextOnly, "All Types",  EditorStyles.toolbarButton);
            GUILayout.Space(20);
            _csvSingleDoc = GUILayout.Toggle(_csvSingleDoc,  "Single File", EditorStyles.toolbarButton);
            _csvSingleDoc = !GUILayout.Toggle(!_csvSingleDoc, "All Files",   EditorStyles.toolbarButton);
            EditorGUILayout.EndHorizontal();

            if (_csvSingleDoc && _allDocs.Count > 0)
            {
                var names = _allDocs.Select(d => d.DisplayName).ToArray();
                _csvSingleDocIdx = Mathf.Clamp(_csvSingleDocIdx, 0, _allDocs.Count - 1);
                _csvSingleDocIdx = EditorGUILayout.Popup("File", _csvSingleDocIdx, names);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Export to File…"))
            {
                _csvBuffer = _csvSingleDoc && _allDocs.Count > 0
                    ? LexiconCsvInterop.ExportSingle(_allDocs[_csvSingleDocIdx], _csvTextOnly)
                    : LexiconCsvInterop.ExportMulti(_allDocs, _csvTextOnly);

                if (!string.IsNullOrEmpty(_csvBuffer))
                {
                    var path = EditorUtility.SaveFilePanel("Export CSV", "", "lexicon_export", "csv");
                    if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, _csvBuffer);
                }
            }
            if (GUILayout.Button("Copy to Clipboard") && !string.IsNullOrEmpty(_csvBuffer))
                GUIUtility.systemCopyBuffer = _csvBuffer;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Import", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Load from File…"))
            {
                var path = EditorUtility.OpenFilePanel("Open CSV", "", "csv");
                if (!string.IsNullOrEmpty(path)) _csvBuffer = File.ReadAllText(path);
            }
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_csvBuffer)))
            {
                if (GUILayout.Button("Import"))
                {
                    LexiconCsvInterop.ImportMulti(_csvBuffer, _allDocs);
                    Rebuild();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Preview", EditorStyles.miniLabel);
            _csvBuffer = EditorGUILayout.TextArea(_csvBuffer, GUILayout.Height(PanelHeight - 140));
            EditorGUILayout.EndVertical();
        }

        // ── Model ─────────────────────────────────────────────────────────────────

        private void RefreshDocList()
        {
            _allDocs.Clear();
            foreach (var path in FindHelexPaths())
            {
                try { _allDocs.Add(ReadHelexDoc(path)); } catch { }
            }

            // Default doc first
            _allDocs.Sort((a, b) =>
            {
                bool aIsDefault = IsDefaultDoc(a);
                bool bIsDefault = IsDefaultDoc(b);
                if (aIsDefault && !bIsDefault) return -1;
                if (!aIsDefault && bIsDefault) return  1;
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });
        }

        private void Rebuild()
        {
            RefreshDocList();

            if (_allDocs.Count == 0)
            {
                GetOrCreateDefault();
                RefreshDocList();
            }

            _defaultDoc = _allDocs.FirstOrDefault(IsDefaultDoc) ?? _allDocs.FirstOrDefault();

            // Re-sync extra columns
            _extraCols = _extraCols
                .Select(c => _allDocs.FirstOrDefault(d => d.Path == c.Path))
                .Where(d => d != null && d != _defaultDoc)
                .ToList();

            RebuildKeyList();

            if (_defaultDoc != null)
            {
                _editedEntries.Clear();
                foreach (var e in _defaultDoc.Entries)
                    if (!string.IsNullOrWhiteSpace(e.Key))
                        _editedEntries[e.Key] = e;
            }
        }

        private void RebuildKeyList()
        {
            var union = new HashSet<string>(_editedEntries.Keys);
            if (_defaultDoc != null)
                foreach (var e in _defaultDoc.Entries)
                    if (!string.IsNullOrWhiteSpace(e.Key)) union.Add(e.Key);
            foreach (var col in _extraCols)
                foreach (var e in col.Entries)
                    if (!string.IsNullOrWhiteSpace(e.Key)) union.Add(e.Key);
            _allKeys = union.OrderBy(k => k).ToList();
        }

        private static bool IsDefaultDoc(HelexDocument d)
            => string.Equals(d.AssetId, "default", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileNameWithoutExtension(d.Path), "Default", StringComparison.OrdinalIgnoreCase);

        private List<string> FilterKeys()
        {
            if (string.IsNullOrEmpty(_selectedPrefix)) return _allKeys;
            return _allKeys.Where(k => k == _selectedPrefix || k.StartsWith(_selectedPrefix + ".")).ToList();
        }

        private string EntryStatusChar(string key)
        {
            if (!_editedEntries.TryGetValue(key, out var e)) return "○";
            if (e.IsEmpty) return "□";
            if (e.Hint == LexiconHintType.String && !string.IsNullOrEmpty(e.StringValue))
                foreach (var kv in _editedEntries)
                    if (kv.Key != key && kv.Value.Hint == LexiconHintType.String && kv.Value.StringValue == e.StringValue)
                        return "▲";
            return "●";
        }

        private void AddKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || _allKeys.Contains(key)) return;
            var newEntry = new HelexEntry { Key = key, Hint = LexiconHintType.String };
            _editedEntries[key] = newEntry;
            _allKeys = _allKeys.Append(key).OrderBy(k => k).ToList();
            // Propagate empty entry to all docs
            foreach (var doc in _allDocs)
            {
                if (doc == _defaultDoc) continue;
                if (doc.Entries.Any(e => e.Key == key)) continue;
                doc.Entries.Add(new HelexEntry { Key = key, Hint = LexiconHintType.String });
                MarkDocPending(doc.Path);
            }
            MarkDirty();
        }

        private void ChangeHint(HelexEntry entry, LexiconHintType newHint, string key)
        {
            if (newHint == entry.Hint) return;
            entry.Hint        = newHint;
            entry.StringValue = newHint == LexiconHintType.String ? entry.StringValue : "";
            entry.AssetPath   = newHint == LexiconHintType.String ? "" : entry.AssetPath;
            _editedEntries[key] = entry;
            // Propagate hint change to all docs
            foreach (var doc in _allDocs)
            {
                if (doc == _defaultDoc) continue;
                var idx = doc.Entries.FindIndex(e => e.Key == key);
                if (idx < 0) continue;
                var e = doc.Entries[idx];
                e.Hint        = newHint;
                e.StringValue = newHint == LexiconHintType.String ? e.StringValue : "";
                e.AssetPath   = newHint == LexiconHintType.String ? "" : e.AssetPath;
                doc.Entries[idx] = e;
                MarkDocPending(doc.Path);
            }
            MarkDirty();
        }

        private void DeleteKey(string key)
        {
            _editedEntries.Remove(key);
            _allKeys.Remove(key);
            foreach (var doc in _allDocs)
            {
                var idx = doc.Entries.FindIndex(e => e.Key == key);
                if (idx >= 0) { doc.Entries.RemoveAt(idx); MarkDocPending(doc.Path); }
            }
            MarkDirty();
        }

        // ── Pending writes ────────────────────────────────────────────────────────

        private void MarkDirty()
        {
            _dirty = true;
            if (_defaultDoc != null) MarkDocPending(_defaultDoc.Path);
        }

        private void MarkDocPending(string path)
        {
            _pendingWrites.Add(path);
            if (_writePending) return;
            _writePending = true;
            EditorApplication.delayCall += FlushPendingWrites;
        }

        private void FlushDefaultDoc()
        {
            if (_defaultDoc == null) return;
            _defaultDoc.Entries.Clear();
            foreach (var kv in _editedEntries.OrderBy(kv => kv.Key))
                _defaultDoc.Entries.Add(kv.Value);
            _dirty = false;
        }

        private void FlushPendingWrites()
        {
            _writePending = false;
            if (_dirty) FlushDefaultDoc();

            foreach (var path in _pendingWrites.ToList())
            {
                var doc = _allDocs.FirstOrDefault(d => d.Path == path);
                if (doc == null) continue;
                if (doc == _defaultDoc)
                {
                    doc.Entries.Clear();
                    foreach (var kv in _editedEntries.OrderBy(kv => kv.Key))
                        doc.Entries.Add(kv.Value);
                    _dirty = false;
                }
                WriteHelexDoc(doc);
            }
            _pendingWrites.Clear();
        }

        // ── Culture picker ────────────────────────────────────────────────────────

        private void ShowCulturePicker(HelexDocument doc, string filter)
        {
            var menu  = new GenericMenu();
            var f     = filter.Trim();
            int shown = 0;

            foreach (var (code, name) in KnownCultures.All)
            {
                if (!string.IsNullOrEmpty(f)
                    && !code.StartsWith(f, StringComparison.OrdinalIgnoreCase)
                    && !name.Contains(f,  StringComparison.OrdinalIgnoreCase))
                    continue;
                if (doc.Cultures.Contains(code)) continue;

                var capturedCode = code;
                menu.AddItem(new GUIContent($"{code}  —  {name}"), false, () =>
                {
                    doc.Cultures.Add(capturedCode);
                    if (_cultureAddBuffers.ContainsKey(doc.Path))
                        _cultureAddBuffers[doc.Path] = "";
                    MarkDocPending(doc.Path);
                });
                if (++shown >= 50) break;
            }

            if (shown == 0)
            {
                if (!string.IsNullOrEmpty(f))
                    menu.AddItem(new GUIContent($"Add \"{f}\" (custom code)"), false, () =>
                    {
                        doc.Cultures.Add(f);
                        if (_cultureAddBuffers.ContainsKey(doc.Path))
                            _cultureAddBuffers[doc.Path] = "";
                        MarkDocPending(doc.Path);
                    });
                else
                    menu.AddDisabledItem(new GUIContent("(all known cultures already added)"));
            }

            menu.ShowAsContext();
        }

        // ── Create new culture document ───────────────────────────────────────────

        private void CreateNewCultureDocument()
        {
            FlushPendingWrites();
            var path = EditorUtility.SaveFilePanelInProject(
                "New Culture File", "CultureData", "helex", "Create a new .helex culture file");
            if (string.IsNullOrEmpty(path)) return;

            var doc = new HelexDocument
            {
                Path         = path,
                AssetId      = Path.GetFileNameWithoutExtension(path),
                AutoRegister = true,
            };
            if (_defaultDoc != null)
                foreach (var e in _defaultDoc.Entries)
                    doc.Entries.Add(new HelexEntry { Key = e.Key, Hint = e.Hint });
            WriteHelexDoc(doc);
            Rebuild();
        }

        // ── Public editor API ─────────────────────────────────────────────────────

        public static IEnumerable<string> GetAllLexiconKeys()
        {
            var keys  = new HashSet<string>();
            foreach (var path in FindHelexPaths())
            {
                try { foreach (var e in ReadHelexDoc(path).Entries) if (!string.IsNullOrWhiteSpace(e.Key)) keys.Add(e.Key); }
                catch { }
            }
            return keys.OrderBy(k => k);
        }

        public static IEnumerable<string> GetAllLexiconKeys(LexiconHintType hint)
        {
            var keys  = new HashSet<string>();
            foreach (var path in FindHelexPaths())
            {
                try
                {
                    foreach (var e in ReadHelexDoc(path).Entries)
                        if (!string.IsNullOrWhiteSpace(e.Key) && e.Hint == hint)
                            keys.Add(e.Key);
                }
                catch { }
            }
            return keys.OrderBy(k => k);
        }

        public static void UpsertStringEntry(string key, string stringValue)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            foreach (var path in FindHelexPaths())
            {
                try
                {
                    var doc    = ReadHelexDoc(path);
                    var idx    = doc.Entries.FindIndex(e => e.Key == key);
                    bool isDef = string.Equals(doc.AssetId, "default", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(Path.GetFileNameWithoutExtension(path), "Default", StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    { if (isDef) { var e = doc.Entries[idx]; e.StringValue = stringValue; doc.Entries[idx] = e; } }
                    else
                        doc.Entries.Add(new HelexEntry { Key = key, Hint = LexiconHintType.String, StringValue = isDef ? stringValue : "" });
                    WriteHelexDoc(doc);
                }
                catch { }
            }
        }

        public static string GetOrCreateDefault()
        {
            foreach (var p in FindHelexPaths())
            {
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

        // ── .helex I/O ────────────────────────────────────────────────────────────

        /// <summary>
        /// Enumerates every <c>.helex</c> asset path in the project, skipping hidden package <c>Samples~</c>
        /// folders. Replaces <c>FindAssets("t:LexiconCompiledData")</c> now that <c>.helex</c> imports to a
        /// TextAsset rather than a compiled ScriptableObject.
        /// </summary>
        internal static IEnumerable<string> FindHelexPaths()
        {
            foreach (var path in AssetDatabase.GetAllAssetPaths())
                if (path.EndsWith(".helex", StringComparison.OrdinalIgnoreCase) && !path.Contains("~/"))
                    yield return path;
        }

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
                        var ap       = assetObj["path"]?.Value<string>() ?? "";
                        var hintStr  = assetObj["hint"]?.Value<string>();
                        LexiconHintType hint;

                        // Prefer the explicit stored hint, then detect from the loaded asset.
                        if (!string.IsNullOrEmpty(hintStr) && Enum.TryParse(hintStr, out LexiconHintType parsedHint))
                            hint = parsedHint;
                        else if (!string.IsNullOrEmpty(ap))
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
                        else
                            hint = LexiconHintType.Asset;

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
                    entries[e.Key] = new JObject
                    {
                        ["hint"] = e.Hint.ToString(),
                        // GUID is authoritative at runtime (the Addressables address); path is kept for readability.
                        ["guid"] = string.IsNullOrEmpty(e.AssetPath) ? "" : AssetDatabase.AssetPathToGUID(e.AssetPath),
                        ["path"] = e.AssetPath ?? ""
                    };
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

    }

    // ── Column picker popup ───────────────────────────────────────────────────────

    internal sealed class DocColPicker : PopupWindowContent
    {
        private readonly List<HelexDocument> _all;
        private readonly HelexDocument       _skip;
        private readonly List<HelexDocument> _selected;
        private readonly Action              _onChange;

        public DocColPicker(List<HelexDocument> all, HelexDocument skip, List<HelexDocument> selected, Action onChange)
        { _all = all; _skip = skip; _selected = selected; _onChange = onChange; }

        public override Vector2 GetWindowSize()
            => new Vector2(200, Mathf.Min(_all.Count * 22f + 8, 300));

        public override void OnGUI(Rect rect)
        {
            foreach (var doc in _all)
            {
                if (doc == _skip) continue;
                bool on  = _selected.Contains(doc);
                bool now = EditorGUILayout.ToggleLeft(doc.DisplayName, on);
                if (now != on) { if (now) _selected.Add(doc); else _selected.Remove(doc); _onChange(); }
            }
        }
    }

    // ── Shared .helex document model ──────────────────────────────────────────────

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

    // ── Known BCP 47 culture codes ────────────────────────────────────────────────
    // Covers base language codes (e.g. "fr") and common regional variants.
    // Base codes match all sub-cultures at runtime (fr → fr-FR, fr-CA, fr-BE, …).
    // Regional codes (fr-CA) are matched exactly before falling back to the base.

    internal static class KnownCultures
    {
        public static readonly (string Code, string Name)[] All =
        {
            // ── Base language codes ──────────────────────────────────────────────
            ("af",       "Afrikaans"),
            ("ar",       "Arabic"),
            ("be",       "Belarusian"),
            ("bg",       "Bulgarian"),
            ("bn",       "Bengali"),
            ("ca",       "Catalan"),
            ("cs",       "Czech"),
            ("cy",       "Welsh"),
            ("da",       "Danish"),
            ("de",       "German"),
            ("el",       "Greek"),
            ("en",       "English"),
            ("es",       "Spanish"),
            ("et",       "Estonian"),
            ("eu",       "Basque"),
            ("fa",       "Persian"),
            ("fi",       "Finnish"),
            ("fr",       "French"),
            ("ga",       "Irish"),
            ("gl",       "Galician"),
            ("he",       "Hebrew"),
            ("hi",       "Hindi"),
            ("hr",       "Croatian"),
            ("hu",       "Hungarian"),
            ("hy",       "Armenian"),
            ("id",       "Indonesian"),
            ("is",       "Icelandic"),
            ("it",       "Italian"),
            ("ja",       "Japanese"),
            ("ka",       "Georgian"),
            ("kk",       "Kazakh"),
            ("ko",       "Korean"),
            ("lt",       "Lithuanian"),
            ("lv",       "Latvian"),
            ("mk",       "Macedonian"),
            ("ms",       "Malay"),
            ("mt",       "Maltese"),
            ("nb",       "Norwegian Bokmål"),
            ("nl",       "Dutch"),
            ("nn",       "Norwegian Nynorsk"),
            ("pl",       "Polish"),
            ("pt",       "Portuguese"),
            ("ro",       "Romanian"),
            ("ru",       "Russian"),
            ("sk",       "Slovak"),
            ("sl",       "Slovenian"),
            ("sq",       "Albanian"),
            ("sr",       "Serbian"),
            ("sv",       "Swedish"),
            ("sw",       "Swahili"),
            ("th",       "Thai"),
            ("tr",       "Turkish"),
            ("uk",       "Ukrainian"),
            ("ur",       "Urdu"),
            ("uz",       "Uzbek"),
            ("vi",       "Vietnamese"),
            ("zh",       "Chinese"),
            ("zu",       "Zulu"),
            // ── Arabic variants ──────────────────────────────────────────────────
            ("ar-AE",    "Arabic (United Arab Emirates)"),
            ("ar-EG",    "Arabic (Egypt)"),
            ("ar-SA",    "Arabic (Saudi Arabia)"),
            // ── Chinese variants ─────────────────────────────────────────────────
            ("zh-CN",    "Chinese (Simplified, China)"),
            ("zh-HK",    "Chinese (Traditional, Hong Kong)"),
            ("zh-MO",    "Chinese (Traditional, Macau)"),
            ("zh-SG",    "Chinese (Simplified, Singapore)"),
            ("zh-TW",    "Chinese (Traditional, Taiwan)"),
            // ── Dutch variants ───────────────────────────────────────────────────
            ("nl-BE",    "Dutch (Belgium)"),
            ("nl-NL",    "Dutch (Netherlands)"),
            // ── English variants ─────────────────────────────────────────────────
            ("en-AU",    "English (Australia)"),
            ("en-CA",    "English (Canada)"),
            ("en-GB",    "English (United Kingdom)"),
            ("en-IE",    "English (Ireland)"),
            ("en-IN",    "English (India)"),
            ("en-NZ",    "English (New Zealand)"),
            ("en-SG",    "English (Singapore)"),
            ("en-US",    "English (United States)"),
            ("en-ZA",    "English (South Africa)"),
            // ── French variants ──────────────────────────────────────────────────
            ("fr-BE",    "French (Belgium)"),
            ("fr-CA",    "French (Canada)"),
            ("fr-CH",    "French (Switzerland)"),
            ("fr-FR",    "French (France)"),
            ("fr-LU",    "French (Luxembourg)"),
            ("fr-MC",    "French (Monaco)"),
            // ── German variants ──────────────────────────────────────────────────
            ("de-AT",    "German (Austria)"),
            ("de-CH",    "German (Switzerland)"),
            ("de-DE",    "German (Germany)"),
            ("de-LI",    "German (Liechtenstein)"),
            ("de-LU",    "German (Luxembourg)"),
            // ── Italian variants ─────────────────────────────────────────────────
            ("it-CH",    "Italian (Switzerland)"),
            ("it-IT",    "Italian (Italy)"),
            // ── Portuguese variants ──────────────────────────────────────────────
            ("pt-BR",    "Portuguese (Brazil)"),
            ("pt-PT",    "Portuguese (Portugal)"),
            // ── Russian variants ─────────────────────────────────────────────────
            ("ru-RU",    "Russian (Russia)"),
            ("ru-UA",    "Russian (Ukraine)"),
            // ── Serbian variants ─────────────────────────────────────────────────
            ("sr-Cyrl",    "Serbian (Cyrillic)"),
            ("sr-Cyrl-RS", "Serbian (Cyrillic, Serbia)"),
            ("sr-Latn",    "Serbian (Latin)"),
            ("sr-Latn-RS", "Serbian (Latin, Serbia)"),
            // ── Spanish variants ─────────────────────────────────────────────────
            ("es-AR",    "Spanish (Argentina)"),
            ("es-BO",    "Spanish (Bolivia)"),
            ("es-CL",    "Spanish (Chile)"),
            ("es-CO",    "Spanish (Colombia)"),
            ("es-CR",    "Spanish (Costa Rica)"),
            ("es-DO",    "Spanish (Dominican Republic)"),
            ("es-EC",    "Spanish (Ecuador)"),
            ("es-ES",    "Spanish (Spain)"),
            ("es-GT",    "Spanish (Guatemala)"),
            ("es-HN",    "Spanish (Honduras)"),
            ("es-MX",    "Spanish (Mexico)"),
            ("es-NI",    "Spanish (Nicaragua)"),
            ("es-PA",    "Spanish (Panama)"),
            ("es-PE",    "Spanish (Peru)"),
            ("es-PR",    "Spanish (Puerto Rico)"),
            ("es-PY",    "Spanish (Paraguay)"),
            ("es-SV",    "Spanish (El Salvador)"),
            ("es-UY",    "Spanish (Uruguay)"),
            ("es-VE",    "Spanish (Venezuela)"),
            // ── Swedish variants ─────────────────────────────────────────────────
            ("sv-FI",    "Swedish (Finland)"),
            ("sv-SE",    "Swedish (Sweden)"),
            // ── Other single-region codes ────────────────────────────────────────
            ("af-ZA",    "Afrikaans (South Africa)"),
            ("bs-Cyrl",  "Bosnian (Cyrillic)"),
            ("bs-Latn",  "Bosnian (Latin)"),
            ("ca-ES",    "Catalan (Spain)"),
            ("cs-CZ",    "Czech (Czech Republic)"),
            ("cy-GB",    "Welsh (United Kingdom)"),
            ("da-DK",    "Danish (Denmark)"),
            ("el-GR",    "Greek (Greece)"),
            ("et-EE",    "Estonian (Estonia)"),
            ("eu-ES",    "Basque (Spain)"),
            ("fi-FI",    "Finnish (Finland)"),
            ("ga-IE",    "Irish (Ireland)"),
            ("gl-ES",    "Galician (Spain)"),
            ("he-IL",    "Hebrew (Israel)"),
            ("hi-IN",    "Hindi (India)"),
            ("hr-BA",    "Croatian (Bosnia and Herzegovina)"),
            ("hr-HR",    "Croatian (Croatia)"),
            ("hu-HU",    "Hungarian (Hungary)"),
            ("hy-AM",    "Armenian (Armenia)"),
            ("id-ID",    "Indonesian (Indonesia)"),
            ("is-IS",    "Icelandic (Iceland)"),
            ("ja-JP",    "Japanese (Japan)"),
            ("ka-GE",    "Georgian (Georgia)"),
            ("kk-KZ",    "Kazakh (Kazakhstan)"),
            ("ko-KR",    "Korean (Korea)"),
            ("lt-LT",    "Lithuanian (Lithuania)"),
            ("lv-LV",    "Latvian (Latvia)"),
            ("mk-MK",    "Macedonian (North Macedonia)"),
            ("ms-BN",    "Malay (Brunei)"),
            ("ms-MY",    "Malay (Malaysia)"),
            ("mt-MT",    "Maltese (Malta)"),
            ("nb-NO",    "Norwegian Bokmål (Norway)"),
            ("nn-NO",    "Norwegian Nynorsk (Norway)"),
            ("pl-PL",    "Polish (Poland)"),
            ("ro-RO",    "Romanian (Romania)"),
            ("sk-SK",    "Slovak (Slovakia)"),
            ("sl-SI",    "Slovenian (Slovenia)"),
            ("sq-AL",    "Albanian (Albania)"),
            ("sw-KE",    "Swahili (Kenya)"),
            ("th-TH",    "Thai (Thailand)"),
            ("tr-TR",    "Turkish (Turkey)"),
            ("uk-UA",    "Ukrainian (Ukraine)"),
            ("ur-PK",    "Urdu (Pakistan)"),
            ("uz-Latn",  "Uzbek (Latin)"),
            ("vi-VN",    "Vietnamese (Vietnam)"),
            ("zu-ZA",    "Zulu (South Africa)"),
        };
    }
}
