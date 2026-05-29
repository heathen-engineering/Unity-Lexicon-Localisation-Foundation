using System;
using UnityEngine;

namespace Heathen.Lexicon
{
    [Serializable]
    public class LexiconSound
    {
        public LexiconLocMode Mode = LexiconLocMode.Literal;
        [SerializeField] private string _key;
        [SerializeField] private AudioClip _literalClip;

        private ulong _cachedHash;

        public string Key
        {
            get => _key;
            set { _key = value; _cachedHash = 0; }
        }

        public AudioClip LiteralClip
        {
            get => _literalClip;
            set => _literalClip = value;
        }

        public bool IsLocalised => Mode == LexiconLocMode.Localised;
        public bool IsLiteral   => Mode == LexiconLocMode.Literal;
        public bool IsInvariant => Mode == LexiconLocMode.Invariant;

        public ulong GetHash()
        {
            if (_cachedHash == 0 && !string.IsNullOrEmpty(_key))
                _cachedHash = LexiconRegistry.Hash(_key);
            return _cachedHash;
        }

        public void InvalidateHash() => _cachedHash = 0;

        // Returns the active-culture clip (Localised) or _literalClip directly (Literal/Invariant).
        public AudioClip Resolve()
        {
            if (Mode == LexiconLocMode.Localised)
                return LexiconRegistry.ResolveSound(GetHash()) ?? _literalClip;
            return _literalClip;
        }

        public static implicit operator AudioClip(LexiconSound ls) => ls?.Resolve();
    }
}
