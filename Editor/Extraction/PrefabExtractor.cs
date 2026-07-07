using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using EngineContext.Editor.Diagnostics;
using EngineContext.Editor.Model;

namespace EngineContext.Editor.Extraction
{
    /// <summary>
    /// L3 — Prefab 자산 인벤토리 + 사용처(usedBy) 수집 (TA §3, FS §3).
    /// Prefab 참조는 AI 오답 최다 발생원 — 인벤토리를 정확히 전달한다.
    /// </summary>
    internal static class PrefabExtractor
    {
        private const int LoadCapFull = 1000;         // 성능 가드: 내용을 로드하는 자산 수 상한
        private const int LoadCapCurrentScene = 200;  // F8 폴백 시 축소
        private const int MaxUsedByPerPrefab = 5;
        private const int MaxComponentEntries = 300;  // 참조 필드 수집 상한 (Hierarchy와 공유 개념)
        private const int MaxEventBindings = 300;     // UnityEvent 캡처 상한
        private const int MaxKeyRefsPerComponent = 8;

        public static void Extract(Snapshot snapshot, ExtractionLog log, ScanScope scope)
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            var paths = new SortedSet<string>(StringComparer.Ordinal); // 결정론
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }

            var loadCap = scope == ScanScope.CurrentScene ? LoadCapCurrentScene : LoadCapFull;
            if (paths.Count > loadCap)
                log.AddWarning("Prefab scan was capped at " + loadCap + " of " + paths.Count + " assets.");

            var usedByMap = BuildUsedByMap();

            var componentTypes = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var type in snapshot.componentTypes)
                componentTypes.Add(type);
            var customTypes = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var type in snapshot.customComponentTypes)
                customTypes.Add(type);

            var loaded = 0;
            foreach (var path in paths)
            {
                if (loaded >= loadCap)
                    break;
                loaded++;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    log.AddSkip("unloadable prefab '" + path + "'"); // E4: 무중단
                    continue;
                }

                var entry = new PrefabEntry { name = prefab.name, path = path };
                foreach (var component in prefab.GetComponents<Component>())
                {
                    if (component == null)
                    {
                        log.AddSkip("missing script on prefab '" + path + "'");
                        continue;
                    }
                    var typeName = component.GetType().Name;
                    entry.rootComponents.Add(typeName);
                    componentTypes.Add(typeName);
                    if (!TypeClassifier.IsEngineType(component.GetType()))
                        customTypes.Add(typeName);
                }

                if (usedByMap.TryGetValue(path, out var users))
                    entry.usedBy = users;

                snapshot.prefabs.Add(entry);

                // 프리팹 구멍 메우기: 프리팹 내부 계층에도 씬과 동일한 wiring 캡처 적용
                // (rootComponents 요약은 유지, 이벤트/참조 배선만 추가). ownerPath는 자산 경로 기준.
                CaptureWiring(prefab.transform, path, snapshot, log);
            }

            snapshot.componentTypes = new List<string>(componentTypes);
            snapshot.customComponentTypes = new List<string>(customTypes);
        }

        /// <summary>프리팹 내부 GameObject 계층을 순회하며 UnityEvent·참조 필드를 캡처.</summary>
        private static void CaptureWiring(Transform transform, string ownerPath, Snapshot snapshot, ExtractionLog log)
        {
            var go = transform.gameObject;
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null)
                    continue;

                if (snapshot.eventBindings.Count < MaxEventBindings)
                    WiringCapture.CaptureEvents(ownerPath, component, snapshot, log);

                if (component is MonoBehaviour mb && !TypeClassifier.IsEngineType(component.GetType())
                    && snapshot.components.Count < MaxComponentEntries)
                    WiringCapture.CaptureReferenceFields(ownerPath, mb, snapshot, log, MaxKeyRefsPerComponent);
            }

            for (var i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                CaptureWiring(child, ownerPath + "/" + child.gameObject.name, snapshot, log);
            }
        }

        /// <summary>열린 씬들을 순회해 prefab 자산 경로 → 씬 인스턴스 경로 목록을 만든다.</summary>
        private static Dictionary<string, List<string>> BuildUsedByMap()
        {
            var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                var roots = scene.GetRootGameObjects();
                Array.Sort(roots, (a, b) => string.CompareOrdinal(a.name, b.name)); // 결정론
                foreach (var root in roots)
                    Walk(scene.name, root.transform, map);
            }

            foreach (var users in map.Values)
                users.Sort(StringComparer.Ordinal);
            return map;
        }

        private static void Walk(string sceneName, Transform transform, Dictionary<string, List<string>> map)
        {
            var go = transform.gameObject;
            if (PrefabUtility.IsAnyPrefabInstanceRoot(go))
            {
                var sourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                if (!string.IsNullOrEmpty(sourcePath))
                {
                    if (!map.TryGetValue(sourcePath, out var users))
                        map[sourcePath] = users = new List<string>();
                    if (users.Count < MaxUsedByPerPrefab)
                        users.Add(sceneName + "/" + go.name);
                }
            }

            for (var i = 0; i < transform.childCount; i++)
                Walk(sceneName, transform.GetChild(i), map);
        }
    }
}
