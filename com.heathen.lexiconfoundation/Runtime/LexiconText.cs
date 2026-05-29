using System;
using UnityEngine;

namespace Heathen.Lexicon
{
    [Serializable]
    public class LexiconText
    {
        public LexiconLocMode Mode = LexiconLocMode.Literal;
        [SerializeField] private string _keyOrValue;

        private ulong _cachedHash;

        public string KeyOrValue
        {
            get => _keyOrValue;
            set { _keyOrValue = value; _cachedHash = 0; }
        }

        public bool IsLocalised => Mode == LexiconLocMode.Localised;
        public bool IsLiteral   => Mode == LexiconLocMode.Literal;
        public bool IsInvariant => Mode == LexiconLocMode.Invariant;

        // Only meaningful in Localised mode. 0 = needs recompute (treated as empty key).
        public ulong GetHash()
        {
            if (_cachedHash == 0 && !string.IsNullOrEmpty(_keyOrValue))
                _cachedHash = LexiconRegistry.Hash(_keyOrValue);
            return _cachedHash;
        }

        public void InvalidateHash() => _cachedHash = 0;

        // Returns the active-culture string (Localised) or _keyOrValue directly (Literal/Invariant).
        public string Resolve()
        {
            if (Mode == LexiconLocMode.Localised)
                return LexiconRegistry.ResolveString(GetHash()) ?? _keyOrValue;
            return _keyOrValue;
        }

        public static implicit operator string(LexiconText lt) => lt?.Resolve();
    }
}
