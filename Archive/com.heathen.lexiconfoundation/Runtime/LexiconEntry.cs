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
        /// or the editor); baked/streamed content carries <see cref="assetGuid"/> instead and resolves through
        /// the Addressables seam (<see cref="LexiconAssetLoader"/>) on demand.
        /// </summary>
        public Object assetValue;

        /// <summary>
        /// The GUID of the localised asset, for streamed content where a live <see cref="assetValue"/> cannot be
        /// serialised (baked code or JSON sources). When set and <see cref="assetValue"/> is <c>null</c>, the
        /// entry resolves through the Addressables seam; acquire it with
        /// <see cref="LexiconRegistry.AcquireAsset(string,string)"/> to stream it resident. Empty for entries
        /// that carry a direct reference.
        /// </summary>
        public string assetGuid;

        /// <summary>
        /// The sub-asset name within <see cref="assetGuid"/> (e.g. a named sprite in a sprite sheet), or
        /// <c>null</c>/empty for the main asset. Paired with <see cref="assetGuid"/> when resolving through the
        /// Addressables seam.
        /// </summary>
        public string assetSubName;
    }
}
