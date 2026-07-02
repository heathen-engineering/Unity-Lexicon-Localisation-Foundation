using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Heathen.Lexicon
{
    /// <summary>
    /// A parsed <c>.helex</c> source: the culture list and entries read from the JSON text of a single
    /// localisation file. This is the runtime-facing form of a <c>.helex</c> file, produced by
    /// <see cref="Parse"/> from a shipped <see cref="UnityEngine.TextAsset"/> and handed to
    /// <see cref="LexiconRegistry.RegisterParsed"/>. It replaces the compiled <c>LexiconCompiledData</c>
    /// ScriptableObject as the delivery vehicle: strings load wholesale, assets carry a GUID and stream on
    /// demand through the Addressables seam (<see cref="LexiconAssetLoader"/>).
    /// </summary>
    /// <remarks>
    /// Runtime schema (a superset of the editor-authored form, cross-engine with O3DE Lexicon Foundation):
    /// <code>
    /// {
    ///   "assetId":    "UI.Strings",
    ///   "registered": true,
    ///   "cultures":   ["en-GB"],
    ///   "entries": {
    ///     "UI.Play": "Play",
    ///     "UI.Logo": { "guid": "abc123...", "sub": "LogoSprite", "hint": "Sprite", "path": "Assets/Sprites/Logo.png" }
    ///   }
    /// }
    /// </code>
    /// For asset entries <c>guid</c> is authoritative at runtime (it is the addressable address); <c>path</c> is
    /// retained for human readability and as an editor fallback; <c>sub</c> names a sub-asset (e.g. a sprite in a
    /// sheet); <c>hint</c> records the asset type so it need not be loaded to be classified. <c>uuid</c> is the
    /// O3DE identifier and is ignored in Unity.
    /// </remarks>
    public sealed class LexiconSource
    {
        /// <summary>The asset identifier declared by the source, used to match display names and the default fallback.</summary>
        public string AssetId = "";

        /// <summary>The BCP 47 culture codes this source provides entries for.</summary>
        public string[] Cultures = Array.Empty<string>();

        /// <summary>Whether this source should auto-register on load (the <c>registered</c> flag; defaults to true).</summary>
        public bool AutoRegister = true;

        /// <summary>The parsed entries: strings carry <see cref="LexiconEntry.stringValue"/>, assets carry <see cref="LexiconEntry.assetGuid"/>.</summary>
        public List<LexiconEntry> Entries = new();

        /// <summary>
        /// True when this source is the culture-neutral last-resort fallback, identified by an
        /// <see cref="AssetId"/> of <c>"default"</c> (case-insensitive).
        /// </summary>
        public bool IsDefault =>
            string.Equals(AssetId, "default", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Parses <c>.helex</c> JSON text into a <see cref="LexiconSource"/>. Never throws: on malformed JSON it
        /// logs a warning and returns an empty source. In the editor, asset entries missing a <c>guid</c> resolve
        /// it from <c>path</c> via <c>AssetDatabase</c>, so files authored before GUIDs were threaded still work
        /// in play-in-editor.
        /// </summary>
        /// <param name="json">The raw text of a <c>.helex</c> file.</param>
        /// <returns>The parsed source; empty (but non-null) when <paramref name="json"/> is blank or malformed.</returns>
        public static LexiconSource Parse(string json)
        {
            var src = new LexiconSource();
            if (string.IsNullOrWhiteSpace(json)) return src;

            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lexicon] Failed to parse .helex JSON: {e.Message}");
                return src;
            }

            src.AssetId      = root["assetId"]?.Value<string>() ?? "";
            src.AutoRegister = root["registered"]?.Value<bool>() ?? true;
            src.Cultures     = root["cultures"]?.ToObject<string[]>() ?? Array.Empty<string>();

            if (root["entries"] is JObject entries)
            {
                var seen = new HashSet<ulong>();
                foreach (var prop in entries.Properties())
                {
                    var key = prop.Name?.Trim();
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    // Duplicate keys would collide in the registry; keep the first, drop the rest.
                    if (!seen.Add(LexiconRegistry.Hash(key))) continue;

                    if (prop.Value.Type == JTokenType.String)
                    {
                        src.Entries.Add(new LexiconEntry
                        {
                            key         = key,
                            hint        = LexiconHintType.String,
                            stringValue = prop.Value.Value<string>() ?? "",
                        });
                    }
                    else if (prop.Value is JObject assetObj)
                    {
                        src.Entries.Add(ParseAssetEntry(key, assetObj));
                    }
                }
            }

            return src;
        }

        private static LexiconEntry ParseAssetEntry(string key, JObject o)
        {
            var guid = o["guid"]?.Value<string>();
            var sub  = o["sub"]?.Value<string>();
            var path = o["path"]?.Value<string>();

            var hint = ParseHint(o["hint"]?.Value<string>());

#if UNITY_EDITOR
            // Files authored before GUIDs were threaded (or hand-edited with only a path) still resolve in the
            // editor: fill the GUID from the path, and infer the hint from the actual asset when unspecified.
            if (string.IsNullOrEmpty(guid) && !string.IsNullOrEmpty(path))
                guid = UnityEditor.AssetDatabase.AssetPathToGUID(path);
            if (hint == LexiconHintType.None && !string.IsNullOrEmpty(path))
                hint = HintFromAsset(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path));
#endif
            if (hint == LexiconHintType.None) hint = LexiconHintType.Asset;

            return new LexiconEntry
            {
                key          = key,
                hint         = hint,
                assetGuid    = guid,
                assetSubName = sub,
            };
        }

        private static LexiconHintType ParseHint(string s) =>
            Enum.TryParse<LexiconHintType>(s, ignoreCase: true, out var h) ? h : LexiconHintType.None;

#if UNITY_EDITOR
        private static LexiconHintType HintFromAsset(UnityEngine.Object asset) => asset switch
        {
            AudioClip  _ => LexiconHintType.Sound,
            Texture2D  _ => LexiconHintType.Texture,
            Sprite     _ => LexiconHintType.Sprite,
            GameObject _ => LexiconHintType.Prefab,
            null         => LexiconHintType.None,
            _            => LexiconHintType.Asset,
        };
#endif
    }
}
