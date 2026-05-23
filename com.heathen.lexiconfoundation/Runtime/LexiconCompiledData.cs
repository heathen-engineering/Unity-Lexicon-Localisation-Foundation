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
    [Serializable]
    public struct CompiledLexiconEntry
    {
        public ulong           Hash;        // XXH3 hash of Key (same algorithm as O3DE)
        public string          Key;         // dot-path string, for debug display
        public LexiconHintType Hint;
        public string          StringValue;
        public Object          AssetValue;  // null for string entries
    }

    public class LexiconCompiledData : ScriptableObject
    {
        public string                 AssetId;
        public string[]               Cultures;
        public bool                   AutoRegister = true;
        public CompiledLexiconEntry[] Entries;
    }
}
