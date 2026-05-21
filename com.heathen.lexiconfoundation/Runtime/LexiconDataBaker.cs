#if UNITY_ENTITIES
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Heathen.Lexicon
{
    public class LexiconDataAuthoring : MonoBehaviour
    {
        public LexiconData Data;
    }

    public class LexiconDataBaker : Baker<LexiconDataAuthoring>
    {
        public override void Bake(LexiconDataAuthoring authoring)
        {
            if (authoring.Data == null) return;

            var entity = GetEntity(TransformUsageFlags.None);

            var pairs = new List<(ulong hash, string value)>();
            foreach (var entry in authoring.Data.entries)
            {
                if (entry.hint != LexiconHintType.String) continue;
                if (string.IsNullOrWhiteSpace(entry.key)) continue;
                pairs.Add((LexiconRegistry.Hash(entry.key), entry.stringValue ?? ""));
            }

            pairs.Sort((a, b) => a.hash.CompareTo(b.hash));

            var builder = new BlobBuilder(Allocator.Temp);
            ref var blob = ref builder.ConstructRoot<LexiconStringBlob>();
            var arr = builder.Allocate(ref blob.Entries, pairs.Count);
            for (int i = 0; i < pairs.Count; i++)
            {
                arr[i].Hash = pairs[i].hash;
                var bytes = System.Text.Encoding.UTF8.GetBytes(pairs[i].value);
                var s = bytes.Length > 510
                    ? System.Text.Encoding.UTF8.GetString(bytes, 0, 510)
                    : pairs[i].value;
                arr[i].Value = new FixedString512Bytes(s);
            }

            var blobRef = builder.CreateBlobAssetReference<LexiconStringBlob>(Allocator.Persistent);
            builder.Dispose();

            AddBlobAsset(ref blobRef, out _);
            AddComponent(entity, new LexiconStringBlobComponent { Value = blobRef });
        }
    }
}
#endif
