using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Heathen.Lexicon.Editor
{
    /// <summary>
    /// Guarantees the always-present Default Lexicon asset is created and added to PlayerSettings →
    /// Preloaded Assets before every player build, so it is baked into the build and registers itself at
    /// startup. This is what makes the Default the system's reliable last-resort fallback at runtime.
    /// </summary>
    internal sealed class LexiconBuildProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => LexiconDataEditor.EnsureDefaultPreloaded();
    }
}
