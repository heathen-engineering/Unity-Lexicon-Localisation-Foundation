using System.Collections.Generic;

namespace Heathen.Lexicon.Editor
{
    public enum LexiconEntryStatus : byte
    {
        OK, Missing, Orphan, Duplicate, Empty
    }

    public static class LexiconValidator
    {
        public struct Result
        {
            public string Key;
            public LexiconEntryStatus Status;
            public string Note;
        }

        // Cross-culture validation: compares active against source.
        // Returns one Result per key in the union of both assets.
        public static List<Result> Validate(LexiconData source, LexiconData active)
        {
            var srcKeys  = BuildStringDict(source);
            var actKeys  = BuildStringDict(active);
            var results  = new List<Result>();
            var actSeen  = new Dictionary<string, string>(); // value -> first key

            foreach (var kv in srcKeys)
            {
                if (!actKeys.TryGetValue(kv.Key, out var actVal))
                {
                    results.Add(new Result { Key = kv.Key, Status = LexiconEntryStatus.Missing });
                    continue;
                }
                if (string.IsNullOrEmpty(actVal))
                {
                    results.Add(new Result { Key = kv.Key, Status = LexiconEntryStatus.Empty });
                    continue;
                }
                if (actSeen.TryGetValue(actVal, out var otherKey))
                {
                    results.Add(new Result { Key = kv.Key, Status = LexiconEntryStatus.Duplicate, Note = otherKey });
                }
                else
                {
                    actSeen[actVal] = kv.Key;
                    results.Add(new Result { Key = kv.Key, Status = LexiconEntryStatus.OK });
                }
            }

            // Orphans: in active but absent from source
            foreach (var kv in actKeys)
                if (!srcKeys.ContainsKey(kv.Key))
                    results.Add(new Result { Key = kv.Key, Status = LexiconEntryStatus.Orphan });

            return results;
        }

        // Single-asset validation: duplicates, empty keys, empty values.
        public static List<Result> ValidateSingle(LexiconData data)
        {
            var results    = new List<Result>();
            var seenKeys   = new HashSet<string>();
            var seenValues = new Dictionary<string, string>();

            if (data == null) return results;

            foreach (var entry in data.entries)
            {
                if (string.IsNullOrWhiteSpace(entry.key))
                {
                    results.Add(new Result { Key = "(empty key)", Status = LexiconEntryStatus.Empty });
                    continue;
                }
                if (!seenKeys.Add(entry.key))
                {
                    results.Add(new Result { Key = entry.key, Status = LexiconEntryStatus.Duplicate, Note = "Duplicate key" });
                    continue;
                }
                if (entry.hint == LexiconHintType.String)
                {
                    if (string.IsNullOrEmpty(entry.stringValue))
                    {
                        results.Add(new Result { Key = entry.key, Status = LexiconEntryStatus.Empty });
                    }
                    else if (seenValues.TryGetValue(entry.stringValue, out var other))
                    {
                        results.Add(new Result { Key = entry.key, Status = LexiconEntryStatus.Duplicate, Note = $"Same value as '{other}'" });
                    }
                    else
                    {
                        seenValues[entry.stringValue] = entry.key;
                        results.Add(new Result { Key = entry.key, Status = LexiconEntryStatus.OK });
                    }
                }
                else
                {
                    results.Add(new Result
                    {
                        Key    = entry.key,
                        Status = entry.assetValue == null ? LexiconEntryStatus.Empty : LexiconEntryStatus.OK
                    });
                }
            }

            return results;
        }

        private static Dictionary<string, string> BuildStringDict(LexiconData data)
        {
            var d = new Dictionary<string, string>();
            if (data == null) return d;
            foreach (var e in data.entries)
                if (!string.IsNullOrWhiteSpace(e.key) && e.hint == LexiconHintType.String)
                    d[e.key] = e.stringValue ?? "";
            return d;
        }
    }
}
