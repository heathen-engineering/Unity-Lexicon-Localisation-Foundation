using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Heathen.Lexicon
{
    /// <summary>
    /// A serialisable field wrapper that holds a reference to a generic <see cref="UnityEngine.Object"/> asset
    /// and can resolve it from the active culture via <see cref="LexiconRegistry"/> when set to
    /// <see cref="LexiconLocMode.Localised"/> mode.
    /// </summary>
    [Serializable]
    public class LexiconAsset
    {
        /// <summary>
        /// Determines how the field resolves its value: via the active culture registry,
        /// a directly assigned asset, or a culture-neutral invariant.
        /// </summary>
        public LexiconLocMode Mode = LexiconLocMode.Literal;

        /// <summary>
        /// Specifies the expected asset type for this entry, used by the Inspector drawer
        /// to filter the object picker and resolve the correct type from the registry.
        /// </summary>
        public LexiconHintType Hint = LexiconHintType.Asset;

        [SerializeField] private string _key;
        [SerializeField] private Object _literalAsset;

        private ulong _cachedHash;

        /// <summary>
        /// The dot-path key used to look up this entry in the <see cref="LexiconRegistry"/>
        /// when the field is in <see cref="LexiconLocMode.Localised"/> mode.
        /// Setting this property clears the cached hash.
        /// </summary>
        public string Key
        {
            get => _key;
            set { _key = value; _cachedHash = 0; }
        }

        /// <summary>
        /// The directly assigned asset used when the field is in <see cref="LexiconLocMode.Literal"/>
        /// or <see cref="LexiconLocMode.Invariant"/> mode, and as the fallback in
        /// <see cref="LexiconLocMode.Localised"/> mode when no registry entry is found.
        /// </summary>
        public Object LiteralAsset
        {
            get => _literalAsset;
            set => _literalAsset = value;
        }

        /// <summary>Gets a value indicating whether this field is set to <see cref="LexiconLocMode.Localised"/> mode.</summary>
        public bool IsLocalised => Mode == LexiconLocMode.Localised;

        /// <summary>Gets a value indicating whether this field is set to <see cref="LexiconLocMode.Literal"/> mode.</summary>
        public bool IsLiteral   => Mode == LexiconLocMode.Literal;

        /// <summary>Gets a value indicating whether this field is set to <see cref="LexiconLocMode.Invariant"/> mode.</summary>
        public bool IsInvariant => Mode == LexiconLocMode.Invariant;

        /// <summary>
        /// Returns the XXH3 hash of <see cref="Key"/>, computing and caching it on first call.
        /// Returns zero when the key is null or empty.
        /// </summary>
        /// <returns>The cached hash of the dot-path key, or zero if no key is set.</returns>
        public ulong GetHash()
        {
            if (_cachedHash == 0 && !string.IsNullOrEmpty(_key))
                _cachedHash = LexiconRegistry.Hash(_key);
            return _cachedHash;
        }

        /// <summary>
        /// Forces the hash to be recomputed on the next call to <see cref="GetHash"/>.
        /// Call this after modifying the key string directly in the backing field.
        /// </summary>
        public void InvalidateHash() => _cachedHash = 0;

        /// <summary>
        /// Returns the active-culture asset when in <see cref="LexiconLocMode.Localised"/> mode,
        /// falling back to <see cref="LiteralAsset"/> if the registry has no matching entry.
        /// Returns <see cref="LiteralAsset"/> directly in all other modes.
        /// </summary>
        /// <returns>The resolved <see cref="UnityEngine.Object"/> for the current culture, or the literal asset.</returns>
        public Object Resolve()
        {
            if (Mode == LexiconLocMode.Localised)
                return LexiconRegistry.ResolveAsset(GetHash()) ?? _literalAsset;
            return _literalAsset;
        }

        /// <summary>
        /// Implicitly converts a <see cref="LexiconAsset"/> to a <see cref="UnityEngine.Object"/>
        /// by calling <see cref="Resolve"/>, enabling direct assignment without an explicit cast.
        /// </summary>
        /// <param name="la">The <see cref="LexiconAsset"/> to resolve.</param>
        /// <returns>The resolved asset, or <see langword="null"/> if <paramref name="la"/> is <see langword="null"/>.</returns>
        public static implicit operator Object(LexiconAsset la) => la?.Resolve();
    }
}
