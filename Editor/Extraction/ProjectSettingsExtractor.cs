using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine.Rendering;
using EngineContext.Editor.Diagnostics;
using EngineContext.Editor.Model;

namespace EngineContext.Editor.Extraction
{
    /// <summary>
    /// L3 — Project Settings 큐레이션 요약 수집 (TA §3, FS §3).
    /// 전체 덤프 금지: productName, 스크립팅 백엔드, 렌더 파이프라인, Input System, Define Symbols만.
    /// </summary>
    internal static class ProjectSettingsExtractor
    {
        public static void Extract(Snapshot snapshot, ExtractionLog log)
        {
            var project = snapshot.project;
            project.productName = PlayerSettings.productName;

            try
            {
                var group = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
                var target = NamedBuildTarget.FromBuildTargetGroup(group);

                project.scriptingBackend = ToBackendName(PlayerSettings.GetScriptingBackend(target));

                var defines = PlayerSettings.GetScriptingDefineSymbols(target);
                foreach (var define in defines.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = define.Trim();
                    if (trimmed.Length > 0 && !project.defineSymbols.Contains(trimmed))
                        project.defineSymbols.Add(trimmed);
                }
                project.defineSymbols.Sort(StringComparer.Ordinal); // 결정론 (FS §3.2)
            }
            catch (Exception ex)
            {
                project.scriptingBackend = "unknown";
                log.AddWarning("Could not read scripting backend / define symbols: " + ex.Message);
            }

            project.renderPipeline = DetectRenderPipeline();
            project.inputSystem = DetectInputSystem();
            CollectBuildScenes(snapshot, log);
        }

        /// <summary>
        /// 빌드에 등록된 씬 목록 (열린 씬과 무관). 등록 순서를 그대로 보존한다 —
        /// 빌드 인덱스 순서 자체가 씬 전환 흐름을 이해하는 데 의미 있는 정보다.
        /// </summary>
        private static void CollectBuildScenes(Snapshot snapshot, ExtractionLog log)
        {
            try
            {
                foreach (var scene in EditorBuildSettings.scenes)
                {
                    if (scene == null || string.IsNullOrEmpty(scene.path))
                        continue;
                    snapshot.project.buildScenes.Add(new BuildSceneEntry
                    {
                        name = Path.GetFileNameWithoutExtension(scene.path),
                        path = scene.path,
                        enabled = scene.enabled
                    });
                }
            }
            catch (Exception ex)
            {
                log.AddWarning("Could not read the build scene list: " + ex.Message);
            }
        }

        private static string ToBackendName(ScriptingImplementation implementation)
        {
            switch (implementation)
            {
                case ScriptingImplementation.Mono2x: return "Mono";
                case ScriptingImplementation.IL2CPP: return "IL2CPP";
                default: return implementation.ToString();
            }
        }

        private static string DetectRenderPipeline()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null)
                return "BiRP";
            var typeName = pipeline.GetType().Name;
            if (typeName.Contains("Universal"))
                return "URP";
            if (typeName.Contains("HDRender") || typeName.Contains("HighDefinition"))
                return "HDRP";
            return "unknown";
        }

        private static string DetectInputSystem()
        {
#if ENABLE_INPUT_SYSTEM && ENABLE_LEGACY_INPUT_MANAGER
            return "Both";
#elif ENABLE_INPUT_SYSTEM
            return "New";
#elif ENABLE_LEGACY_INPUT_MANAGER
            return "Old";
#else
            return "unknown";
#endif
        }
    }
}
