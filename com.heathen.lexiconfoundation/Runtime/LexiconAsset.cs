using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Heathen.Lexicon
{
    [Serializable]
    public class LexiconAsset
    {
        public LexiconLocMode Mode = LexiconLocMode.Literal;
        public LexiconHintType Hint = LexiconHintType.Asset;
        [SerializeField] private string _key;
        [SerializeField] private Object _literalAsset;

        private ulong _cachedHash;

        public string Key
        {
            get => _key;
            set { _key = value; _cachedHash = 0; }
        }

        public Object LiteralAsset
        {
            get => _literalAsset;
            set => _literalAsset = value;
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

        // Returns the active-culture asset (Localised) or _literalAsset directly (Literal/Invariant).
        public Object Resolve()
        {
            if (Mode == LexiconLocMode.Localised)
                return LexiconRegistry.ResolveAsset(GetHash()) ?? _literalAsset;
            return _literalAsset;
        }
    }
}
