using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace Heathen.Lexicon.Editor
{
    /// <summary>
    /// Marks assets addressable so that content authored as JSON or baked to code can ship and load by GUID at
    /// runtime. A bare GUID string in baked code is invisible to Unity's build dependency walker, so without an
    /// addressable entry the asset is stripped and unresolvable. This helper gives each referenced asset an
    /// addressable entry whose address is its GUID, matching the runtime <see cref="LexiconAssetLoader"/> which
    /// loads by GUID. Lexicon owns asset delivery, so every Heathen tool routes its build-time marking here.
    /// </summary>
    public static class LexiconAddressables
    {
        /// <summary>The Addressables group that holds GUID-keyed Heathen content entries.</summary>
        public const string GroupName = "Heathen Lexicon Content";

        private static bool _dirty;

        /// <summary>
        /// Ensures the asset with the given GUID has an addressable entry whose address is the GUID. Creates the
        /// default Addressables settings and the Heathen content group on first use. Idempotent: existing entries
        /// are left in place (only a mismatched address is corrected). Call <see cref="Save"/> afterwards to persist.
        /// </summary>
        /// <param name="guid">The asset GUID to mark addressable.</param>
        /// <returns><c>true</c> when an entry was created or its address corrected; otherwise <c>false</c>.</returns>
        public static bool EnsureAddressable(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return false;

            // Create the default settings asset if the project has none yet (the user opted into Addressables
            // by depending on it). Only happens the first time content with assets is built.
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null) return false;

            var existing = settings.FindAssetEntry(guid);
            if (existing != null)
            {
                if (existing.address == guid) return false;
                existing.SetAddress(guid);
                _dirty = true;
                return true;
            }

            var group = settings.FindGroup(GroupName) ?? settings.DefaultGroup;
            if (group == null) return false;

            var entry = settings.CreateOrMoveEntry(guid, group, readOnly: false, postEvent: false);
            if (entry == null) return false;
            entry.SetAddress(guid);
            _dirty = true;
            return true;
        }

        /// <summary>Persists pending addressable changes to disk. No-op when nothing changed since the last save.</summary>
        public static void Save()
        {
            if (!_dirty) return;
            _dirty = false;
            AssetDatabase.SaveAssets();
        }
    }
}
