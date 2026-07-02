#if UNITY_ENTITIES
using Unity.Collections;
using Unity.Entities;

namespace Heathen.Lexicon
{
    /// <summary>
    /// A single string entry stored within a <see cref="LexiconStringBlob"/>, combining an XXH3 hash
    /// with a fixed-length UTF-8 string value for Burst-compatible access.
    /// </summary>
    public struct LexiconStringEntry
    {
        /// <summary>The XXH3 hash of the dot-path key, used for O(log n) binary search within the blob.</summary>
        public ulong Hash;

        /// <summary>The localised string value, truncated to 510 bytes if necessary to fit within the fixed buffer.</summary>
        public FixedString512Bytes Value;
    }

    /// <summary>
    /// A blob asset containing a sorted array of <see cref="LexiconStringEntry"/> values for
    /// Burst-compatible string lookups. Entries are sorted by <see cref="LexiconStringEntry.Hash"/>
    /// to allow O(log n) binary search. Asset references are excluded as they cannot be accessed from Burst.
    /// </summary>
    public struct LexiconStringBlob
    {
        /// <summary>
        /// The sorted array of string entries, built by a consumer-provided baker.
        /// Access this via binary search on <see cref="LexiconStringEntry.Hash"/> from Burst systems.
        /// </summary>
        public BlobArray<LexiconStringEntry> Entries;
    }

    /// <summary>
    /// An ECS component that holds a persistent reference to a <see cref="LexiconStringBlob"/>
    /// blob asset, making localised strings available to Burst-compiled ECS systems.
    /// </summary>
    public struct LexiconStringBlobComponent : IComponentData
    {
        /// <summary>
        /// The reference to the <see cref="LexiconStringBlob"/> blob asset.
        /// Dispose this reference when the owning entity or system is destroyed.
        /// </summary>
        public BlobAssetReference<LexiconStringBlob> Value;
    }
}
#endif
