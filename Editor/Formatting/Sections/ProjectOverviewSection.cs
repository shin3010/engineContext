using System.Collections.Generic;
using System.Text;
using EngineContext.Editor.Model;

namespace EngineContext.Editor.Formatting.Sections
{
    /// <summary>
    /// 섹션 2 — Project Overview (FS §4.1).
    /// "Scenes in build"은 EditorBuildSettings 등록 기준 — 현재 열린 씬 요약(Scene / Hierarchy
    /// Summary 섹션)과 별개로, 프로젝트에 어떤 씬들이 존재하고 빌드에 어떤 순서로 포함되는지 알려준다.
    /// </summary>
    internal static class ProjectOverviewSection
    {
        public static string Render(Snapshot snapshot, FormatterLimits limits)
        {
            var project = snapshot.project;
            var sb = new StringBuilder();
            sb.AppendLine("## Project Overview");
            sb.AppendLine("- Product: `" + (string.IsNullOrEmpty(project.productName) ? "unknown" : project.productName) + "`");
            sb.AppendLine("- Unity: `" + snapshot.meta.unityVersion + "`");
            sb.AppendLine("- Render pipeline: `" + project.renderPipeline + "`");
            sb.AppendLine("- Scripting backend: `" + project.scriptingBackend + "`");
            sb.AppendLine("- Input system: `" + project.inputSystem + "`");

            if (project.buildScenes.Count > 0)
                sb.AppendLine(BuildScenesLine(project.buildScenes, limits));

            if (project.defineSymbols.Count == 0)
            {
                sb.Append("- Define symbols: none");
            }
            else
            {
                var capped = FormatterLimits.Cap(project.defineSymbols, limits.MaxDefineSymbols, out var hidden);
                var codes = new List<string>();
                foreach (var symbol in capped)
                    codes.Add("`" + symbol + "`");
                sb.Append("- Define symbols: " + string.Join(", ", codes) + FormatterLimits.More(hidden));
            }
            return sb.ToString();
        }

        private static string BuildScenesLine(List<BuildSceneEntry> scenes, FormatterLimits limits)
        {
            var enabledNames = new List<string>();
            var disabledNames = new List<string>();
            foreach (var scene in scenes) // 빌드 등록 순서 유지 (로드 순서 정보)
            {
                if (scene.enabled)
                    enabledNames.Add(scene.name);
                else
                    disabledNames.Add(scene.name);
            }

            string line;
            if (enabledNames.Count > 0)
            {
                var capped = FormatterLimits.Cap(enabledNames, limits.MaxBuildScenes, out var hidden);
                var codes = new List<string>();
                foreach (var name in capped)
                    codes.Add("`" + name + "`");
                line = "- Scenes in build: " + string.Join(", ", codes) + FormatterLimits.More(hidden);
            }
            else
            {
                line = "- Scenes in build: none enabled";
            }

            if (disabledNames.Count > 0)
            {
                var codes = new List<string>();
                foreach (var name in disabledNames)
                    codes.Add("`" + name + "`");
                line += " (disabled: " + string.Join(", ", codes) + ")";
            }
            return line;
        }
    }
}
