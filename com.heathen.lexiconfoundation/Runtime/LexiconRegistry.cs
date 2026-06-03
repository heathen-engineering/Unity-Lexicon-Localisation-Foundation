using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Heathen.Lexicon
{
    /// <summary>
    /// The central runtime registry for all Lexicon localisation data.
    /// Maintains a per-culture dictionary of entries sourced from <see cref="LexiconData"/> and
    /// <see cref="LexiconCompiledData"/> assets, and resolves strings and assets for the active culture.
    /// </summary>
    public static class LexiconRegistry
    {
        private static readonly List<LexiconData>         _registeredData         = new();
        private static readonly List<LexiconCompiledData> _registeredCompiledData = new();
        // culture code -> (hash -> entry)
        private static readonly Dictionary<string, Dictionary<ulong, LexiconData.Entry>> _cultures = new();
        private static string           _activeCulture;
        private static string           _defaultCulture;
        // The asset literally named "Default" — unconditional last-resort fallback
        private static LexiconData         _defaultData;
        private static LexiconCompiledData _defaultCompiledData;

        /// <summary>
        /// Raised whenever the active culture changes via <see cref="UseCulture"/>.
        /// Subscribe to this event to refresh any UI or cached values that depend on the current culture.
        /// </summary>
        public static event Action<string> CultureChanged;

        /// <summary>
        /// Raised whenever the default culture is first established during registration.
        /// Useful for systems that need to know when a fallback culture becomes available.
        /// </summary>
        public static event Action<string> DefaultCultureChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            _registeredData.Clear();
            _registeredCompiledData.Clear();
            _cultures.Clear();
            _activeCulture       = null;
            _defaultCulture      = null;
            _defaultData         = null;
            _defaultCompiledData = null;

            // Compiled .helex assets — pre-hashed at import time, zero hashing work here.
            var compiled = Resources.LoadAll<LexiconCompiledData>("");
            foreach (var asset in compiled)
                if (IsDefaultCompiledAsset(asset) && asset.AutoRegister) Register(asset);
            foreach (var asset in compiled)
                if (!IsDefaultCompiledAsset(asset) && asset.AutoRegister) Register(asset);

            // Legacy LexiconData assets — kept for backward compatibility.
            var assets = Resources.LoadAll<LexiconData>("");
            foreach (var asset in assets)
                if (IsDefaultAsset(asset) && asset.autoRegister) Register(asset);
            foreach (var asset in assets)
                if (!IsDefaultAsset(asset) && asset.autoRegister) Register(asset);

            // Auto-detect system locale. Sets _activeCulture directly without firing the event
            // since no listeners are registered this early in startup.
            var systemCulture = System.Globalization.CultureInfo.CurrentCulture.Name;
            if (!string.IsNullOrEmpty(systemCulture))
                _activeCulture = systemCulture;
        }

        /// <summary>
        /// Registers a <see cref="LexiconData"/> asset with the registry, indexing all its entries
        /// under each of its declared cultures. Skips registration if the asset is already registered.
        /// </summary>
        /// <param name="data">The <see cref="LexiconData"/> asset to register.</param>
        public static void Register(LexiconData data)
        {
            if (data == null || _registeredData.Contains(data)) return;
            _registeredData.Add(data);
            AddCulturesFrom(data);

            if (IsDefaultAsset(data))
                _defaultData = data;

            if (_defaultCulture == null && data.cultures.Count > 0)
                _defaultCulture = data.cultures[0];
            if (_activeCulture == null && data.cultures.Count > 0)
                _activeCulture = data.cultures[0];
        }

        /// <summary>
        /// Registers a <see cref="LexiconCompiledData"/> asset with the registry, indexing all its
        /// pre-hashed entries under each of its declared cultures. Skips registration if already registered.
        /// </summary>
        /// <param name="data">The <see cref="LexiconCompiledData"/> asset to register.</param>
        public static void Register(LexiconCompiledData data)
        {
            if (data == null || _registeredCompiledData.Contains(data)) return;
            _registeredCompiledData.Add(data);
            AddCulturesFrom(data);

            if (IsDefaultCompiledAsset(data))
                _defaultCompiledData = data;
        }

        /// <summary>
        /// Returns <see langword="true"/> when the given <see cref="LexiconCompiledData"/> asset
        /// is the designated fallback, identified by its name or <see cref="LexiconCompiledData.AssetId"/>
        /// equalling <c>"default"</c> (case-insensitive).
        /// </summary>
        /// <param name="data">The compiled data asset to test.</param>
        /// <returns><see langword="true"/> if the asset is the default fallback; otherwise <see langword="false"/>.</returns>
        public static bool IsDefaultCompiledAsset(LexiconCompiledData data) =>
            data != null &&
            (string.Equals(data.name,    "Default", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(data.AssetId, "default", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Returns <see langword="true"/> when the given <see cref="LexiconData"/> asset
        /// is the designated fallback, identified by its name or <see cref="LexiconData.assetId"/>
        /// equalling <c>"default"</c> (case-insensitive).
        /// </summary>
        /// <param name="data">The legacy data asset to test.</param>
        /// <returns><see langword="true"/> if the asset is the default fallback; otherwise <see langword="false"/>.</returns>
        public static bool IsDefaultAsset(LexiconData data) =>
            data != null &&
            (string.Equals(data.name,    "Default", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(data.assetId, "default", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Removes a <see cref="LexiconData"/> asset from the registry and rebuilds all culture tables.
        /// </summary>
        /// <param name="data">The <see cref="LexiconData"/> asset to remove.</param>
        public static void Unregister(LexiconData data)
        {
            if (!_registeredData.Remove(data)) return;
            RebuildAllCultures();
        }

        /// <summary>
        /// Sets the active culture and raises <see cref="CultureChanged"/>. This is the primary
        /// game-developer API for switching language at runtime, such as from a settings menu.
        /// Resolution falls back through the base language, then the default culture, then the
        /// Default asset when no exact match is found.
        /// </summary>
        /// <param name="cultureCode">The BCP 47 culture code to activate (e.g. <c>"fr-CA"</c>).</param>
        public static void UseCulture(string cultureCode)
        {
            _activeCulture = cultureCode;
            CultureChanged?.Invoke(cultureCode);
        }

        /// <summary>
        /// Backwards-compatible alias for <see cref="UseCulture"/>. Prefer <see cref="UseCulture"/> in new code.
        /// </summary>
        /// <param name="cultureCode">The BCP 47 culture code to activate.</param>
        public static void LoadCulture(string cultureCode) => UseCulture(cultureCode);

        /// <summary>
        /// Returns the BCP 47 code of the currently active culture, or <see langword="null"/>
        /// if no culture has been set or detected yet.
        /// </summary>
        /// <returns>The active culture code, or <see langword="null"/>.</returns>
        public static string GetActiveCulture() => _activeCulture;

        /// <summary>
        /// Returns all culture codes that have at least one registered asset mapped to them.
        /// Use this to populate a language-selection menu in your game's settings UI.
        /// </summary>
        /// <returns>An enumerable of BCP 47 culture code strings.</returns>
        public static IEnumerable<string> GetMappedCultureCodes() => _cultures.Keys;

        /// <summary>
        /// Returns the asset IDs of all currently registered <see cref="LexiconData"/> assets
        /// that have a non-empty <see cref="LexiconData.assetId"/>.
        /// </summary>
        /// <returns>An enumerable of asset ID strings.</returns>
        public static IEnumerable<string> GetAvailableAssetIds()
        {
            foreach (var data in _registeredData)
                if (!string.IsNullOrWhiteSpace(data.assetId))
                    yield return data.assetId;
        }

        /// <summary>
        /// Returns the localised display name for a culture asset by resolving the key
        /// <c>Language.{assetId}</c> in the active culture. Falls back to <paramref name="assetId"/>
        /// itself when no entry is found.
        /// </summary>
        /// <param name="assetId">The asset ID whose display name to look up.</param>
        /// <returns>The localised display name, or <paramref name="assetId"/> if none is registered.</returns>
        public static string GetDisplayName(string assetId) =>
            ResolveString($"Language.{assetId}") ?? assetId;

        /// <summary>
        /// Resolves a string entry for the given pre-computed hash in the active culture,
        /// following the fallback chain: exact culture, base language, default culture, Default asset.
        /// </summary>
        /// <param name="key">The XXH3 hash of the dot-path key.</param>
        /// <returns>The resolved string, or <see langword="null"/> if no matching entry is found.</returns>
        public static string ResolveString(ulong key)
        {
            if (TryGetEntry(key, out var entry) && entry.hint == LexiconHintType.String)
                return entry.stringValue;
            return null;
        }

        /// <summary>
        /// Resolves a string entry for the given dot-path key in the active culture.
        /// Hashes the key and delegates to <see cref="ResolveString(ulong)"/>.
        /// </summary>
        /// <param name="dotPath">The dot-path key string (e.g. <c>"UI.Play"</c>).</param>
        /// <returns>The resolved string, or <see langword="null"/> if no matching entry is found.</returns>
        public static string ResolveString(string dotPath) => ResolveString(Hash(dotPath));

        /// <summary>
        /// Resolves an asset entry for the given pre-computed hash in the active culture.
        /// </summary>
        /// <param name="key">The XXH3 hash of the dot-path key.</param>
        /// <returns>The resolved <see cref="UnityEngine.Object"/>, or <see langword="null"/> if not found.</returns>
        public static Object ResolveAsset(ulong key)
        {
            if (TryGetEntry(key, out var entry))
                return entry.assetValue;
            return null;
        }

        /// <summary>
        /// Resolves an asset entry for the given dot-path key in the active culture.
        /// </summary>
        /// <param name="dotPath">The dot-path key string.</param>
        /// <returns>The resolved <see cref="UnityEngine.Object"/>, or <see langword="null"/> if not found.</returns>
        public static Object ResolveAsset(string dotPath) => ResolveAsset(Hash(dotPath));

        /// <summary>
        /// Resolves a sound entry for the given pre-computed hash as an <see cref="AudioClip"/>.
        /// </summary>
        /// <param name="key">The XXH3 hash of the dot-path key.</param>
        /// <returns>The resolved <see cref="AudioClip"/>, or <see langword="null"/> if not found or not an AudioClip.</returns>
        public static AudioClip ResolveSound(ulong key) => ResolveAsset(key) as AudioClip;

        /// <summary>
        /// Resolves a sound entry for the given dot-path key as an <see cref="AudioClip"/>.
        /// </summary>
        /// <param name="dotPath">The dot-path key string.</param>
        /// <returns>The resolved <see cref="AudioClip"/>, or <see langword="null"/> if not found or not an AudioClip.</returns>
        public static AudioClip ResolveSound(string dotPath) => ResolveSound(Hash(dotPath));

        /// <summary>
        /// Computes the XXH3 (seed 0) hash of the given text, using the same algorithm as O3DE
        /// Lexicon Foundation to ensure cross-engine hash compatibility.
        /// </summary>
        /// <param name="text">The string to hash.</param>
        /// <returns>The 64-bit XXH3 hash of <paramref name="text"/>.</returns>
        public static ulong Hash(string text) => GameplayTags.GameplayTag.HashPath(text);

        /// <summary>
        /// Creates a Burst-readable snapshot of all string entries in the active culture as a
        /// <see cref="NativeHashMap{TKey,TValue}"/>. Asset entries are excluded because
        /// <see cref="UnityEngine.Object"/> references cannot be accessed from Burst.
        /// The caller owns the returned map and must call <c>Dispose</c> when finished.
        /// Rebuild the snapshot whenever <see cref="CultureChanged"/> fires.
        /// </summary>
        /// <param name="allocator">The allocator to use for the native map.</param>
        /// <returns>A <see cref="NativeHashMap{TKey,TValue}"/> mapping entry hashes to fixed-length string values.</returns>
        public static NativeHashMap<ulong, FixedString512Bytes> GetStringSnapshot(Allocator allocator)
        {
            var result = new NativeHashMap<ulong, FixedString512Bytes>(64, allocator);

            if (_activeCulture != null && _cultures.TryGetValue(_activeCulture, out var dict))
            {
                foreach (var kv in dict)
                {
                    if (kv.Value.hint != LexiconHintType.String) continue;
                    result.TryAdd(kv.Key, ToFixed512(kv.Value.stringValue));
                }
            }

            return result;
        }

        /// <summary>
        /// Injects or overwrites a string entry in the registry without requiring a
        /// <see cref="LexiconData"/> asset. Uses the active culture when <paramref name="cultureCode"/>
        /// is <see langword="null"/>.
        /// </summary>
        /// <param name="dotPath">The dot-path key for the entry (e.g. <c>"UI.Play"</c>).</param>
        /// <param name="value">The string value to store.</param>
        /// <param name="cultureCode">
        /// The BCP 47 culture code to write into, or <see langword="null"/> to target the active culture.
        /// </param>
        public static void SetString(string dotPath, string value, string cultureCode = null)
        {
            if (string.IsNullOrWhiteSpace(dotPath)) return;
            var key  = Hash(dotPath);
            var dict = EnsureCulture(ResolveWriteCulture(cultureCode));
            dict[key] = new LexiconData.Entry { key = dotPath, hint = LexiconHintType.String, stringValue = value };
        }

        /// <summary>
        /// Injects or overwrites an asset entry in the registry without requiring a
        /// <see cref="LexiconData"/> asset. The hint is inferred from the asset type.
        /// Uses the active culture when <paramref name="cultureCode"/> is <see langword="null"/>.
        /// </summary>
        /// <param name="dotPath">The dot-path key for the entry.</param>
        /// <param name="asset">The <see cref="UnityEngine.Object"/> asset to store.</param>
        /// <param name="cultureCode">
        /// The BCP 47 culture code to write into, or <see langword="null"/> to target the active culture.
        /// </param>
        public static void SetAsset(string dotPath, Object asset, string cultureCode = null)
        {
            if (string.IsNullOrWhiteSpace(dotPath)) return;
            var key  = Hash(dotPath);
            var hint = asset switch
            {
                UnityEngine.AudioClip  _ => LexiconHintType.Sound,
                UnityEngine.Texture    _ => LexiconHintType.Texture,
                UnityEngine.Sprite     _ => LexiconHintType.Sprite,
                UnityEngine.GameObject _ => LexiconHintType.Prefab,
                _                        => LexiconHintType.Asset,
            };
            var dict = EnsureCulture(ResolveWriteCulture(cultureCode));
            dict[key] = new LexiconData.Entry { key = dotPath, hint = hint, assetValue = asset };
        }

        /// <summary>
        /// Removes a runtime-injected entry from the registry. Pass <see langword="null"/> for
        /// <paramref name="cultureCode"/> to remove the entry from every culture simultaneously.
        /// </summary>
        /// <param name="dotPath">The dot-path key of the entry to remove.</param>
        /// <param name="cultureCode">
        /// The specific culture to remove the entry from, or <see langword="null"/> to remove from all cultures.
        /// </param>
        public static void RemoveKey(string dotPath, string cultureCode = null)
        {
            if (string.IsNullOrWhiteSpace(dotPath)) return;
            var key = Hash(dotPath);
            if (cultureCode != null)
            {
                if (_cultures.TryGetValue(cultureCode, out var dict))
                    dict.Remove(key);
            }
            else
            {
                foreach (var dict in _cultures.Values)
                    dict.Remove(key);
            }
        }

        private static string ResolveWriteCulture(string cultureCode) =>
            cultureCode ?? _activeCulture ?? _defaultCulture ?? "default";

        private static Dictionary<ulong, LexiconData.Entry> EnsureCulture(string culture)
        {
            if (!_cultures.TryGetValue(culture, out var dict))
            {
                dict = new Dictionary<ulong, LexiconData.Entry>();
                _cultures[culture] = dict;
                if (_defaultCulture == null) _defaultCulture = culture;
                if (_activeCulture  == null) _activeCulture  = culture;
            }
            return dict;
        }

        private static bool TryGetEntry(ulong key, out LexiconData.Entry entry)
        {
            // 1. Exact active culture (e.g. "fr-CA")
            if (TryGetFromCulture(_activeCulture, key, out entry)) return true;
            // 2. Base language of active culture (e.g. "fr")
            if (TryGetFromCulture(BaseCulture(_activeCulture), key, out entry)) return true;
            // 3. Exact default culture
            if (_defaultCulture != null && _defaultCulture != _activeCulture)
            {
                if (TryGetFromCulture(_defaultCulture, key, out entry)) return true;
                if (TryGetFromCulture(BaseCulture(_defaultCulture), key, out entry)) return true;
            }
            // 4. Last resort: Default helex entries not indexed under any culture code
            if (_defaultCompiledData?.Entries != null)
                foreach (var e in _defaultCompiledData.Entries)
                    if (e.Hash == key)
                    {
                        entry = new LexiconData.Entry { key = e.Key, hint = e.Hint, stringValue = e.StringValue, assetValue = e.AssetValue };
                        return true;
                    }
            if (_defaultData != null)
                foreach (var e in _defaultData.entries)
                    if (!string.IsNullOrWhiteSpace(e.key) && Hash(e.key) == key) { entry = e; return true; }

            entry = default;
            return false;
        }

        private static bool TryGetFromCulture(string culture, ulong key, out LexiconData.Entry entry)
        {
            if (culture != null && _cultures.TryGetValue(culture, out var dict) && dict.TryGetValue(key, out entry))
                return true;
            entry = default;
            return false;
        }

        // Returns the base language tag (e.g. "fr" from "fr-CA"), or null if already a base tag.
        private static string BaseCulture(string culture)
        {
            if (culture == null) return null;
            var dash = culture.IndexOf('-');
            return dash > 0 ? culture[..dash] : null;
        }

        private static void AddCulturesFrom(LexiconData data)
        {
            foreach (var culture in data.cultures)
            {
                if (string.IsNullOrWhiteSpace(culture)) continue;
                if (!_cultures.TryGetValue(culture, out var dict))
                {
                    dict = new Dictionary<ulong, LexiconData.Entry>();
                    _cultures[culture] = dict;
                }
                foreach (var entry in data.entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.key)) continue;
                    dict[Hash(entry.key)] = entry;
                }
            }
        }

        private static void RebuildAllCultures()
        {
            _cultures.Clear();
            foreach (var data in _registeredCompiledData)
                AddCulturesFrom(data);
            foreach (var data in _registeredData)
                AddCulturesFrom(data);
        }

        private static void AddCulturesFrom(LexiconCompiledData data)
        {
            if (data?.Entries == null || data.Cultures == null) return;
            foreach (var culture in data.Cultures)
            {
                if (string.IsNullOrWhiteSpace(culture)) continue;
                if (!_cultures.TryGetValue(culture, out var dict))
                {
                    dict = new Dictionary<ulong, LexiconData.Entry>();
                    _cultures[culture] = dict;
                }
                foreach (var entry in data.Entries)
                {
                    dict[entry.Hash] = new LexiconData.Entry
                    {
                        key         = entry.Key,
                        hint        = entry.Hint,
                        stringValue = entry.StringValue,
                        assetValue  = entry.AssetValue,
                    };
                }
            }
        }

        private static FixedString512Bytes ToFixed512(string s)
        {
            if (string.IsNullOrEmpty(s)) return default;
            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            if (bytes.Length > 510)
                s = System.Text.Encoding.UTF8.GetString(bytes, 0, 510);
            return new FixedString512Bytes(s);
        }
    }
}
