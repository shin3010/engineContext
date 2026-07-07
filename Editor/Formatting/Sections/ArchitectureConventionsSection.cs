using System.Collections.Generic;
using System.Text;
using EngineContext.Editor.Model;

namespace EngineContext.Editor.Formatting.Sections
{
    /// <summary>
    /// 섹션 4 — Architecture Conventions (Inferred): 추론 결과를 확신도 헤지 표현으로 서술 (FS §4.1/§4.4).
    /// 근거 없는 항목은 "none detected"로 표기 — 허위 단정 금지.
    /// </summary>
    internal static class ArchitectureConventionsSection
    {
        public static string Render(Snapshot snapshot, FormatterLimits limits)
        {
            var inferred = snapshot.inferred;
            var sb = new StringBuilder();
            sb.AppendLine("## Architecture Conventions (Inferred)");
            sb.AppendLine("_Inferred by rules from packages, type names, and folders. Treat as hints, not ground truth._");

            // DI
            if (inferred.di.detected)
                sb.AppendLine("- Dependency injection: appears to use `" + inferred.di.container
                              + "` (evidence: " + JoinEvidence(inferred.di.evidence) + ")");
            else
                sb.AppendLine("- Dependency injection: none detected");

            // 아키텍처 패턴
            if (inferred.architecturePatterns.Count == 0)
            {
                sb.AppendLine("- Architecture pattern: none detected");
            }
            else
            {
                foreach (var pattern in inferred.architecturePatterns)
                    sb.AppendLine("- Architecture pattern: `" + pattern.pattern + "` tendencies (confidence: "
                                  + pattern.confidence + "; evidence: " + JoinEvidence(pattern.evidence) + ")");
            }

            // 네이밍 규칙
            if (inferred.namingConventions.Count == 0)
            {
                sb.Append("- Naming conventions: none detected");
            }
            else
            {
                foreach (var rule in inferred.namingConventions)
                    sb.AppendLine("- Naming: " + rule.rule + " (evidence: " + JoinEvidence(rule.evidence) + ")");
            }

            return sb.ToString().TrimEnd();
        }

        private static string JoinEvidence(List<string> evidence)
        {
            var capped = FormatterLimits.Cap(evidence, 3, out var hidden);
            return string.Join("; ", capped) + FormatterLimits.More(hidden);
        }
    }
}
