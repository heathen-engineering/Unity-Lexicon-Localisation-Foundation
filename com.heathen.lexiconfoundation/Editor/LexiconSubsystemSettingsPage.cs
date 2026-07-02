using Heathen.Editor;
using System;
using UnityEditor;

namespace Heathen.Lexicon.Editor
{
    /// <summary>
    /// Links the Localisation Lexicon subsystem's card header on Project ▸ Subsystems to its settings page, and
    /// supplies the documentation URL for the header help button.
    /// </summary>
    public sealed class LexiconSubsystemSettingsPage : ISubsystemSettingsPage, ISubsystemDocumentation
    {
        public Type SubsystemType => typeof(LexiconSubsystem);

        public void Open() => SettingsService.OpenProjectSettings("Project/Subsystems/Localisation Lexicon");
        public string DocumentationUrl => "https://heathen.group/kb/lexicon-welcome/";
    }
}
