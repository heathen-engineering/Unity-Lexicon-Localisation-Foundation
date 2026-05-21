using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Heathen.Lexicon.Editor
{
    public static class LexiconCsvInterop
    {
        // Single export: Col 0 = key, Col 1 = value for this asset.
        public static string ExportSingle(LexiconData data)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Encode("Key") + "," + Encode(data.assetId ?? data.name));
            sb.AppendLine("," + Encode(data.assetId ?? data.name));
            foreach (var e in data.entries)
            {
                if (e.hint != LexiconHintType.String) continue;
                sb.AppendLine(Encode(e.key) + "," + Encode(e.stringValue ?? ""));
            }
            return sb.ToString();
        }

        // Multi export: Col 0 = key, Col N = value per asset (all string entries).
        // Row 0 = human-readable headers, Row 1 = asset IDs, Row 2+ = data.
        public static string ExportMulti(IEnumerable<LexiconData> datasets)
        {
            var list = new List<LexiconData>(datasets);
            if (list.Count == 0) return "";

            var allKeys = new HashSet<string>();
            foreach (var d in list)
                foreach (var e in d.entries)
                    if (e.hint == LexiconHintType.String && !string.IsNullOrWhiteSpace(e.key))
                        allKeys.Add(e.key);

            var keys = new List<string>(allKeys);
            keys.Sort();

            var sb = new StringBuilder();

            // Row 0: human-readable headers
            sb.Append(Encode("Key"));
            foreach (var d in list) sb.Append("," + Encode(d.assetId ?? d.name));
            sb.AppendLine();

            // Row 1: asset IDs used for import column mapping
            sb.Append("");
            foreach (var d in list) sb.Append("," + Encode(d.assetId ?? d.name));
            sb.AppendLine();

            // Row 2+: data
            foreach (var key in keys)
            {
                sb.Append(Encode(key));
                foreach (var d in list)
                {
                    var val = "";
                    foreach (var e in d.entries)
                        if (e.key == key && e.hint == LexiconHintType.String) { val = e.stringValue ?? ""; break; }
                    sb.Append("," + Encode(val));
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // Import: Row 0 = headers (ignored), Row 1 = asset IDs, Row 2+ = data rows.
        // Single import reads Col 0 + Col 1 only; multi reads all value columns.
        public static void ImportMulti(string csv, IEnumerable<LexiconData> datasets)
        {
            var list = new List<LexiconData>(datasets);
            var rows = ParseCsv(csv);

            if (rows.Count < 3)
            {
                Debug.LogWarning("[Lexicon] CSV has fewer than 3 rows; nothing to import.");
                return;
            }

            // Row 1: map column index -> dataset by asset ID
            var idRow       = rows[1];
            var colToData   = new Dictionary<int, LexiconData>();
            for (int col = 1; col < idRow.Count; col++)
            {
                var id = idRow[col];
                foreach (var d in list)
                    if ((d.assetId ?? d.name) == id) { colToData[col] = d; break; }
            }

            // Build per-asset key -> entry index maps for fast upsert
            var indexMaps = new Dictionary<LexiconData, Dictionary<string, int>>();
            foreach (var d in colToData.Values)
            {
                if (indexMaps.ContainsKey(d)) continue;
                var map = new Dictionary<string, int>();
                for (int i = 0; i < d.entries.Count; i++)
                    if (!string.IsNullOrWhiteSpace(d.entries[i].key))
                        map[d.entries[i].key] = i;
                indexMaps[d] = map;
            }

            // Row 2+: upsert
            for (int row = 2; row < rows.Count; row++)
            {
                var cols = rows[row];
                if (cols.Count == 0 || string.IsNullOrWhiteSpace(cols[0])) continue;
                var key = cols[0];

                foreach (var kv in colToData)
                {
                    if (kv.Key >= cols.Count) continue;
                    var val  = cols[kv.Key];
                    var d    = kv.Value;
                    var imap = indexMaps[d];

                    if (imap.TryGetValue(key, out var idx))
                    {
                        var entry = d.entries[idx];
                        entry.stringValue = val;
                        d.entries[idx] = entry;
                    }
                    else
                    {
                        d.entries.Add(new LexiconData.Entry { key = key, hint = LexiconHintType.String, stringValue = val });
                        imap[key] = d.entries.Count - 1;
                    }

                    EditorUtility.SetDirty(d);
                }
            }

            AssetDatabase.SaveAssets();
        }

        // RFC 4180 field encode: wraps in quotes when the value contains commas, quotes, or newlines.
        private static string Encode(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        // RFC 4180 parse: handles quoted fields, embedded newlines, and escaped quotes.
        private static List<List<string>> ParseCsv(string csv)
        {
            var result = new List<List<string>>();
            var row    = new List<string>();
            var field  = new StringBuilder();
            bool inQuotes = false;
            int i = 0;

            while (i < csv.Length)
            {
                char c = csv[i];
                if (inQuotes)
                {
                    if (c == '"' && i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i += 2; }
                    else if (c == '"') { inQuotes = false; i++; }
                    else { field.Append(c); i++; }
                }
                else
                {
                    if (c == '"') { inQuotes = true; i++; }
                    else if (c == ',') { row.Add(field.ToString()); field.Clear(); i++; }
                    else if (c == '\r' || c == '\n')
                    {
                        row.Add(field.ToString()); field.Clear();
                        result.Add(row); row = new List<string>();
                        if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                        i++;
                    }
                    else { field.Append(c); i++; }
                }
            }

            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                result.Add(row);
            }

            return result;
        }
    }
}
