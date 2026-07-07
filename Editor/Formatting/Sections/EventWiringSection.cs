using System.Text;
using EngineContext.Editor.Model;

namespace EngineContext.Editor.Formatting.Sections
{
    /// <summary>
    /// 섹션 (신규) — Event Wiring: Inspector에서 배선된 UnityEvent 연결 (Wiring Capture 패치 §변경1).
    /// 이 연결은 코드에 나타나지 않으므로 AI가 "어느 버튼이 무엇을 호출하는지" 알게 하는 핵심 정보.
    /// v0.3 [A-1]에서 Project Overview 바로 다음으로 승격 (차별점 전면 배치). 데이터 없으면 섹션 생략.
    /// </summary>
    internal static class EventWiringSection
    {
        public static string Render(Snapshot snapshot, FormatterLimits limits)
        {
            if (snapshot.eventBindings.Count == 0)
                return ""; // 배선 없으면 섹션 생략 (FS §4.2)

            var sb = new StringBuilder();
            sb.AppendLine("## Event Wiring");
            sb.AppendLine("UnityEvent connections wired in the Inspector. These are NOT visible in code.");

            var capped = FormatterLimits.Cap(snapshot.eventBindings, limits.MaxEventBindings, out var hidden);
            foreach (var binding in capped)
            {
                var source = "`" + binding.sourcePath + " (" + binding.sourceComponent + ")." + binding.eventName + "`";
                string target;
                if (!string.IsNullOrEmpty(binding.targetComponent))
                    target = "`" + binding.targetComponent + "." + binding.method + "()`";
                else
                    target = "`" + binding.method + "()` (target: `" + binding.targetPath + "`)";
                sb.AppendLine("- " + source + " → " + target);
            }
            if (hidden > 0)
                sb.AppendLine("-" + FormatterLimits.More(hidden));

            return sb.ToString().TrimEnd();
        }
    }
}
