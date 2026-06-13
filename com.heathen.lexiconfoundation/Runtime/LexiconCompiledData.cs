using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Heathen.Lexicon
{
    // Compiled output produced by LexiconImporter from a .helex source file.
    // All keys are pre-hashed at import time — zero hashing work at runtime registration.
    //
    // Source: MyStrings.helex (JSON, human-authored, cross-engine, source-controlled)
    // Output: MyStrings.helex sub-asset (this ScriptableObject, drag-and-drop ready)

    /// <summary>
    /// A single pre-compiled entry produced by <see cref="LexiconImporter"/> from a <c>.helex</c> source file.
    /// The key is stored as both a dot-path string (for debugging) and a pre-computed XXH3 hash
    /// so that registration at runtime requires no hashing work.
    /// </summary>
    [Serializable]
    public struct CompiledLexiconEntry
    {
        /// <summary>XXH3 hash of <see cref="Key"/>, computed at import time using the same algorithm as O3DE.</summary>
        public ulong           Hash;

        /// <summary>The dot-path string key as authored in the <c>.helex</c> source, retained for debug display.</summary>
        public string          Key;

        /// <summary>The content type of this entry, indicating whether it holds a string or an asset reference.</summary>
        public LexiconHintType Hint;

        /// <summary>The localised string value for entries where <see cref="Hint"/> is <see cref="LexiconHintType.String"/>.</summary>
        public string          StringValue;

        /// <summary>
        /// The asset reference for non-string entries. This field is <see langword="null"/> for string entries.
        /// </summary>
        public Object          AssetValue;
    }

    /// <summary>
    /// A <see cref="ScriptableObject"/> that holds all entries compiled from a single <c>.helex</c> source file.
    /// Instances are created automatically by <see cref="LexiconImporter"/> and registered with
    /// <see cref="LexiconRegistry"/> on load when <see cref="AutoRegister"/> is <see langword="true"/>.
    /// </summary>
    public class LexiconCompiledData : ScriptableObject
    {
        /// <summary>
        /// The asset identifier declared in the <c>.helex</c> source file, used to match this asset
        /// against localised display names and to identify the default fallback asset.
        /// </summary>
        public string                 AssetId;

        /// <summary>
        /// The BCP 47 culture codes this asset provides entries for (e.g. <c>"en-GB"</c>, <c>"fr"</c>).
        /// Each culture maps all <see cref="Entries"/> into the registry under that culture code.
        /// </summary>
        public string[]               Cultures;

        /// <summary>
        /// When <see langword="true"/>, this asset registers itself with <see cref="LexiconRegistry"/>
        /// automatically on load and re-registers each time the Editor refreshes compiled data.
        /// </summary>
        public bool                   AutoRegister = true;

        /// <summary>
        /// The compiled entries read from the <c>.helex</c> source, with keys pre-hashed at import time.
        /// </summary>
        public CompiledLexiconEntry[] Entries;

        private void OnEnable()
        {
#if !UNITY_EDITOR
            // Self-register whenever the asset loads at runtime — including PlayerSettings-preloaded assets
            // (such as the Default) that may load after the registry's subsystem-registration pass.
            // Editor-time registration is handled by LexiconCompiledDataRefresh.
            if (AutoRegister)
                LexiconRegistry.Register(this);
#endif
        }

        private void OnDisable()
        {
#if !UNITY_EDITOR
            // The registry refuses to unregister the Default, so this only releases optional language packs.
            if (AutoRegister)
                LexiconRegistry.Unregister(this);
#endif
        }
    }
}
