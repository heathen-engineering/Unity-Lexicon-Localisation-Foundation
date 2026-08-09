using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace Heathen.Lexicon.Editor
{
    /// <summary>
    /// Marks assets addressable so that content authored as JSON can ship and load by GUID at runtime. A bare
    /// GUID string in JSON is invisible to Unity's build dependency walker, so without an addressable entry the
    /// asset is stripped and unresolvable. This helper gives each referenced asset an addressable entry whose
    /// address is its GUID, matching the runtime <see cref="LexiconAssetLoader"/> which loads by GUID; and it
    /// labels each shipped <c>.helex</c> TextAsset so the runtime can load every localisation source with one
    /// labelled load (<see cref="LexiconRegistry.LoadShippedSourcesAsync"/>). Lexicon owns asset delivery, so
    /// every Heathen tool routes its build-time marking here.
    /// </summary>
    public static class LexiconAddressables
    {
        /// <summary>The default Addressables group that holds GUID-keyed Heathen content entries.</summary>
        public const string GroupName = "Heathen Lexicon Content";

        private static bool _dirty;

        /// <summary>
        /// Ensures the asset with the given GUID has an addressable entry whose address is the GUID, in the
        /// named group, optionally tagged with a label. Creates the default Addressables settings and the group
        /// on first use. Idempotent: an existing entry's address/group/label are corrected but not duplicated.
        /// Call <see cref="Save"/> afterwards to persist.
        /// </summary>
        /// <param name="guid">The asset GUID to mark addressable.</param>
        /// <param name="groupName">The Addressables group to place the entry in (created if missing).</param>
        /// <param name="label">An optional label to apply to the entry (created in settings if missing).</param>
        /// <returns><c>true</c> when an entry was created or changed; otherwise <c>false</c>.</returns>
        public static bool EnsureAddressable(string guid, string groupName = GroupName, string label = null)
        {
            if (string.IsNullOrEmpty(guid)) return false;

            // Create the default settings asset if the project has none yet (the user opted into Addressables
            // by depending on it). Only happens the first time content is marked.
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null) return false;

            var group = GetOrCreateGroup(settings, groupName);
            if (group == null) return false;

            bool changed = false;

            var entry = settings.FindAssetEntry(guid);
            if (entry == null)
            {
                entry = settings.CreateOrMoveEntry(guid, group, readOnly: false, postEvent: false);
                if (entry == null) return false;
                changed = true;
            }
            else if (entry.parentGroup != group)
            {
                settings.MoveEntry(entry, group, readOnly: false, postEvent: false);
                changed = true;
            }

            if (entry.address != guid) { entry.SetAddress(guid); changed = true; }

            if (!string.IsNullOrEmpty(label))
            {
                settings.AddLabel(label); // idempotent
                if (!entry.labels.Contains(label)) { entry.SetLabel(label, true, force: true); changed = true; }
            }

            if (changed) _dirty = true;
            return changed;
        }

        /// <summary>
        /// Marks every <c>.helex</c> source in the project ready to ship: each <c>.helex</c> TextAsset is placed
        /// in a per-culture group and labelled for runtime discovery, and every asset it references is marked
        /// addressable (by GUID) in the same group so a language pack bundles its own assets. Called at build
        /// time by <see cref="LexiconBuildProcessor"/>.
        /// </summary>
        public static void MarkAllForBuild()
        {
            foreach (var path in LexiconSettingsProvider.FindHelexPaths())
            {
                var helexGuid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(helexGuid)) continue;

                HelexDocument doc;
                try { doc = LexiconSettingsProvider.ReadHelexDoc(path); }
                catch { continue; }

                var group = GroupNameForCulture(doc.Cultures != null && doc.Cultures.Count > 0 ? doc.Cultures[0] : null);

                // The .helex TextAsset itself: labelled so the runtime loads all sources with one call.
                EnsureAddressable(helexGuid, group, LexiconRegistry.AddressablesLabel);

                // Its referenced assets: addressable by GUID (no label — they load on demand by address), in
                // the same per-culture group so they ship and stream with the language.
                foreach (var e in doc.Entries)
                {
                    if (e.Hint == LexiconHintType.String || string.IsNullOrEmpty(e.AssetPath)) continue;
                    var g = AssetDatabase.AssetPathToGUID(e.AssetPath);
                    if (!string.IsNullOrEmpty(g)) EnsureAddressable(g, group);
                }
            }
            Save();
        }

        /// <summary>Persists pending addressable changes to disk. No-op when nothing changed since the last save.</summary>
        public static void Save()
        {
            if (!_dirty) return;
            _dirty = false;
            AssetDatabase.SaveAssets();
        }

        private static string GroupNameForCulture(string culture) =>
            string.IsNullOrWhiteSpace(culture) ? "Lexicon Default" : $"Lexicon {culture}";

        private static AddressableAssetGroup GetOrCreateGroup(AddressableAssetSettings settings, string groupName)
        {
            if (string.IsNullOrEmpty(groupName)) return settings.DefaultGroup;
            var group = settings.FindGroup(groupName);
            if (group != null) return group;

            // A standard bundled content group (same schema set Addressables' own "Create Group" uses).
            return settings.CreateGroup(groupName, setAsDefaultGroup: false, readOnly: false, postEvent: false,
                schemasToCopy: null,
                types: new[] { typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema) })
                ?? settings.DefaultGroup;
        }
    }
}
