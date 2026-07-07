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
    /// L3 — 열린 씬의 GameObject·Component 수집 (TA §3, FS §3).
    /// 채우는 곳: snapshot.hierarchy, snapshot.componentTypes(헛참조 방지 카탈로그),
    ///           snapshot.components(병합 스키마: ownerPath/type/keyRefs).
    /// Transform 좌표·필드 값은 읽지 않는다 (FS §3.1).
    /// </summary>
    internal static class HierarchyExtractor
    {
        private const int MaxDepth = 5;                // 트리 깊이 상한 (IR 단계 적용, TA §4)
        private const int MaxComponentEntries = 300;   // 참조 필드(keyRefs) 수집 대상 상한 (성능 가드)
        private const int MaxKeyRefsPerComponent = 8;
        private const int MaxEventBindings = 300;      // UnityEvent 캡처 상한 (성능 가드)

        public static void Extract(Snapshot snapshot, ExtractionLog log, ScanScope scope)
        {
            var scenes = new List<Scene>();
            if (scope == ScanScope.CurrentScene)
            {
                var active = SceneManager.GetActiveScene();
                if (active.IsValid() && active.isLoaded)
                    scenes.Add(active);
            }
            else
            {
                for (var i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (scene.IsValid() && scene.isLoaded)
                        scenes.Add(scene);
                }
            }
            scenes.Sort((a, b) => string.CompareOrdinal(a.name, b.name)); // 결정론

            var componentTypes = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var type in snapshot.componentTypes)
                componentTypes.Add(type);
            var customTypes = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var type in snapshot.customComponentTypes)
                customTypes.Add(type);

            var anyObjects = false;
            foreach (var scene in scenes)
            {
                var roots = scene.GetRootGameObjects();
                Array.Sort(roots, (a, b) => string.CompareOrdinal(a.name, b.name));
                if (roots.Length == 0)
                    continue;

                anyObjects = true;
                var sceneHierarchy = new SceneHierarchy { scene = scene.name };
                foreach (var root in roots)
                    sceneHierarchy.roots.Add(BuildNode(scene.name, root, 1, snapshot, log, componentTypes, customTypes));
                snapshot.hierarchy.Add(sceneHierarchy);
            }

            if (!anyObjects)
            {
                // E2: 열린 씬 없음 → 씬 섹션 생략, 나머지 컨텍스트로 부분 생성 진행
                log.AddWarning("No open scene with objects was found; scene sections were omitted.");
            }

            snapshot.componentTypes = new List<string>(componentTypes);
            snapshot.customComponentTypes = new List<string>(customTypes);
        }

        private static HierarchyNode BuildNode(
            string sceneName, GameObject go, int depth,
            Snapshot snapshot, ExtractionLog log,
            SortedSet<string> componentTypes, SortedSet<string> customTypes)
        {
            var node = new HierarchyNode
            {
                name = go.name,
                active = go.activeSelf,
                tag = SafeTag(go),
                layer = LayerMask.LayerToName(go.layer)
            };

            var ownerPath = sceneName + "/" + GetPath(go);
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null)
                {
                    log.AddSkip("missing script on '" + GetPath(go) + "'"); // F10/E4
                    continue;
                }

                var typeName = component.GetType().Name;
                node.components.Add(typeName);
                componentTypes.Add(typeName);
                if (!TypeClassifier.IsEngineType(component.GetType()))
                    customTypes.Add(typeName); // 커스텀 스크립트 — 출력에서 우선 표기

                // 변경1: UnityEvent persistent call 캡처 (Button.onClick 등, 코드에 안 나타남).
                // 내장 컴포넌트(Button/Toggle 등)의 이벤트도 대상이므로 타입 제한 없이 스캔한다.
                if (snapshot.eventBindings.Count < MaxEventBindings)
                    WiringCapture.CaptureEvents(ownerPath, component, snapshot, log);

                // 변경2: 커스텀 스크립트의 참조 필드(public·private [SerializeField] 무관, 참조 타입만) 캡처.
                // 스칼라/값 타입 및 Unity 내장 컴포넌트의 기본 직렬화 참조는 제외(노이즈 방지).
                if (component is MonoBehaviour mb && !TypeClassifier.IsEngineType(component.GetType())
                    && snapshot.components.Count < MaxComponentEntries)
                    WiringCapture.CaptureReferenceFields(ownerPath, mb, snapshot, log, MaxKeyRefsPerComponent);
            }

            if (depth < MaxDepth)
            {
                var transform = go.transform;
                for (var i = 0; i < transform.childCount; i++)
                    node.children.Add(BuildNode(sceneName, transform.GetChild(i).gameObject, depth + 1, snapshot, log, componentTypes, customTypes));
            }
            else if (go.transform.childCount > 0)
            {
                // E5: 과중첩 → 깊이 상한에서 안전 정지, Notes에 1회 고지
                log.AddWarningOnce("hierarchyDepth", "Hierarchy tree was capped at depth " + MaxDepth + "; deeper objects are omitted.");
            }

            return node;
        }

        private static string SafeTag(GameObject go)
        {
            try
            {
                return go.tag;
            }
            catch (Exception)
            {
                return "Untagged"; // 태그가 삭제된 경우 UnityException 방지
            }
        }

        private static string GetPath(GameObject go)
        {
            var transform = go.transform;
            var path = go.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }
    }
}
