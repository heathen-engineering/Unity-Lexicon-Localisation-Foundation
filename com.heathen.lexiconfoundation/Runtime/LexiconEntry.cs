using System;
using Object = UnityEngine.Object;

namespace Heathen.Lexicon
{
    /// <summary>
    /// A single resolved Lexicon entry held in the registry's per-culture tables: either a localised string or
    /// a localised asset reference. This is the runtime entry type (formerly nested in the retired legacy
    /// <c>LexiconData</c> ScriptableObject); it is populated from the compiled <c>.helex</c> sources and from
    /// runtime injections (<see cref="LexiconRegistry.SetString"/> / <see cref="LexiconRegistry.SetAsset"/>).
    /// </summary>
    [Serializable]
    public struct LexiconEntry
    {
        /// <summary>The dot-path key identifying this entry within the registry.</summary>
        public string key;

        /// <summary>The content type of this entry, indicating whether it holds a string or an asset reference.</summary>
        public LexiconHintType hint;

        /// <summary>The localised string value, used when <see cref="hint"/> is <see cref="LexiconHintType.String"/>.</summary>
        public string stringValue;

        /// <summary>
        /// The localised asset reference, used when <see cref="hint"/> is any non-string value. A directly
        /// resolved/injected <see cref="UnityEngine.Object"/> (e.g. via <see cref="LexiconRegistry.SetAsset"/>
        /// or the editor); baked content carries the GUID instead and resolves through the Addressables seam.
        /// </summary>
        public Object assetValue;
    }
}
