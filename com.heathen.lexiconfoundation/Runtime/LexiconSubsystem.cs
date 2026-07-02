using System.Collections.Generic;
using System.Linq;
using Heathen;

namespace Heathen.Lexicon
{
    /// <summary>
    /// Global framework subsystem that owns the <see cref="LexiconRegistry"/> session lifecycle. It runs the
    /// per-session reset (and re-discovery of registered Lexicon sources) at framework boot
    /// (<see cref="SubsystemScope.Global"/> subsystems initialise at <c>RuntimeInitializeLoadType.SubsystemRegistration</c>),
    /// replacing the registry's former ad-hoc <c>[RuntimeInitializeOnLoadMethod]</c> bootstrap so the lifecycle
    /// is framework-managed and other subsystems can order against it via <c>DependsOn</c>.
    /// <para>The static <see cref="LexiconRegistry"/> remains the ergonomic facade for string/asset resolution;
    /// this subsystem only governs when the registry is reset.</para>
    /// </summary>
    [Subsystem(SubsystemScope.Global)]
    public sealed class LexiconSubsystem : Subsystem, ISubsystemDebug
    {
        /// <summary>
        /// Resets the registry to a clean session and (re-)discovers the registered Lexicon sources. In the
        /// editor discovery is synchronous (AssetDatabase, done inside <see cref="LexiconRegistry.ResetForSession"/>);
        /// in a player the shipped <c>.helex</c> TextAssets stream in asynchronously via Addressables.
        /// </summary>
        protected override void Initialize()
        {
            LexiconRegistry.ResetForSession();
#if !UNITY_EDITOR
            LexiconRegistry.LoadShippedSourcesAsync();
#endif
        }

        /// <inheritdoc/>
        public IEnumerable<(string label, string value)> GetDebugInfo()
        {
            yield return ("Active culture",  LexiconRegistry.GetActiveCulture()  ?? "(none)");
            yield return ("Default culture", LexiconRegistry.GetDefaultCulture() ?? "(none)");
            yield return ("Cultures",        LexiconRegistry.GetMappedCultureCodes().Count().ToString());
            yield return ("Registered sources", LexiconRegistry.RegisteredSourceCount.ToString());
        }
    }
}
