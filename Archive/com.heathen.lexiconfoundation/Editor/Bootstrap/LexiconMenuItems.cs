// Copyright 2024 Heathen Engineering
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Heathen.Lexicon.Editor
{
    // Lives in its own assembly with no dependency on Heathen.GameFramework(.Editor), so it always compiles
    // and its InitializeOnLoadMethod always runs — even when Game Framework is missing and the rest of the
    // Lexicon Editor assembly is (deliberately, via asmdef defineConstraints) excluded from compilation.
    // Mirrors the equivalent bootstrap in Foundation for Steamworks.
    public static class LexiconMenuItems
    {
        private const string GameFrameworkUrl =
            "https://github.com/heathen-engineering/Unity-Game-Framework.git?path=/com.heathen.gameframework";

        [MenuItem("Help/Heathen/Lexicon Foundation/Install Game Framework", priority = 1)]
        public static void InstallGameFramework() => StartCoroutine(DoInstall(GameFrameworkUrl));

        [InitializeOnLoadMethod]
        public static void CheckForGameFrameworkInstall()
        {
            StartCoroutine(ValidateInstall());
        }

        private static IEnumerator ValidateInstall()
        {
            var listReq = Client.List(offlineMode: false);
            while (!listReq.IsCompleted) yield return null;

            if (listReq.Status != StatusCode.Success)
            {
                Debug.LogError("[LexiconMenuItems] Failed to query Package Manager: " + listReq.Error?.message);
                yield break;
            }

#if !HEATHEN_GAMEFRAMEWORK
            bool install = EditorUtility.DisplayDialog(
                "Heathen — Lexicon Foundation",
                "Heathen Game Framework is required but was not found. " +
                "Would you like to install it now?",
                "Install", "Cancel");

            if (install)
                StartCoroutine(DoInstall(GameFrameworkUrl));
#endif
        }

        private static IEnumerator DoInstall(string uri)
        {
            if (SessionState.GetBool("LexiconGameFrameworkInstalling", false))
            {
                Debug.LogWarning("[LexiconMenuItems] An install is already in progress.");
                yield break;
            }

            SessionState.SetBool("LexiconGameFrameworkInstalling", true);

            AddRequest req = Client.Add(uri);
            Debug.Log($"[LexiconMenuItems] Installing package from {uri} …");

            while (!req.IsCompleted) yield return null;

            if (req.Status == StatusCode.Success)
                Debug.Log($"[LexiconMenuItems] {req.Result.displayName} {req.Result.version} installed successfully.");
            else
                Debug.LogError($"[LexiconMenuItems] Package install failed: {req.Error?.message}");

            SessionState.SetBool("LexiconGameFrameworkInstalling", false);
        }

        private static List<IEnumerator> _coroutines;

        private static void StartCoroutine(IEnumerator routine)
        {
            if (_coroutines == null)
            {
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
                _coroutines = new List<IEnumerator>();
            }
            _coroutines.Add(routine);
        }

        private static void Tick()
        {
            if (_coroutines == null || _coroutines.Count == 0) return;

            var done = new List<IEnumerator>();
            foreach (var c in _coroutines)
                if (!c.MoveNext()) done.Add(c);
            foreach (var d in done) _coroutines.Remove(d);
        }
    }
}
#endif
