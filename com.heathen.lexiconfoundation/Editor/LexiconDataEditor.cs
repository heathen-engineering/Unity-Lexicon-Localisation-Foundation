using System.Linq;
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

        public static void ForceRefresh()
        {
            LexiconSettingsProvider.GetOrCreateDefault();
            LexiconCompiledDataRefresh.Refresh();
            EnsureDefaultPreloaded();
        }

        /// <summary>
        /// Guarantees the Default compiled asset is listed in PlayerSettings → Preloaded Assets, so it ships
        /// in every player build and registers itself at startup regardless of where the <c>.helex</c> lives.
        /// Also strips null holes Unity leaves in the list when assets are deleted.
        /// </summary>
        public static void EnsureDefaultPreloaded()
        {
            var path     = LexiconSettingsProvider.GetOrCreateDefault();
            var compiled = AssetDatabase.LoadAssetAtPath<LexiconCompiledData>(path);
            if (compiled == null) return;

            var preloaded = PlayerSettings.GetPreloadedAssets().ToList();
            bool changed  = preloaded.RemoveAll(a => a == null) > 0;
            if (!preloaded.Contains(compiled)) { preloaded.Add(compiled); changed = true; }
            if (changed) PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
        }
    }
}
