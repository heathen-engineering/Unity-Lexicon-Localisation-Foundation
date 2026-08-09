using System;
using UnityEngine;

namespace Heathen.Lexicon
{
    /// <summary>
    /// A serialisable field wrapper that holds a reference to an <see cref="AudioClip"/>
    /// and can resolve it from the active culture via <see cref="LexiconRegistry"/> when set to
    /// <see cref="LexiconLocMode.Localised"/> mode.
    /// </summary>
    [Serializable]
    public class LexiconSound
    {
        /// <summary>
        /// Determines how the field resolves its value: via the active culture registry,
        /// a directly assigned clip, or a culture-neutral invariant.
        /// </summary>
        public LexiconLocMode Mode = LexiconLocMode.Literal;

        [SerializeField] private string _key;
        [SerializeField] private AudioClip _literalClip;

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
        /// The directly assigned clip used when the field is in <see cref="LexiconLocMode.Literal"/>
        /// or <see cref="LexiconLocMode.Invariant"/> mode, and as the fallback in
        /// <see cref="LexiconLocMode.Localised"/> mode when no registry entry is found.
        /// </summary>
        public AudioClip LiteralClip
        {
            get => _literalClip;
            set => _literalClip = value;
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
        /// Returns the active-culture clip when in <see cref="LexiconLocMode.Localised"/> mode,
        /// falling back to <see cref="LiteralClip"/> if the registry has no matching entry.
        /// Returns <see cref="LiteralClip"/> directly in all other modes.
        /// </summary>
        /// <returns>The resolved <see cref="AudioClip"/> for the current culture, or the literal clip.</returns>
        public AudioClip Resolve()
        {
            if (Mode == LexiconLocMode.Localised)
                return LexiconRegistry.ResolveSound(GetHash()) ?? _literalClip;
            return _literalClip;
        }

        /// <summary>
        /// Implicitly converts a <see cref="LexiconSound"/> to an <see cref="AudioClip"/>
        /// by calling <see cref="Resolve"/>, enabling direct assignment without an explicit cast.
        /// </summary>
        /// <param name="ls">The <see cref="LexiconSound"/> to resolve.</param>
        /// <returns>The resolved clip, or <see langword="null"/> if <paramref name="ls"/> is <see langword="null"/>.</returns>
        public static implicit operator AudioClip(LexiconSound ls) => ls?.Resolve();
    }
}
