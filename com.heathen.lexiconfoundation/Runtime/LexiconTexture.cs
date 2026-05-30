using System;
using UnityEngine;

namespace Heathen.Lexicon
{
    [Serializable]
    public class LexiconTexture
    {
        public LexiconLocMode Mode = LexiconLocMode.Literal;
        [SerializeField] private string    _key;
        [SerializeField] private Texture2D _literalTexture;

        private ulong _cachedHash;

        public string Key
        {
            get => _key;
            set { _key = value; _cachedHash = 0; }
        }

        public Texture2D LiteralTexture
        {
            get => _literalTexture;
            set => _literalTexture = value;
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

        public Texture2D Resolve()
        {
            if (Mode == LexiconLocMode.Localised)
                return LexiconRegistry.ResolveAsset(GetHash()) as Texture2D ?? _literalTexture;
            return _literalTexture;
        }

        public static implicit operator Texture2D(LexiconTexture lt) => lt?.Resolve();
        public static implicit operator Texture(LexiconTexture lt)   => lt?.Resolve();
    }
}
