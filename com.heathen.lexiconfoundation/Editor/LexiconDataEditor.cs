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
        }
    }
}
