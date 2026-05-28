using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Heathen.Lexicon
{
    public class LexiconData : ScriptableObject
    {
        public string assetId;
        public List<string> cultures = new();
        public bool autoRegister = true;

        [Serializable]
        public struct Entry
        {
            public string key;
            public LexiconHintType hint;
            public string stringValue;
            public Object assetValue;
        }

        public List<Entry> entries = new();

        private void OnEnable()
        {
#if UNITY_EDITOR
            // Editor-time registration handled by LexiconDataEditor via [InitializeOnLoad]
#else
            if (autoRegister)
                Register();
#endif
        }

        private void OnDisable()
        {
#if !UNITY_EDITOR
            if (autoRegister)
                Unregister();
#endif
        }

        public void Register() => LexiconRegistry.Register(this);

        public void Unregister() => LexiconRegistry.Unregister(this);

        public List<string> GetValidationErrors()
        {
            var errors = new List<string>();
            var seenKeys = new HashSet<string>();
            var seenStringValues = new Dictionary<string, string>();

            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.key))
                {
                    errors.Add("Entry with empty key found.");
                    continue;
                }

                if (!seenKeys.Add(entry.key))
                {
                    errors.Add($"Duplicate key: {entry.key}");
                    continue;
                }

                if (entry.hint == LexiconHintType.String)
                {
                    if (string.IsNullOrEmpty(entry.stringValue))
                        errors.Add($"Empty string value for key: {entry.key}");
                    else if (seenStringValues.TryGetValue(entry.stringValue, out var other))
                        errors.Add($"Duplicate value for keys '{entry.key}' and '{other}'");
                    else
                        seenStringValues[entry.stringValue] = entry.key;
                }
                else if (entry.assetValue == null)
                {
                    errors.Add($"Null asset reference for key: {entry.key}");
                }
            }

            return errors;
        }
    }
}
