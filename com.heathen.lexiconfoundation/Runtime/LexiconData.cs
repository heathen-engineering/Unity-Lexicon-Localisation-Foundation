using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Heathen.Lexicon
{
    /// <summary>
    /// A legacy <see cref="ScriptableObject"/> that stores Lexicon entries as a plain list.
    /// When <see cref="autoRegister"/> is <see langword="true"/> the asset registers itself with
    /// <see cref="LexiconRegistry"/> at runtime via <c>OnEnable</c>. Prefer <c>.helex</c> source
    /// files compiled into <see cref="LexiconCompiledData"/> for new projects.
    /// </summary>
    public class LexiconData : ScriptableObject
    {
        /// <summary>
        /// The asset identifier for this data set, used by the registry to identify the default
        /// fallback asset when the name or ID equals <c>"default"</c> (case-insensitive).
        /// </summary>
        public string assetId;

        /// <summary>
        /// The BCP 47 culture codes this asset provides entries for (e.g. <c>"en-GB"</c>, <c>"fr"</c>).
        /// All entries are registered under every culture listed here.
        /// </summary>
        public List<string> cultures = new();

        /// <summary>
        /// When <see langword="true"/>, the asset automatically registers with <see cref="LexiconRegistry"/>
        /// on <c>OnEnable</c> at runtime and unregisters on <c>OnDisable</c>.
        /// </summary>
        public bool autoRegister = true;

        /// <summary>
        /// A single key-value entry within a <see cref="LexiconData"/> asset, holding either a
        /// localised string or a reference to a localised asset.
        /// </summary>
        [Serializable]
        public struct Entry
        {
            /// <summary>The dot-path key identifying this entry within the registry.</summary>
            public string key;

            /// <summary>The content type of this entry, indicating whether it holds a string or an asset reference.</summary>
            public LexiconHintType hint;

            /// <summary>The localised string value, used when <see cref="hint"/> is <see cref="LexiconHintType.String"/>.</summary>
            public string stringValue;

            /// <summary>The localised asset reference, used when <see cref="hint"/> is any non-string value.</summary>
            public Object assetValue;
        }

        /// <summary>The complete list of localisation entries stored in this asset.</summary>
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

        /// <summary>
        /// Registers this asset's entries with the <see cref="LexiconRegistry"/> under all listed cultures.
        /// Safe to call manually when <see cref="autoRegister"/> is <see langword="false"/>.
        /// </summary>
        public void Register() => LexiconRegistry.Register(this);

        /// <summary>
        /// Removes this asset's entries from the <see cref="LexiconRegistry"/> and rebuilds the culture tables.
        /// Safe to call manually when <see cref="autoRegister"/> is <see langword="false"/>.
        /// </summary>
        public void Unregister() => LexiconRegistry.Unregister(this);

        /// <summary>
        /// Validates the entries in this asset and returns a list of human-readable error messages.
        /// Checks for empty keys, duplicate keys, empty string values, duplicate string values,
        /// and null asset references.
        /// </summary>
        /// <returns>
        /// A list of error message strings describing each validation problem found.
        /// Returns an empty list when the asset is valid.
        /// </returns>
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
