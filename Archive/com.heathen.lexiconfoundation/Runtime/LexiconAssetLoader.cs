using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace Heathen.Lexicon
{
    /// <summary>
    /// Resolves assets by GUID for content authored as JSON or baked to code, where a live
    /// <see cref="UnityEngine.Object"/> reference cannot be serialised (a bare GUID string in baked code is
    /// invisible to Unity's build dependency walker, so the asset is stripped unless marked addressable with its
    /// GUID as the address). Lexicon owns asset delivery, so this is the single asset seam every Heathen tool
    /// consumes.
    /// <para>
    /// Designed for streaming large bodies of content: <see cref="AcquireAsync"/> / <see cref="Release"/> are
    /// reference-counted, so a consumer holds a window of assets resident and frees them when they leave the
    /// window, bounding memory regardless of total content size. <see cref="Resolve"/> is synchronous and reads
    /// the cache; in the editor it falls back to <c>AssetDatabase</c> so play-in-editor needs no built catalogue.
    /// </para>
    /// </summary>
    public static class LexiconAssetLoader
    {
        // Synchronous cache: cache key (guid, or "guid#subAsset") → resolved object.
        private static readonly Dictionary<string, Object> _cache = new();
        // Live Addressables handles, kept so loaded assets are not released while referenced.
        private static readonly Dictionary<string, AsyncOperationHandle> _handles = new();
        // In-flight loads, so concurrent acquires of the same key share one load (no duplicate handle).
        private static readonly Dictionary<string, Task> _inflight = new();
        // Reference counts: how many live holders have acquired each key. Freed when it reaches zero.
        private static readonly Dictionary<string, int> _refCount = new();

        private static string CacheKey(string guid, string subAssetName)
            => string.IsNullOrEmpty(subAssetName) ? guid : guid + "#" + subAssetName;

        /// <summary>
        /// Synchronously resolves an asset by GUID from the cache. In the editor, falls back to
        /// <c>AssetDatabase</c> on a cache miss so play-in-editor needs no built catalogue. In a player, returns
        /// <c>null</c> when the asset is not currently held (acquire it first via <see cref="AcquireAsync"/>).
        /// </summary>
        /// <param name="guid">The asset GUID (its addressable address).</param>
        /// <param name="subAssetName">The sub-asset name for sprite sheets, or <c>null</c> for the main asset.</param>
        /// <returns>The resolved asset, or <c>null</c>.</returns>
        public static Object Resolve(string guid, string subAssetName = null)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            var key = CacheKey(guid, subAssetName);
            if (_cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

#if UNITY_EDITOR
            var editorAsset = EditorResolve(guid, subAssetName);
            if (editorAsset != null) _cache[key] = editorAsset;
            return editorAsset;
#else
            return null;
#endif
        }

        /// <summary>
        /// Acquires a reference to an asset by GUID, loading it into the synchronous cache if it is not already
        /// held, so a later <see cref="Resolve"/> returns it without blocking. Balance every call with a
        /// <see cref="Release"/> when the asset leaves the holder's window. In the editor it resolves immediately
        /// via <c>AssetDatabase</c>; in a player it loads via Addressables (the GUID is the address).
        /// </summary>
        /// <param name="guid">The asset GUID (its addressable address).</param>
        /// <param name="subAssetName">The sub-asset name for sprite sheets, or <c>null</c> for the main asset.</param>
        /// <returns>A task that completes when the asset is in the cache (already complete when held or in the editor).</returns>
        public static Task AcquireAsync(string guid, string subAssetName = null)
        {
            if (string.IsNullOrEmpty(guid)) return Task.CompletedTask;
            var key = CacheKey(guid, subAssetName);
            _refCount[key] = _refCount.TryGetValue(key, out var c) ? c + 1 : 1;

            if (_cache.ContainsKey(key)) return Task.CompletedTask;
            if (_inflight.TryGetValue(key, out var existing)) return existing;

#if UNITY_EDITOR
            // In the editor AssetDatabase resolves synchronously, so there is no need to drive Addressables
            // (which would also require a built catalogue) for play-in-editor.
            var editorAsset = EditorResolve(guid, subAssetName);
            if (editorAsset != null) { _cache[key] = editorAsset; return Task.CompletedTask; }
#endif
            var task = LoadAddressableAsync(key, guid, subAssetName);
            _inflight[key] = task;
            return task;
        }

        /// <summary>
        /// Releases one reference acquired via <see cref="AcquireAsync"/>. When the last reference is released the
        /// asset is unloaded (its Addressables handle released) and removed from the cache, freeing memory. A
        /// no-op when the asset is not currently held.
        /// </summary>
        /// <param name="guid">The asset GUID (its addressable address).</param>
        /// <param name="subAssetName">The sub-asset name for sprite sheets, or <c>null</c> for the main asset.</param>
        public static void Release(string guid, string subAssetName = null)
        {
            if (string.IsNullOrEmpty(guid)) return;
            var key = CacheKey(guid, subAssetName);
            if (!_refCount.TryGetValue(key, out var c)) return;
            if (c > 1) { _refCount[key] = c - 1; return; }

            _refCount.Remove(key);
            FreeKey(key);
        }

        /// <summary>
        /// Releases every cached asset and Addressables handle and clears all reference counts. Call when
        /// unloading a whole body of content. Safe to call repeatedly.
        /// </summary>
        public static void ReleaseAll()
        {
            foreach (var handle in _handles.Values)
                if (handle.IsValid()) Addressables.Release(handle);
            _handles.Clear();
            _cache.Clear();
            _inflight.Clear();
            _refCount.Clear();
        }

        private static async Task LoadAddressableAsync(string key, string guid, string subAssetName)
        {
            try
            {
                // Sprite sheets: address the named sub-object via the "address[subObject]" key form, falling
                // back to the main asset when no sub-asset is requested.
                object loadKey = string.IsNullOrEmpty(subAssetName) ? guid : $"{guid}[{subAssetName}]";
                var handle = Addressables.LoadAssetAsync<Object>(loadKey);
                var asset  = await handle.Task;
                if (handle.Status == AsyncOperationStatus.Succeeded && asset != null)
                {
                    _cache[key]   = asset;
                    _handles[key] = handle;
                    // Released while loading: nobody is holding it any more, so free it immediately.
                    if (!_refCount.ContainsKey(key)) FreeKey(key);
                }
                else
                {
                    Addressables.Release(handle);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lexicon] Addressables could not load asset GUID '{guid}'" +
                                 (string.IsNullOrEmpty(subAssetName) ? "" : $" (sub-asset '{subAssetName}')") +
                                 $": {e.Message}. Mark the asset addressable so it ships in builds.");
            }
            finally
            {
                _inflight.Remove(key);
            }
        }

        private static void FreeKey(string key)
        {
            if (_handles.TryGetValue(key, out var handle))
            {
                if (handle.IsValid()) Addressables.Release(handle);
                _handles.Remove(key);
            }
            _cache.Remove(key);
        }

#if UNITY_EDITOR
        // Editor-only synchronous resolution via AssetDatabase, so play-in-editor and in-graph test-play resolve
        // assets identically without a built Addressables catalogue.
        private static Object EditorResolve(string guid, string subAssetName)
        {
            var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;

            if (!string.IsNullOrEmpty(subAssetName))
            {
                // Sprite sheets store sprites as sub-assets; match by name, preferring a non-main object.
                Object nameMatch = null;
                var main = UnityEditor.AssetDatabase.LoadMainAssetAtPath(path);
                foreach (var a in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (a == null || a.name != subAssetName) continue;
                    if (a != main) return a;
                    nameMatch ??= a;
                }
                if (nameMatch != null) return nameMatch;
            }

            return UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(path);
        }
#endif
    }
}
