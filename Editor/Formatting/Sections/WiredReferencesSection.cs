using System;
using System.Collections.Generic;
using System.Text;
using EngineContext.Editor.Model;

namespace EngineContext.Editor.Formatting.Sections
{
    /// <summary>
    /// 섹션 (v0.3 [A] 신설) — Wired References: 커스텀 스크립트의 직렬화 참조 필드.
    /// Key Components 하위 "Reference fields" 줄이었던 것을 독립 섹션으로 승격 —
    /// Event Wiring과 함께 "코드에 안 보이는 배선"이라는 이 도구의 차별점을 파일 최전면에 배치한다.
    /// 정보 이동이지 삭제가 아니다 (Public API 스키마는 Key Components에 그대로 남는다).
    /// 데이터 없으면 섹션 생략 (기존 규칙).
    /// </summary>
    internal static class WiredReferencesSection
    {
        public static string Render(Snapshot snapshot, FormatterLimits limits)
        {
            var grouped = GroupByType(snapshot);
            if (grouped.Count == 0)
                return ""; // 빈 섹션 생략 (FS §4.2)

            var sb = new StringBuilder();
            sb.AppendLine("## Wired References");
            sb.AppendLine("Serialized reference fields on custom scripts (`[SerializeField]` included, wired or not). "
                          + "Like Event Wiring, these connections are NOT visible in code signatures. Scalar/value fields excluded.");

            var shown = 0;
            var hiddenTypes = 0;
            foreach (var pair in grouped)
            {
                if (shown >= limits.MaxApiTypes)
                {
                    hiddenTypes++;
                    continue;
                }
                shown++;

                var refs = FormatterLimits.Cap(pair.Value, limits.MaxKeyRefEntries, out var hidden);
                var codes = new List<string>();
                foreach (var reference in refs)
                    codes.Add("`" + reference + "`");
                sb.AppendLine("- `" + pair.Key + "`: " + string.Join(", ", codes) + FormatterLimits.More(hidden));
            }
            if (hiddenTypes > 0)
                sb.AppendLine("-" + FormatterLimits.More(hiddenTypes));

            return sb.ToString().TrimEnd();
        }

        /// <summary>헤더 요약({M} 카운트, A-2)과 본 섹션 렌더가 같은 집계 기준을 쓰도록 공용 계산.</summary>
        public static int CountUniqueReferences(Snapshot snapshot)
        {
            var count = 0;
            foreach (var pair in GroupByType(snapshot))
                count += pair.Value.Count;
            return count;
        }

        /// <summary>타입별 참조 필드 집계 (인스턴스 간 중복 제거, 타입명 정렬 = 결정론).</summary>
        private static SortedDictionary<string, List<string>> GroupByType(Snapshot snapshot)
        {
            var grouped = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var entry in snapshot.components)
            {
                if (entry.keyRefs.Count == 0)
                    continue;
                if (!grouped.TryGetValue(entry.type, out var refs))
                    grouped[entry.type] = refs = new List<string>();
                foreach (var reference in entry.keyRefs)
                {
                    if (!refs.Contains(reference))
                        refs.Add(reference);
                }
            }
            return grouped;
        }
    }
}
