using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Heathen.Lexicon.Editor
{
    /// <summary>
    /// Marks every <c>.helex</c> source (and the assets it references) addressable before each player build, so
    /// the localisation sources ship, load via their Addressables label at startup, and stream their assets by
    /// GUID at runtime. Replaces the former PlayerSettings-preload of the compiled Default asset.
    /// </summary>
    internal sealed class LexiconBuildProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => LexiconAddressables.MarkAllForBuild();
    }
}
