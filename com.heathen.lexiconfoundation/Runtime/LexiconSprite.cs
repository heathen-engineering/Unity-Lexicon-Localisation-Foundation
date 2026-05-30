using System;
using UnityEngine;

namespace Heathen.Lexicon
{
    [Serializable]
    public class LexiconSprite
    {
        public LexiconLocMode Mode = LexiconLocMode.Literal;
        [SerializeField] private string _key;
        [SerializeField] private Sprite _literalSprite;

        private ulong _cachedHash;

        public string Key
        {
            get => _key;
            set { _key = value; _cachedHash = 0; }
        }

        public Sprite LiteralSprite
        {
            get => _literalSprite;
            set => _literalSprite = value;
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

        public Sprite Resolve()
        {
            if (Mode == LexiconLocMode.Localised)
                return LexiconRegistry.ResolveAsset(GetHash()) as Sprite ?? _literalSprite;
            return _literalSprite;
        }

        public static implicit operator Sprite(LexiconSprite ls) => ls?.Resolve();
    }
}
