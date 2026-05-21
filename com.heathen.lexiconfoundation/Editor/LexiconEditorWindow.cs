using UnityEditor;

namespace Heathen.Lexicon.Editor
{
    internal static class LexiconMenu
    {
        [MenuItem("Window/Heathen/Localisation Lexicon")]
        public static void Open() =>
            SettingsService.OpenProjectSettings("Project/Localisation Lexicon");
    }
}
