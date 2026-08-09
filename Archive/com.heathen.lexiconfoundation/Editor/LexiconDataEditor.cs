using UnityEditor;

namespace Heathen.Lexicon.Editor
{
    [InitializeOnLoad]
    public static class LexiconDataEditor
    {
        static LexiconDataEditor()
        {
            EditorApplication.delayCall += ForceRefresh;
        }

        /// <summary>
        /// Ensures a Default <c>.helex</c> exists and re-discovers all <c>.helex</c> sources into the registry
        /// for edit-mode resolution. The Default now ships via its Addressables label (marked at build time by
        /// <see cref="LexiconAddressables.MarkAllForBuild"/>), so no PlayerSettings preload is required.
        /// </summary>
        public static void ForceRefresh()
        {
            LexiconSettingsProvider.GetOrCreateDefault();
            LexiconSourceRefresh.Refresh();
        }
    }
}
