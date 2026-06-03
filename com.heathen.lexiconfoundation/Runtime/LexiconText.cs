using System;
using UnityEngine;

namespace Heathen.Lexicon
{
    /// <summary>
    /// A serialisable field wrapper that holds a localised string and can resolve it from
    /// the active culture via <see cref="LexiconRegistry"/> when set to
    /// <see cref="LexiconLocMode.Localised"/> mode. In other modes the stored string is
    /// returned directly as a literal or invariant value.
    /// </summary>
    [Serializable]
    public class LexiconText
    {
        /// <summary>
        /// Determines how the field resolves its value: via the active culture registry,
        /// a directly authored literal string, or a culture-neutral invariant string.
        /// </summary>
        public LexiconLocMode Mode = LexiconLocMode.Literal;

        [SerializeField] private string _keyOrValue;

        private ulong _cachedHash;

        /// <summary>
        /// In <see cref="LexiconLocMode.Localised"/> mode this holds the dot-path lookup key.
        /// In <see cref="LexiconLocMode.Literal"/> and <see cref="LexiconLocMode.Invariant"/> modes
        /// it holds the string value itself. Setting this property clears the cached hash.
        /// </summary>
        public string KeyOrValue
        {
            get => _keyOrValue;
            set { _keyOrValue = value; _cachedHash = 0; }
        }

        /// <summary>Gets a value indicating whether this field is set to <see cref="LexiconLocMode.Localised"/> mode.</summary>
        public bool IsLocalised => Mode == LexiconLocMode.Localised;

        /// <summary>Gets a value indicating whether this field is set to <see cref="LexiconLocMode.Literal"/> mode.</summary>
        public bool IsLiteral   => Mode == LexiconLocMode.Literal;

        /// <summary>Gets a value indicating whether this field is set to <see cref="LexiconLocMode.Invariant"/> mode.</summary>
        public bool IsInvariant => Mode == LexiconLocMode.Invariant;

        /// <summary>
        /// Returns the XXH3 hash of <see cref="KeyOrValue"/>, computing and caching it on first call.
        /// Only meaningful in <see cref="LexiconLocMode.Localised"/> mode. Returns zero when the
        /// key is null or empty.
        /// </summary>
        /// <returns>The cached hash of the dot-path key, or zero if no key is set.</returns>
        public ulong GetHash()
        {
            if (_cachedHash == 0 && !string.IsNullOrEmpty(_keyOrValue))
                _cachedHash = LexiconRegistry.Hash(_keyOrValue);
            return _cachedHash;
        }

        /// <summary>
        /// Forces the hash to be recomputed on the next call to <see cref="GetHash"/>.
        /// Call this after modifying the key string directly in the backing field.
        /// </summary>
        public void InvalidateHash() => _cachedHash = 0;

        /// <summary>
        /// Returns the active-culture string when in <see cref="LexiconLocMode.Localised"/> mode,
        /// falling back to <see cref="KeyOrValue"/> if the registry has no matching entry.
        /// Returns <see cref="KeyOrValue"/> directly in all other modes.
        /// </summary>
        /// <returns>The resolved string for the current culture, or the literal/invariant value.</returns>
        public string Resolve()
        {
            if (Mode == LexiconLocMode.Localised)
                return LexiconRegistry.ResolveString(GetHash()) ?? _keyOrValue;
            return _keyOrValue;
        }

        /// <summary>
        /// Implicitly converts a <see cref="LexiconText"/> to a <see cref="string"/>
        /// by calling <see cref="Resolve"/>, enabling direct string assignment without an explicit cast.
        /// </summary>
        /// <param name="lt">The <see cref="LexiconText"/> to resolve.</param>
        /// <returns>The resolved string, or <see langword="null"/> if <paramref name="lt"/> is <see langword="null"/>.</returns>
        public static implicit operator string(LexiconText lt) => lt?.Resolve();
    }
}
