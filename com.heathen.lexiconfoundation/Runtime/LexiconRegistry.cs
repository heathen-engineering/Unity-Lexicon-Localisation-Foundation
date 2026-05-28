using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Heathen.Lexicon
{
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

        public static event Action<string> CultureChanged;
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

        public static void Register(LexiconCompiledData data)
        {
            if (data == null || _registeredCompiledData.Contains(data)) return;
            _registeredCompiledData.Add(data);
            AddCulturesFrom(data);

            if (IsDefaultCompiledAsset(data))
                _defaultCompiledData = data;
        }

        public static bool IsDefaultCompiledAsset(LexiconCompiledData data) =>
            data != null &&
            (string.Equals(data.name,    "Default", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(data.AssetId, "default", StringComparison.OrdinalIgnoreCase));

        public static bool IsDefaultAsset(LexiconData data) =>
            data != null &&
            (string.Equals(data.name,    "Default", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(data.assetId, "default", StringComparison.OrdinalIgnoreCase));

        public static void Unregister(LexiconData data)
        {
            if (!_registeredData.Remove(data)) return;
            RebuildAllCultures();
        }

        // Primary game-dev API: call this from a language settings menu or on startup.
        // Finds the helex that serves cultureCode (exact, then base-language prefix),
        // falling back to the Default helex if nothing matches.
        public static void UseCulture(string cultureCode)
        {
            _activeCulture = cultureCode;
            CultureChanged?.Invoke(cultureCode);
        }

        // Backwards-compatible alias.
        public static void LoadCulture(string cultureCode) => UseCulture(cultureCode);

        public static string GetActiveCulture() => _activeCulture;

        // Returns the culture codes that have at least one helex mapped to them —
        // useful for populating a language-selection menu.
        public static IEnumerable<string> GetMappedCultureCodes() => _cultures.Keys;

        public static IEnumerable<string> GetAvailableAssetIds()
        {
            foreach (var data in _registeredData)
                if (!string.IsNullOrWhiteSpace(data.assetId))
                    yield return data.assetId;
        }

        // Resolves "Language.{assetId}" in active then default culture; returns assetId as fallback.
        public static string GetDisplayName(string assetId) =>
            ResolveString($"Language.{assetId}") ?? assetId;

        public static string ResolveString(ulong key)
        {
            if (TryGetEntry(key, out var entry) && entry.hint == LexiconHintType.String)
                return entry.stringValue;
            return null;
        }

        public static string ResolveString(string dotPath) => ResolveString(Hash(dotPath));

        public static Object ResolveAsset(ulong key)
        {
            if (TryGetEntry(key, out var entry))
                return entry.assetValue;
            return null;
        }

        public static Object ResolveAsset(string dotPath) => ResolveAsset(Hash(dotPath));

        public static AudioClip ResolveSound(ulong key) => ResolveAsset(key) as AudioClip;

        public static AudioClip ResolveSound(string dotPath) => ResolveSound(Hash(dotPath));

        public static ulong Hash(string text) => GameplayTags.GameplayTag.HashPath(text);

        // Burst-readable snapshot of all string entries in the active culture.
        // Asset entries are excluded — UnityEngine.Object refs cannot be accessed from Burst.
        // Caller owns and must Dispose. Rebuild on CultureChanged.
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

        // Inject or overwrite a string entry without a LexiconData asset. Uses active culture when cultureCode is null.
        public static void SetString(string dotPath, string value, string cultureCode = null)
        {
            if (string.IsNullOrWhiteSpace(dotPath)) return;
            var key  = Hash(dotPath);
            var dict = EnsureCulture(ResolveWriteCulture(cultureCode));
            dict[key] = new LexiconData.Entry { key = dotPath, hint = LexiconHintType.String, stringValue = value };
        }

        // Inject or overwrite an asset entry without a LexiconData asset. Uses active culture when cultureCode is null.
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

        // Remove a runtime-injected entry. Pass null to remove from all cultures.
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
