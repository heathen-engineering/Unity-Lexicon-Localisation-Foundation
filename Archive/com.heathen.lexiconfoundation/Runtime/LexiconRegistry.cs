using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Heathen.Lexicon
{
    /// <summary>
    /// The central runtime registry for all Lexicon localisation data.
    /// Maintains a per-culture dictionary of entries sourced from parsed <see cref="LexiconSource"/> (<c>.helex</c>)
    /// data, and resolves strings and assets for the active culture.
    /// </summary>
    public static class LexiconRegistry
    {
        /// <summary>
        /// The Addressables label applied to every shipped <c>.helex</c> TextAsset, so the runtime can load all
        /// localisation sources with a single labelled load. See <see cref="LoadShippedSourcesAsync"/>.
        /// </summary>
        public const string AddressablesLabel = "lexicon";

        // Parsed .helex sources (the TextAsset delivery path). Tracked so RebuildAllCultures can re-index them.
        private static readonly List<LexiconSource> _registeredSources = new();
        // culture code -> (hash -> entry)
        private static readonly Dictionary<string, Dictionary<ulong, LexiconEntry>> _cultures = new();
        // Runtime-injected entries (SetString/SetAsset) that are NOT backed by an asset. Re-applied on top
        // of asset entries after every rebuild so they survive source churn and keep priority.
        private static readonly Dictionary<string, Dictionary<ulong, LexiconEntry>> _runtimeOverrides = new();
        // Flattened hash -> entry index of the Default source for O(1) last-resort lookup.
        private static readonly Dictionary<ulong, LexiconEntry> _defaultIndex = new();
        private static string           _activeCulture;
        private static string           _defaultCulture;
        // The parsed .helex source with assetId "default" — the culture-neutral last-resort fallback.
        private static LexiconSource _defaultSource;

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

        /// <summary>
        /// Resets the registry to a clean session and re-discovers the registered Lexicon assets. Owned by
        /// <c>LexiconSubsystem.Initialize</c> (a Global framework subsystem booted at
        /// <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/>), so the lifecycle runs through the
        /// framework rather than an ad-hoc bootstrap.
        /// </summary>
        public static void ResetForSession()
        {
            // Reset all state for a clean session, including under "Enter Play Mode without Domain Reload"
            // where statics (and event subscriptions) would otherwise persist from the previous session.
            _registeredSources.Clear();
            _cultures.Clear();
            _runtimeOverrides.Clear();
            _defaultIndex.Clear();
            _activeCulture        = null;
            _defaultCulture       = null;
            _defaultSource        = null;
            CultureChanged        = null;
            DefaultCultureChanged = null;

            // Discover localisation sources. In the editor, .helex TextAssets resolve synchronously via
            // AssetDatabase so play-in-editor is populated immediately at subsystem boot. In a player, the
            // shipped .helex TextAssets stream in asynchronously via Addressables — kicked from
            // LexiconSubsystem.Initialize after this reset (see LoadShippedSourcesAsync), since a synchronous
            // reset cannot await a load.
#if UNITY_EDITOR
            DiscoverEditorSources();
#endif

            // Auto-detect system locale, falling back to the default culture. Sets _activeCulture directly
            // without firing the event since no listeners are registered this early in startup.
            var systemCulture = System.Globalization.CultureInfo.CurrentCulture.Name;
            if (!string.IsNullOrEmpty(systemCulture))
                _activeCulture = systemCulture;
            _activeCulture ??= _defaultCulture;
        }

        /// <summary>
        /// Registers a parsed <see cref="LexiconSource"/> (from a shipped <c>.helex</c> TextAsset) with the
        /// registry, indexing all its entries under each declared culture. Strings are held resident; asset
        /// entries carry a GUID and stream on demand via <see cref="AcquireAsset(string,string)"/>. This is the
        /// sole delivery path for localisation content. Skips a source that is already registered or has
        /// <see cref="LexiconSource.AutoRegister"/> false.
        /// </summary>
        /// <param name="source">The parsed <c>.helex</c> source to register.</param>
        public static void RegisterParsed(LexiconSource source)
        {
            if (source == null || !source.AutoRegister || _registeredSources.Contains(source)) return;
            _registeredSources.Add(source);
            AddCulturesFrom(source);

            if (source.IsDefault)
            {
                _defaultSource = source;
                RebuildDefaultIndex();
            }

            // Seed the default/active culture from the first source that declares one, so a project loaded
            // entirely from .helex sources still gets a default-culture fallback tier.
            if (source.Cultures != null && source.Cultures.Length > 0)
            {
                _defaultCulture ??= source.Cultures[0];
                _activeCulture  ??= source.Cultures[0];
            }

            ReapplyOverrides();
        }

        /// <summary>
        /// Notifies listeners that localisation sources have (re)loaded by re-raising <see cref="CultureChanged"/>
        /// with the active culture, so bound fields re-resolve. Called after the asynchronous player load
        /// completes (<see cref="LoadShippedSourcesAsync"/>); the brief window before completion resolves to the
        /// fallback/literal value.
        /// </summary>
        public static void NotifySourcesLoaded() => CultureChanged?.Invoke(_activeCulture);

#if !UNITY_EDITOR
        // Keeps the load handle alive for the lifetime of the app (sources are held resident once loaded).
        private static UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle _sourcesHandle;

        /// <summary>
        /// Streams every shipped <c>.helex</c> TextAsset (those carrying the <see cref="AddressablesLabel"/>)
        /// via Addressables, parses and registers each, then raises <see cref="NotifySourcesLoaded"/>. Kicked
        /// once at framework boot from <c>LexiconSubsystem.Initialize</c> in players; the editor uses the
        /// synchronous <c>AssetDatabase</c> path instead.
        /// </summary>
        public static void LoadShippedSourcesAsync()
        {
            var handle = UnityEngine.AddressableAssets.Addressables.LoadAssetsAsync<TextAsset>(
                AddressablesLabel,
                ta => { if (ta != null) RegisterParsed(LexiconSource.Parse(ta.text)); });
            _sourcesHandle = handle;
            handle.Completed += _ => NotifySourcesLoaded();
        }
#endif

#if UNITY_EDITOR
        // Editor-only synchronous discovery of .helex sources from the project (no built Addressables catalogue
        // needed for play-in-editor). Registers Default sources first so they seed the fallback tier.
        private static void DiscoverEditorSources()
        {
            var sources = new List<LexiconSource>();
            foreach (var path in UnityEditor.AssetDatabase.GetAllAssetPaths())
            {
                if (!path.EndsWith(".helex", StringComparison.OrdinalIgnoreCase)) continue;
                if (path.Contains("~/")) continue; // skip hidden package Samples~ folders
                var ta = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (ta != null) sources.Add(LexiconSource.Parse(ta.text));
            }
            foreach (var s in sources) if ( s.IsDefault) RegisterParsed(s);
            foreach (var s in sources) if (!s.IsDefault) RegisterParsed(s);
        }

        /// <summary>
        /// Editor-only: re-discovers all <c>.helex</c> sources and rebuilds the culture tables, without
        /// disturbing <see cref="CultureChanged"/> subscriptions (unlike <see cref="ResetForSession"/>). Called
        /// on domain reload and whenever a <c>.helex</c> is (re)imported, so edit-mode resolution stays current.
        /// </summary>
        public static void RefreshEditorSources()
        {
            _registeredSources.Clear();
            _defaultSource = null;
            DiscoverEditorSources();
            RebuildAllCultures();
        }
#endif

        /// <summary>
        /// Sets the active culture and raises <see cref="CultureChanged"/>. This is the primary
        /// game-developer API for switching language at runtime, such as from a settings menu.
        /// Resolution falls back through the base language, then the default culture, then the
        /// Default source when no exact match is found.
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

        /// <summary>The fallback culture established during registration, or <c>null</c> when none is set.</summary>
        public static string GetDefaultCulture() => _defaultCulture;

        /// <summary>The number of registered Lexicon sources (parsed .helex sources). For diagnostics.</summary>
        public static int RegisteredSourceCount => _registeredSources.Count;

        /// <summary>
        /// Returns all culture codes that have at least one registered asset mapped to them.
        /// Use this to populate a language-selection menu in your game's settings UI.
        /// </summary>
        /// <returns>An enumerable of BCP 47 culture code strings.</returns>
        public static IEnumerable<string> GetMappedCultureCodes() => _cultures.Keys;

        /// <summary>
        /// Returns the asset IDs of all currently registered <see cref="LexiconSource"/> sources
        /// that have a non-empty <see cref="LexiconSource.AssetId"/>.
        /// </summary>
        /// <returns>An enumerable of asset ID strings.</returns>
        public static IEnumerable<string> GetAvailableAssetIds()
        {
            foreach (var source in _registeredSources)
                if (!string.IsNullOrWhiteSpace(source.AssetId))
                    yield return source.AssetId;
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
            if (!TryGetEntry(key, out var entry)) return null;
            // A direct reference (injected via SetAsset or carried by a compiled asset) wins; otherwise a
            // streamed entry resolves its GUID through the Addressables seam (cache in a player, AssetDatabase
            // in the editor). Acquire the entry first via AcquireAsset for a guaranteed-resident player load.
            if (entry.assetValue != null) return entry.assetValue;
            if (!string.IsNullOrEmpty(entry.assetGuid))
                return LexiconAssetLoader.Resolve(entry.assetGuid, entry.assetSubName);
            return null;
        }

        /// <summary>
        /// Resolves an asset entry for the given dot-path key in the active culture.
        /// </summary>
        /// <param name="dotPath">The dot-path key string.</param>
        /// <returns>The resolved <see cref="UnityEngine.Object"/>, or <see langword="null"/> if not found.</returns>
        public static Object ResolveAsset(string dotPath) => ResolveAsset(Hash(dotPath));

        /// <summary>
        /// Resolves an asset by its GUID via the Addressables asset seam (<see cref="LexiconAssetLoader"/>).
        /// Use for literal, non-localised content authored as JSON or baked to code, where a live asset
        /// reference cannot be serialised. Synchronous: reads the preload cache, falling back to
        /// <c>AssetDatabase</c> in the editor. Acquire with <see cref="AcquireAssetByGuidAsync"/> for players.
        /// </summary>
        /// <param name="guid">The asset GUID (its addressable address).</param>
        /// <param name="subAssetName">The sub-asset name for sprite sheets, or <c>null</c> for the main asset.</param>
        /// <returns>The resolved asset, or <c>null</c>.</returns>
        public static Object ResolveAssetByGuid(string guid, string subAssetName = null)
            => LexiconAssetLoader.Resolve(guid, subAssetName);

        /// <summary>
        /// Acquires a reference-counted reference to an asset by GUID, loading it into the synchronous cache so a
        /// later <see cref="ResolveAssetByGuid"/> returns it without blocking. Balance with
        /// <see cref="ReleaseAssetByGuid"/>. Delegates to <see cref="LexiconAssetLoader"/>.
        /// </summary>
        /// <param name="guid">The asset GUID (its addressable address).</param>
        /// <param name="subAssetName">The sub-asset name for sprite sheets, or <c>null</c> for the main asset.</param>
        public static System.Threading.Tasks.Task AcquireAssetByGuidAsync(string guid, string subAssetName = null)
            => LexiconAssetLoader.AcquireAsync(guid, subAssetName);

        /// <summary>
        /// Releases one reference acquired via <see cref="AcquireAssetByGuidAsync"/>. The asset is unloaded when
        /// its last reference is released. Delegates to <see cref="LexiconAssetLoader"/>.
        /// </summary>
        /// <param name="guid">The asset GUID (its addressable address).</param>
        /// <param name="subAssetName">The sub-asset name for sprite sheets, or <c>null</c> for the main asset.</param>
        public static void ReleaseAssetByGuid(string guid, string subAssetName = null)
            => LexiconAssetLoader.Release(guid, subAssetName);

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
        /// Injects or overwrites a string entry in the registry. Uses the active culture when <paramref name="cultureCode"/>
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
            var key     = Hash(dotPath);
            var culture = ResolveWriteCulture(cultureCode);
            var entry   = new LexiconEntry { key = dotPath, hint = LexiconHintType.String, stringValue = value };
            EnsureCulture(culture)[key]         = entry;
            EnsureOverrideCulture(culture)[key] = entry; // survives RebuildAllCultures
        }

        /// <summary>
        /// Injects or overwrites an asset entry in the registry. The hint is inferred from the asset type.
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
            var culture = ResolveWriteCulture(cultureCode);
            var entry   = new LexiconEntry { key = dotPath, hint = hint, assetValue = asset };
            EnsureCulture(culture)[key]         = entry;
            EnsureOverrideCulture(culture)[key] = entry; // survives RebuildAllCultures
        }

        /// <summary>
        /// Injects or overwrites a <em>streamed</em> asset entry that carries a GUID rather than a live
        /// reference. The asset is not loaded until
        /// acquired via <see cref="AcquireAsset(string,string)"/>; <see cref="ResolveAsset(string)"/> then returns
        /// it from the Addressables seam. This is the runtime surface for streamable, GUID-addressed content
        /// (e.g. voice-over) and the same shape the compiled <c>.helex</c> sources register. Uses the active
        /// culture when <paramref name="cultureCode"/> is <see langword="null"/>.
        /// </summary>
        /// <param name="dotPath">The dot-path key for the entry.</param>
        /// <param name="guid">The GUID of the asset (its addressable address).</param>
        /// <param name="hint">The content type of the asset (e.g. <see cref="LexiconHintType.Sound"/>).</param>
        /// <param name="subAssetName">The sub-asset name for sprite sheets, or <c>null</c> for the main asset.</param>
        /// <param name="cultureCode">
        /// The BCP 47 culture code to write into, or <see langword="null"/> to target the active culture.
        /// </param>
        public static void SetAssetByGuid(string dotPath, string guid, LexiconHintType hint = LexiconHintType.Asset,
                                          string subAssetName = null, string cultureCode = null)
        {
            if (string.IsNullOrWhiteSpace(dotPath) || string.IsNullOrEmpty(guid)) return;
            var key     = Hash(dotPath);
            var culture = ResolveWriteCulture(cultureCode);
            var entry   = new LexiconEntry
            {
                key          = dotPath,
                hint         = hint,
                assetGuid    = guid,
                assetSubName = subAssetName,
            };
            EnsureCulture(culture)[key]         = entry;
            EnsureOverrideCulture(culture)[key] = entry; // survives RebuildAllCultures
        }

        /// <summary>
        /// Acquires and streams resident the asset for the given dot-path key in the active culture, so a later
        /// <see cref="ResolveAsset(string)"/> returns it without blocking in a player. Reference-counted; balance
        /// every call with <see cref="ReleaseAsset(string,string)"/>. A no-op for keys whose entry carries a
        /// direct reference (already resident) or is not an asset. This is the key-addressed voice-over streaming
        /// surface: hold a window of dialogue lines resident and release them when they leave the window.
        /// </summary>
        /// <param name="dotPath">The dot-path key of the asset entry to stream in.</param>
        /// <param name="subAssetName">
        /// The sub-asset name to acquire, or <c>null</c> to use the sub-asset recorded on the entry.
        /// </param>
        /// <returns>A task that completes when the asset is resident (already complete when held or in the editor).</returns>
        public static System.Threading.Tasks.Task AcquireAsset(string dotPath, string subAssetName = null)
            => AcquireAsset(Hash(dotPath), subAssetName);

        /// <summary>
        /// Acquires and streams resident the asset for the given pre-computed hash in the active culture.
        /// See <see cref="AcquireAsset(string,string)"/>.
        /// </summary>
        /// <param name="key">The XXH3 hash of the dot-path key.</param>
        /// <param name="subAssetName">
        /// The sub-asset name to acquire, or <c>null</c> to use the sub-asset recorded on the entry.
        /// </param>
        /// <returns>A task that completes when the asset is resident.</returns>
        public static System.Threading.Tasks.Task AcquireAsset(ulong key, string subAssetName = null)
        {
            if (!TryGetEntry(key, out var entry) || string.IsNullOrEmpty(entry.assetGuid))
                return System.Threading.Tasks.Task.CompletedTask;
            return LexiconAssetLoader.AcquireAsync(entry.assetGuid, subAssetName ?? entry.assetSubName);
        }

        /// <summary>
        /// Releases one reference acquired via <see cref="AcquireAsset(string,string)"/> for the given dot-path
        /// key. The asset is unloaded when its last reference is released. A no-op for non-streamed entries.
        /// </summary>
        /// <param name="dotPath">The dot-path key of the asset entry to release.</param>
        /// <param name="subAssetName">
        /// The sub-asset name to release, or <c>null</c> to use the sub-asset recorded on the entry.
        /// </param>
        public static void ReleaseAsset(string dotPath, string subAssetName = null)
            => ReleaseAsset(Hash(dotPath), subAssetName);

        /// <summary>
        /// Releases one reference acquired via <see cref="AcquireAsset(ulong,string)"/> for the given hash.
        /// See <see cref="ReleaseAsset(string,string)"/>.
        /// </summary>
        /// <param name="key">The XXH3 hash of the dot-path key.</param>
        /// <param name="subAssetName">
        /// The sub-asset name to release, or <c>null</c> to use the sub-asset recorded on the entry.
        /// </param>
        public static void ReleaseAsset(ulong key, string subAssetName = null)
        {
            if (!TryGetEntry(key, out var entry) || string.IsNullOrEmpty(entry.assetGuid)) return;
            LexiconAssetLoader.Release(entry.assetGuid, subAssetName ?? entry.assetSubName);
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
                if (_cultures.TryGetValue(cultureCode, out var dict))         dict.Remove(key);
                if (_runtimeOverrides.TryGetValue(cultureCode, out var odict)) odict.Remove(key);
            }
            else
            {
                foreach (var dict in _cultures.Values)         dict.Remove(key);
                foreach (var dict in _runtimeOverrides.Values) dict.Remove(key);
            }
        }

        private static string ResolveWriteCulture(string cultureCode) =>
            cultureCode ?? _activeCulture ?? _defaultCulture ?? "default";

        private static Dictionary<ulong, LexiconEntry> EnsureCulture(string culture)
        {
            if (!_cultures.TryGetValue(culture, out var dict))
            {
                dict = new Dictionary<ulong, LexiconEntry>();
                _cultures[culture] = dict;
                if (_defaultCulture == null) _defaultCulture = culture;
                if (_activeCulture  == null) _activeCulture  = culture;
            }
            return dict;
        }

        private static bool TryGetEntry(ulong key, out LexiconEntry entry)
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
            // 4. Last resort: the always-present Default asset, indexed for O(1) lookup, culture-independent.
            if (_defaultIndex.TryGetValue(key, out entry)) return true;

            entry = default;
            return false;
        }

        private static bool TryGetFromCulture(string culture, ulong key, out LexiconEntry entry)
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

        private static void RebuildAllCultures()
        {
            _cultures.Clear();
            foreach (var source in _registeredSources)
                AddCulturesFrom(source);
            ReapplyOverrides();   // runtime injections survive source churn and keep priority
            RebuildDefaultIndex();
        }

        // Re-applies runtime-injected entries on top of the freshly rebuilt culture tables.
        private static void ReapplyOverrides()
        {
            foreach (var pair in _runtimeOverrides)
            {
                var target = EnsureCulture(pair.Key);
                foreach (var kv in pair.Value)
                    target[kv.Key] = kv.Value;
            }
        }

        // Rebuilds the flattened hash -> entry index used for the Default last-resort lookup, from the parsed
        // Default source (the .helex whose assetId is "default").
        private static void RebuildDefaultIndex()
        {
            _defaultIndex.Clear();
            if (_defaultSource?.Entries != null)
                foreach (var e in _defaultSource.Entries)
                    _defaultIndex[Hash(e.key)] = e;
        }

        private static Dictionary<ulong, LexiconEntry> EnsureOverrideCulture(string culture)
        {
            if (!_runtimeOverrides.TryGetValue(culture, out var dict))
            {
                dict = new Dictionary<ulong, LexiconEntry>();
                _runtimeOverrides[culture] = dict;
            }
            return dict;
        }

        private static void AddCulturesFrom(LexiconSource source)
        {
            if (source?.Entries == null || source.Cultures == null) return;
            foreach (var culture in source.Cultures)
            {
                if (string.IsNullOrWhiteSpace(culture)) continue;
                if (!_cultures.TryGetValue(culture, out var dict))
                {
                    dict = new Dictionary<ulong, LexiconEntry>();
                    _cultures[culture] = dict;
                }
                foreach (var entry in source.Entries)
                    dict[Hash(entry.key)] = entry;
            }
        }

        private static FixedString512Bytes ToFixed512(string s) =>
            string.IsNullOrEmpty(s) ? default : new FixedString512Bytes(TruncateUtf8(s, 510));

        /// <summary>
        /// Truncates <paramref name="s"/> so its UTF-8 encoding fits within <paramref name="maxBytes"/> without
        /// splitting a multi-byte code point. Returns the original string when it already fits.
        /// </summary>
        /// <param name="s">The string to truncate.</param>
        /// <param name="maxBytes">The maximum UTF-8 byte budget.</param>
        /// <returns>A string whose UTF-8 encoding is at most <paramref name="maxBytes"/> bytes.</returns>
        public static string TruncateUtf8(string s, int maxBytes)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            if (bytes.Length <= maxBytes) return s;
            // Back off any UTF-8 continuation bytes (10xxxxxx) so the cut lands on a code-point boundary.
            int cut = maxBytes;
            while (cut > 0 && (bytes[cut] & 0xC0) == 0x80) cut--;
            return System.Text.Encoding.UTF8.GetString(bytes, 0, cut);
        }
    }
}
