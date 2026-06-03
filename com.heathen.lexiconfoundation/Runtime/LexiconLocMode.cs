namespace Heathen.Lexicon
{
    /// <summary>
    /// Controls how a Lexicon field resolves its value at runtime, allowing each field
    /// to independently opt into localisation, bypass it with a literal value, or be
    /// permanently culture-neutral.
    /// </summary>
    public enum LexiconLocMode : byte
    {
        /// <summary>
        /// The field resolves its value from the active culture via <see cref="LexiconRegistry"/>,
        /// falling back to the literal asset or string when no entry is found.
        /// </summary>
        Localised = 0,
        /// <summary>
        /// The field always returns the directly assigned asset or string, ignoring culture.
        /// This is the default mode for new fields.
        /// </summary>
        Literal = 1,
        /// <summary>
        /// The field holds a culture-neutral value that is never overridden by localisation,
        /// even when a matching key exists in the registry.
        /// </summary>
        Invariant = 2
    }
}
