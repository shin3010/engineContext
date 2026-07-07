using System.Collections.Generic;
using System.Text;
using EngineContext.Editor.Model;

namespace EngineContext.Editor.Formatting.Sections
{
    /// <summary>섹션 6 — Scene / Hierarchy Summary: 씬별 루트→주요 노드 요약, 깊이 제한 (FS §4.1).</summary>
    internal static class HierarchySummarySection
    {
        public static string Render(Snapshot snapshot, FormatterLimits limits)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Scene / Hierarchy Summary");

            if (snapshot.hierarchy.Count == 0)
            {
                // E2와 정합: 열린 씬 없음을 명시 (빈 헤더 방치 금지)
                sb.Append("_No open scene was captured._");
                return sb.ToString();
            }

            foreach (var scene in snapshot.hierarchy)
            {
                sb.AppendLine("### Scene: `" + scene.scene + "`");
                var roots = FormatterLimits.Cap(scene.roots, limits.MaxChildrenPerNode, out var hidden);
                foreach (var root in roots)
                    RenderNode(sb, root, 1, limits);
                if (hidden > 0)
                    sb.AppendLine("-" + FormatterLimits.More(hidden));
            }

            return sb.ToString().TrimEnd();
        }

        private static void RenderNode(StringBuilder sb, HierarchyNode node, int depth, FormatterLimits limits)
        {
            var indent = new string(' ', (depth - 1) * 2);
            var line = indent + "- " + node.name;
            if (!node.active)
                line += " _(inactive)_";

            // Transform은 모든 GO에 존재 → 토큰 절약을 위해 표기 생략 (IR에는 보존됨)
            var meaningful = new List<string>();
            foreach (var component in node.components)
            {
                if (component != "Transform")
                    meaningful.Add(component);
            }
            if (meaningful.Count > 0)
            {
                var capped = FormatterLimits.Cap(meaningful, limits.MaxComponentsPerNode, out var compHidden);
                var codes = new List<string>();
                foreach (var component in capped)
                    codes.Add("`" + component + "`");
                line += " - " + string.Join(", ", codes) + FormatterLimits.More(compHidden);
            }
            sb.AppendLine(line);

            if (depth >= limits.MaxHierarchyDepth)
            {
                if (node.children.Count > 0)
                    sb.AppendLine(indent + "  - (+" + node.children.Count + " nested, depth-capped)");
                return;
            }

            var children = FormatterLimits.Cap(node.children, limits.MaxChildrenPerNode, out var hidden);
            foreach (var child in children)
                RenderNode(sb, child, depth + 1, limits);
            if (hidden > 0)
                sb.AppendLine(indent + "  -" + FormatterLimits.More(hidden));
        }
    }
}
