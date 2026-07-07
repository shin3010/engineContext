using System.Text;
using EngineContext.Editor.Formatting.Sections;
using EngineContext.Editor.Model;

namespace EngineContext.Editor.Formatting
{
    /// <summary>
    /// L5 마스터 포매터 — Snapshot → CLAUDE.md 문자열 변환 (TA §5, FS §4).
    /// 고정 섹션 순서·상한 적용·결정론(동일 Snapshot → 바이트 동일 출력) 보장. LLM·네트워크 없음.
    ///
    /// TODO(v0.2+): 다형식 출력(MCP Resource, .cursorrules, AGENTS.md 등)을 지원하기 위해
    /// Snapshot(IR)과 포매터 사이에 중간 Context Model 계층을 도입할 예정이다.
    ///   현재:  Snapshot ──▶ ClaudeMarkdownFormatter ──▶ CLAUDE.md
    ///   향후:  Snapshot ──▶ ContextModel(포맷 중립 표현)
    ///                         ├─▶ ClaudeMarkdownFormatter  ──▶ CLAUDE.md
    ///                         ├─▶ AgentsMdFormatter        ──▶ AGENTS.md
    ///                         ├─▶ CursorRulesFormatter     ──▶ .cursorrules
    ///                         └─▶ McpResourceExporter      ──▶ MCP resource
    /// v0.1은 CLAUDE.md 1종만 출력하므로(Validation §6) Snapshot을 직접 소비하며,
    /// 이 TODO 외의 선행 추상화(인터페이스 등)는 도입하지 않는다(TA 아키텍처 원칙).
    /// </summary>
    public static class ClaudeMarkdownFormatter
    {
        public static string Format(Snapshot snapshot)
        {
            var limits = FormatterLimits.Default;
            var text = Render(snapshot, limits);
            if (text.Length <= limits.MaxTotalChars)
                return text;

            // E9: 전체 예산 초과 → truncated 표기 후 조밀한 상한으로 재렌더 (자동 요약)
            snapshot.meta.truncated = true;
            limits = FormatterLimits.Tight;
            text = Render(snapshot, limits);
            if (text.Length > limits.MaxTotalChars)
            {
                // 그래도 초과 → 하드 컷 (결정론 유지)
                text = text.Substring(0, limits.MaxTotalChars)
                       + "\n\n<!-- EngineContext: output hard-truncated to fit the size budget -->\n";
            }
            return text;
        }

        private static string Render(Snapshot snapshot, FormatterLimits limits)
        {
            // 고정 섹션 순서 (v0.3 [A-1]에서 재배치 확정) — 순서를 바꾸지 않는다.
            // 차별점(코드 밖 배선: Event Wiring / Wired References)을 Project Overview 바로 다음으로 승격.
            // 삭제되는 섹션 없음 — 일반 구조 섹션은 전부 유지된 채 뒤로만 밀림 (정보 손실 0).
            var sb = new StringBuilder();
            Append(sb, HeaderSection.Render(snapshot, limits));
            Append(sb, ProjectOverviewSection.Render(snapshot, limits));
            Append(sb, EventWiringSection.Render(snapshot, limits));      // 승격 (9 → 3)
            Append(sb, WiredReferencesSection.Render(snapshot, limits));  // 승격·독립 (Key Components 하위 → 4)
            Append(sb, TechStackSection.Render(snapshot, limits));
            Append(sb, ArchitectureConventionsSection.Render(snapshot, limits));
            Append(sb, FolderAssemblyMapSection.Render(snapshot, limits));
            Append(sb, HierarchySummarySection.Render(snapshot, limits));
            Append(sb, PrefabInventorySection.Render(snapshot, limits));
            Append(sb, ScriptableObjectCatalogSection.Render(snapshot, limits));
            Append(sb, KeyComponentsSection.Render(snapshot, limits));
            Append(sb, NotesSection.Render(snapshot, limits));
            return sb.ToString().TrimEnd() + "\n";
        }

        private static void Append(StringBuilder sb, string block)
        {
            if (string.IsNullOrEmpty(block))
                return; // 빈 섹션은 생략 (FS §4.2)
            sb.Append(block.TrimEnd());
            sb.Append("\n\n");
        }
    }
}
