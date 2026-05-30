using System;
using UnityEngine;

namespace Heathen.Lexicon
{
    [Serializable]
    public class LexiconPrefab
    {
        public LexiconLocMode Mode = LexiconLocMode.Literal;
        [SerializeField] private string     _key;
        [SerializeField] private GameObject _literalPrefab;

        private ulong _cachedHash;

        public string Key
        {
            get => _key;
            set { _key = value; _cachedHash = 0; }
        }

        public GameObject LiteralPrefab
        {
            get => _literalPrefab;
            set => _literalPrefab = value;
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

        public GameObject Resolve()
        {
            if (Mode == LexiconLocMode.Localised)
                return LexiconRegistry.ResolveAsset(GetHash()) as GameObject ?? _literalPrefab;
            return _literalPrefab;
        }

        public static implicit operator GameObject(LexiconPrefab lp) => lp?.Resolve();
    }
}
