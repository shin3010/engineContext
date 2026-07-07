using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using EngineContext.Editor.Diagnostics;
using EngineContext.Editor.Extraction;
using EngineContext.Editor.Formatting;
using EngineContext.Editor.Inference;
using EngineContext.Editor.Model;

namespace EngineContext.Editor.Pipeline
{
    /// <summary>Pipeline 실행 결과: 초안 + Snapshot + 로그 요약 (TA §3).</summary>
    public class PipelineResult
    {
        public string draft;
        public Snapshot snapshot;
        public ExtractionLog log;
        public bool cancelled;

        /// <summary>비어 있지 않으면 실행이 사전 차단됨(예: 미저장 씬). 파일 생성 없이 사유만 전달.</summary>
        public string blockedReason;
    }

    /// <summary>
    /// L2 — 오케스트레이터. Extract → Infer → Format을 고정 순서로 실행하는 단일 지점 (TA §1, §3).
    /// 메인 스레드 동기 실행 (Unity API는 스레드 세이프하지 않음 — 백그라운드 스레드 금지).
    /// 여기서는 파일을 절대 쓰지 않는다 (Review 게이트는 Window가 소유).
    /// </summary>
    public static class ContextGenerationPipeline
    {
        public const string ToolVersion = "0.1.0";

        // F8 대형 프로젝트 임계값 (PLAN.md 제안 기본값 — 문서 미규정 수치)
        private const int LargePrefabCount = 2000;
        private const int LargeGameObjectCount = 20000;

        /// <summary>F8/E3: 스캔 규모 사전 추정. 임계 초과 시 호출 측이 현재 씬 폴백을 제안한다.</summary>
        public static bool IsProjectLarge()
        {
            if (AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }).Length > LargePrefabCount)
                return true;

