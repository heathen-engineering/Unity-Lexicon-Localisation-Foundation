using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Heathen.Lexicon.Editor
{
    public static class LexiconCsvInterop
    {
        // Single export: Col 0 = key, Col 1 = value for this document.
        internal static string ExportSingle(HelexDocument doc)
        {
            if (doc == null) return "";
            var sb = new StringBuilder();
            sb.AppendLine(Encode("Key") + "," + Encode(doc.DisplayName));
            sb.AppendLine("," + Encode(doc.AssetId));
            foreach (var e in doc.Entries)
            {
                if (e.Hint != LexiconHintType.String) continue;
                sb.AppendLine(Encode(e.Key) + "," + Encode(e.StringValue ?? ""));
            }
            return sb.ToString();
        }

        // Multi export: Col 0 = key, Col N = value per document (all string entries).
        // Row 0 = human-readable headers, Row 1 = asset IDs, Row 2+ = data.
        internal static string ExportMulti(IEnumerable<HelexDocument> docs)
        {
            var list = new List<HelexDocument>(docs);
            if (list.Count == 0) return "";

            var allKeys = new System.Collections.Generic.HashSet<string>();
            foreach (var d in list)
                foreach (var e in d.Entries)
                    if (e.Hint == LexiconHintType.String && !string.IsNullOrWhiteSpace(e.Key))
                        allKeys.Add(e.Key);

            var keys = new List<string>(allKeys);
            keys.Sort();

            var sb = new StringBuilder();

            sb.Append(Encode("Key"));
            foreach (var d in list) sb.Append("," + Encode(d.DisplayName));
            sb.AppendLine();

            sb.Append("");
            foreach (var d in list) sb.Append("," + Encode(d.AssetId));
            sb.AppendLine();

            foreach (var key in keys)
            {
                sb.Append(Encode(key));
                foreach (var d in list)
                {
                    var val = "";
                    foreach (var e in d.Entries)
                        if (e.Key == key && e.Hint == LexiconHintType.String) { val = e.StringValue ?? ""; break; }
                    sb.Append("," + Encode(val));
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // Import: Row 0 = headers (ignored), Row 1 = asset IDs, Row 2+ = data rows.
        internal static void ImportMulti(string csv, IEnumerable<HelexDocument> docs)
        {
            var list = new List<HelexDocument>(docs);
            var rows = ParseCsv(csv);

            if (rows.Count < 3)
            {
                Debug.LogWarning("[Lexicon] CSV has fewer than 3 rows; nothing to import.");
                return;
            }

            // Row 1: map column index -> document by assetId
            var idRow     = rows[1];
            var colToDoc  = new Dictionary<int, HelexDocument>();
            for (int col = 1; col < idRow.Count; col++)
            {
                var id = idRow[col];
                foreach (var d in list)
                    if (d.AssetId == id || d.DisplayName == id) { colToDoc[col] = d; break; }
            }

            // Row 2+: upsert
            bool anyWritten = false;
            for (int row = 2; row < rows.Count; row++)
            {
                var cols = rows[row];
                if (cols.Count == 0 || string.IsNullOrWhiteSpace(cols[0])) continue;
                var key = cols[0];

                foreach (var kv in colToDoc)
                {
                    if (kv.Key >= cols.Count) continue;
                    var val = cols[kv.Key];
                    var doc = kv.Value;
                    var idx = doc.Entries.FindIndex(e => e.Key == key);
                    if (idx >= 0)
                    {
                        var e = doc.Entries[idx];
                        e.StringValue    = val;
                        doc.Entries[idx] = e;
                    }
                    else
                    {
                        doc.Entries.Add(new HelexEntry { Key = key, Hint = LexiconHintType.String, StringValue = val });
                    }
                    anyWritten = true;
                }
            }

            if (anyWritten)
                foreach (var kv in colToDoc)
                    LexiconSettingsProvider.WriteHelexDoc(kv.Value);
        }

        // RFC 4180 field encode.
        private static string Encode(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        // RFC 4180 parse.
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
