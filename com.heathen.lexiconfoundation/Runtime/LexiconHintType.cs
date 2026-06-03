namespace Heathen.Lexicon
{
    /// <summary>
    /// Indicates the content type of a Lexicon entry, allowing the registry and drawers
    /// to route resolution to the correct asset or string field.
    /// </summary>
    public enum LexiconHintType : byte
    {
        /// <summary>No type hint assigned; treated as unset.</summary>
        None = 0,
        /// <summary>The entry holds a localised string value.</summary>
        String = 1,
        /// <summary>The entry references an <see cref="UnityEngine.AudioClip"/> asset.</summary>
        Sound = 2,
        /// <summary>The entry references a <see cref="UnityEngine.Texture2D"/> asset.</summary>
        Texture = 3,
        /// <summary>The entry references a <see cref="UnityEngine.Sprite"/> asset.</summary>
        Sprite = 4,
        /// <summary>The entry references a <see cref="UnityEngine.GameObject"/> prefab asset.</summary>
        Prefab = 5,
        /// <summary>The entry references a generic <see cref="UnityEngine.Object"/> asset not covered by the typed values.</summary>
        Asset = 6
    }
}
