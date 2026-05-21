#if UNITY_ENTITIES
using Unity.Collections;
using Unity.Entities;

namespace Heathen.Lexicon
{
    public struct LexiconStringEntry
    {
        public ulong Hash;
        public FixedString512Bytes Value;
    }

    // Sorted by Hash for O(log n) binary search from Burst systems.
    // String entries only — asset refs cannot live in Burst/ECS.
    public struct LexiconStringBlob
    {
        public BlobArray<LexiconStringEntry> Entries;
    }

    public struct LexiconStringBlobComponent : IComponentData
    {
        public BlobAssetReference<LexiconStringBlob> Value;
    }
}
#endif
