using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Heathen.Lexicon
{
    public static class LexiconRegistry
    {
        private static readonly List<LexiconData> _registeredData = new();
        // culture code -> (hash -> entry)
        private static readonly Dictionary<string, Dictionary<ulong, LexiconData.Entry>> _cultures = new();
        private static string     _activeCulture;
        private static string     _defaultCulture;
        // The asset literally named "Default" — used as the unconditional fallback
        private static LexiconData _defaultData;

        public static event Action<string> CultureChanged;
        public static event Action<string> DefaultCultureChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            _registeredData.Clear();
            _cultures.Clear();
            _activeCulture  = null;
            _defaultCulture = null;
            _defaultData    = null;

            var assets = Resources.LoadAll<LexiconData>("");
            // Register the "Default" asset first so it becomes the base fallback
            foreach (var asset in assets)
                if (IsDefaultAsset(asset) && asset.autoRegister)
                    Register(asset);
            foreach (var asset in assets)
                if (!IsDefaultAsset(asset) && asset.autoRegister)
                    Register(asset);
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

        public static bool IsDefaultAsset(LexiconData data) =>
            data != null &&
            (string.Equals(data.name,    "Default", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(data.assetId, "default", StringComparison.OrdinalIgnoreCase));

        public static void Unregister(LexiconData data)
        {
            if (!_registeredData.Remove(data)) return;
            RebuildAllCultures();
        }

        public static void LoadCulture(string cultureCode)
        {
            _activeCulture = cultureCode;
            CultureChanged?.Invoke(cultureCode);
        }

        public static void SetDefaultCulture(string cultureCode)
        {
            _defaultCulture = cultureCode;
            DefaultCultureChanged?.Invoke(cultureCode);
        }

        public static string GetActiveCulture() => _activeCulture;

        public static IEnumerable<string> GetAvailableCultureCodes() => _cultures.Keys;

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
            if (_activeCulture != null && _cultures.TryGetValue(_activeCulture, out var active))
                if (active.TryGetValue(key, out entry))
                    return true;

            if (_defaultCulture != null && _defaultCulture != _activeCulture &&
                _cultures.TryGetValue(_defaultCulture, out var def))
                if (def.TryGetValue(key, out entry))
                    return true;

            // Last resort: search the Default data asset directly
            // (covers keys not indexed under any declared culture code)
            if (_defaultData != null)
                foreach (var e in _defaultData.entries)
                {
                    if (string.IsNullOrWhiteSpace(e.key)) continue;
                    if (Hash(e.key) == key) { entry = e; return true; }
                }

            entry = default;
            return false;
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
            foreach (var data in _registeredData)
                AddCulturesFrom(data);
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
