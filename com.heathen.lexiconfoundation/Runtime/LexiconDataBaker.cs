#if UNITY_ENTITIES
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Heathen.Lexicon
{
    /// <summary>
    /// An authoring <see cref="MonoBehaviour"/> that holds a reference to a <see cref="LexiconCompiledData"/>
    /// asset (the import output of a <c>.helex</c>) for baking into the ECS world. Attach this component to a
    /// GameObject in a subscene and assign the desired asset to convert its string entries into a
    /// <see cref="LexiconStringBlobComponent"/> at bake time.
    /// </summary>
    public class LexiconDataAuthoring : MonoBehaviour
    {
        /// <summary>The compiled asset whose string entries will be baked into a blob asset.</summary>
        public LexiconCompiledData Data;
    }

    /// <summary>
    /// ECS baker that converts a <see cref="LexiconDataAuthoring"/> component into a
    /// <see cref="LexiconStringBlobComponent"/> containing a sorted blob array of all string entries.
    /// Asset entries are excluded because <see cref="UnityEngine.Object"/> references cannot live in Burst or ECS.
    /// </summary>
    public class LexiconDataBaker : Baker<LexiconDataAuthoring>
    {
        /// <summary>
        /// Bakes the string entries from <see cref="LexiconDataAuthoring.Data"/> into a
        /// <see cref="LexiconStringBlobComponent"/> attached to the baked entity. Entries are sorted
        /// by hash to enable O(log n) binary search from Burst systems.
        /// </summary>
        /// <param name="authoring">The authoring component supplying the <see cref="LexiconData"/> to bake.</param>
        public override void Bake(LexiconDataAuthoring authoring)
        {
            if (authoring.Data == null) return;

            var entity = GetEntity(TransformUsageFlags.None);

            var pairs = new List<(ulong hash, string value)>();
            foreach (var entry in authoring.Data.Entries)
            {
                if (entry.Hint != LexiconHintType.String) continue;
                pairs.Add((entry.Hash, entry.StringValue ?? ""));
            }

            pairs.Sort((a, b) => a.hash.CompareTo(b.hash));

            var builder = new BlobBuilder(Allocator.Temp);
            ref var blob = ref builder.ConstructRoot<LexiconStringBlob>();
            var arr = builder.Allocate(ref blob.Entries, pairs.Count);
            for (int i = 0; i < pairs.Count; i++)
            {
                arr[i].Hash  = pairs[i].hash;
                arr[i].Value = new FixedString512Bytes(LexiconRegistry.TruncateUtf8(pairs[i].value, 510));
            }

            var blobRef = builder.CreateBlobAssetReference<LexiconStringBlob>(Allocator.Persistent);
            builder.Dispose();

            AddBlobAsset(ref blobRef, out _);
            AddComponent(entity, new LexiconStringBlobComponent { Value = blobRef });
        }
    }
}
#endif