            var count = 0;
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    count += CountTransforms(root.transform);
                    if (count > LargeGameObjectCount)
                        return true;
                }
            }
            return false;
        }

        public static PipelineResult Run(ScanScope scope, Action<string, float> onProgress, Func<bool> isCancelled)
        {
            var snapshot = new Snapshot();
            var log = new ExtractionLog();

            snapshot.meta.schemaVersion = "1.4"; // 1.4: eventBindings 추가 + 멀티 씬 스캔 (Wiring Capture 패치)
            snapshot.meta.toolVersion = ToolVersion;
            // 비결정 요소(시각)는 meta에만 격리 (TA §4, FS §4.5)
            snapshot.meta.generatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            snapshot.meta.scope = scope == ScanScope.Full ? "full" : "current-scene";
            snapshot.meta.unityVersion = Application.unityVersion;

            // 변경3(선행): scope==Full이면 Build Settings의 모든 씬을 열어 스캔해야
            // GameScene 등의 wiring이 비어 보이지 않는다. 씬 열기/닫기는 에디터 상태를 일시 변경하므로
            // (디스크는 불변) 미저장 변경이 있으면 안전을 위해 중단한다(Q2: 저장 안내 후 중단).
            SceneSetup[] sceneSetup = null;
            var openedScenes = new List<Scene>();
            if (scope == ScanScope.Full)
            {
                var block = FindUnsavedSceneBlock();
                if (block != null)
                    return new PipelineResult { snapshot = snapshot, log = log, blockedReason = block };

                sceneSetup = EditorSceneManager.GetSceneManagerSetup();
                OpenBuildScenes(openedScenes, log);
            }

            var cancelled = false;
            try
            {
                cancelled = RunExtraction(snapshot, log, scope, onProgress, isCancelled);
            }
            finally
            {
                RestoreScenes(sceneSetup, openedScenes); // 원래 씬 상태 복원 (저장 안 함)
            }

            if (cancelled)
                return new PipelineResult { snapshot = snapshot, log = log, cancelled = true }; // E8: 파일 미생성

            SortWiring(snapshot); // 결정론: 이벤트/참조 배선 안정 정렬 (FS §3.2)

            snapshot.meta.skippedRefCount = log.SkippedRefCount;
            BuildNotes(snapshot, log, scope, openedScenes.Count);

            onProgress?.Invoke("Formatting", 1f);
            var draft = ClaudeMarkdownFormatter.Format(snapshot);

            return new PipelineResult { draft = draft, snapshot = snapshot, log = log };
        }

        /// <summary>고정 순서(TA §7)로 Extractor·Inference 실행. 취소되면 true.</summary>
        private static bool RunExtraction(Snapshot snapshot, ExtractionLog log, ScanScope scope,
            Action<string, float> onProgress, Func<bool> isCancelled)
        {
            var steps = new (string label, Action action)[]
            {
                ("Project Settings", () => ProjectSettingsExtractor.Extract(snapshot, log)),
                ("Packages", () => PackageExtractor.Extract(snapshot, log)),
                ("Folders & Assemblies", () => AssemblyFolderExtractor.Extract(snapshot, log)),
                ("Scene Hierarchy", () => HierarchyExtractor.Extract(snapshot, log, scope)),
                ("Prefabs", () => PrefabExtractor.Extract(snapshot, log, scope)),
                ("ScriptableObjects", () => ScriptableObjectExtractor.Extract(snapshot, log, scope)),
                ("Semantic Inference", () => SemanticInferenceEngine.Infer(snapshot)),
            };

            var total = steps.Length + 1; // + Formatting
            for (var i = 0; i < steps.Length; i++)
            {
                if (isCancelled != null && isCancelled())
                    return true;

                onProgress?.Invoke(steps[i].label, (float)i / total);
                var stopwatch = Stopwatch.StartNew();
                steps[i].action();
                stopwatch.Stop();
                log.AddTiming(steps[i].label, stopwatch.ElapsedMilliseconds);
            }
            return false;
        }

        /// <summary>Q2: 로드된 씬에 미저장 변경(또는 미저장 새 씬)이 있으면 차단 사유 문자열, 없으면 null.</summary>
        private static string FindUnsavedSceneBlock()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid())
                    continue;
                if (scene.isDirty || string.IsNullOrEmpty(scene.path))
                    return "열려 있는 씬에 저장되지 않은 변경이 있습니다. 전체 스캔은 모든 빌드 씬을 열었다 닫으므로, "
                           + "씬을 먼저 저장한 뒤 다시 실행해 주세요. (또는 현재 씬만 스캔을 사용하세요.)";
            }
            return null;
        }

        /// <summary>Build Settings에 등록된(enabled) 씬을 additive로 연다. 이미 열린 씬은 건너뛴다.</summary>
        private static void OpenBuildScenes(List<Scene> openedScenes, ExtractionLog log)
        {
            foreach (var buildScene in EditorBuildSettings.scenes)
            {
                if (buildScene == null || !buildScene.enabled || string.IsNullOrEmpty(buildScene.path))
                    continue;

                var existing = EditorSceneManager.GetSceneByPath(buildScene.path);
                if (existing.IsValid() && existing.isLoaded)
                    continue;

                try
                {
                    var opened = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Additive);
                    if (opened.IsValid())
                        openedScenes.Add(opened);
                }
                catch (Exception ex)
                {
                    log.AddWarning("Could not open build scene '" + buildScene.path + "': " + ex.Message);
                }
            }
        }

        /// <summary>원래 씬 상태 복원. 저장은 절대 하지 않는다(read-only 불변). 실패는 조용히 넘긴다.</summary>
        private static void RestoreScenes(SceneSetup[] sceneSetup, List<Scene> openedScenes)
        {
            try
            {
                if (sceneSetup != null && sceneSetup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(sceneSetup);
                }
                else
                {
                    foreach (var scene in openedScenes)
                    {
                        if (scene.IsValid() && scene.isLoaded)
                            EditorSceneManager.CloseScene(scene, true);
                    }
                }
            }
            catch (Exception)
            {
                // 복원 실패해도 디스크에는 아무것도 쓰지 않았으므로 프로젝트 파일은 안전.
            }
        }

        private static void SortWiring(Snapshot snapshot)
        {
            snapshot.eventBindings.Sort((a, b) =>
            {
                var c = string.CompareOrdinal(a.sourcePath, b.sourcePath);
                if (c != 0) return c;
                c = string.CompareOrdinal(a.sourceComponent, b.sourceComponent);
                if (c != 0) return c;
                c = string.CompareOrdinal(a.eventName, b.eventName);
                if (c != 0) return c;
                c = string.CompareOrdinal(a.targetPath, b.targetPath);
                if (c != 0) return c;
                return string.CompareOrdinal(a.method, b.method);
            });

            snapshot.components.Sort((a, b) =>
            {
                var c = string.CompareOrdinal(a.ownerPath, b.ownerPath);
                return c != 0 ? c : string.CompareOrdinal(a.type, b.type);
            });
        }

        private static void BuildNotes(Snapshot snapshot, ExtractionLog log, ScanScope scope, int openedBuildScenes)
        {
            if (scope == ScanScope.CurrentScene)
                snapshot.notes.Add("Scan scope was limited to the current scene; project-wide data may be partial.");
            else if (openedBuildScenes > 0)
                snapshot.notes.Add("Full scan additively opened " + openedBuildScenes
                    + " build scene(s) to capture cross-scene wiring; your open-scene setup was restored afterward.");

            if (log.SkippedRefCount > 0)
                snapshot.notes.Add(log.SkippedRefCount + " broken or missing reference(s) were skipped during extraction."); // E4 고지

            foreach (var warning in log.Warnings)
            {
                if (snapshot.notes.Count >= 15)
                    break;
                if (!snapshot.notes.Contains(warning))
                    snapshot.notes.Add(warning);
            }
        }

        private static int CountTransforms(Transform transform)
        {
            var count = 1;
            for (var i = 0; i < transform.childCount; i++)
                count += CountTransforms(transform.GetChild(i));
            return count;
        }
    }
}
