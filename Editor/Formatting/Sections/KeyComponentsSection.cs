using System.Collections.Generic;
using System.Text;
using EngineContext.Editor.Model;

namespace EngineContext.Editor.Formatting.Sections
{
    /// <summary>
    /// 섹션 — Key Components: 존재하는 컴포넌트 타입 카탈로그 (FS §4.1).
    /// AI가 존재하지 않는 컴포넌트를 가정하지 않게 하는 핵심 섹션 (TA §4).
    /// 출력 큐레이션: 프로젝트 스크립트(커스텀)를 먼저, Unity 내장은 뒤에.
    /// 하위 섹션 "Public API"는 커스텀 MonoBehaviour의 public 필드/프로퍼티/메서드 시그니처만 노출한다
    /// (값·구현 제외). 직렬화 참조 필드는 v0.3 [A]에서 별도 "Wired References" 섹션으로 승격·이동됨
    /// (정보 이동이지 삭제 아님).
    /// </summary>
    internal static class KeyComponentsSection
    {
        public static string Render(Snapshot snapshot, FormatterLimits limits)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Key Components");

            if (snapshot.componentTypes.Count == 0 && snapshot.customComponentTypes.Count == 0)
            {
                sb.Append("_none detected_");
                return sb.ToString();
            }

            sb.AppendLine("Component types that exist in this project. **Do not assume components that are not in this list.**");
            sb.AppendLine();

            var customSet = new HashSet<string>(snapshot.customComponentTypes);
            var builtins = new List<string>();
            foreach (var type in snapshot.componentTypes)
            {
                if (!customSet.Contains(type))
                    builtins.Add(type);
            }

            // 프로젝트 스크립트 우선 — AI에게 가장 가치 높은 신호
            if (snapshot.customComponentTypes.Count > 0)
            {
                var capped = FormatterLimits.Cap(snapshot.customComponentTypes, limits.MaxComponentTypes, out var hidden);
                sb.AppendLine("**Project scripts (custom):** " + JoinAsCode(capped) + FormatterLimits.More(hidden));
            }
            else
            {
                sb.AppendLine("**Project scripts (custom):** none - no custom MonoBehaviour scripts were found under `Assets/`.");
            }

            if (builtins.Count > 0)
            {
                var capped = FormatterLimits.Cap(builtins, limits.MaxComponentTypes, out var hidden);
                sb.AppendLine();
                sb.AppendLine("**Unity / package built-ins:** " + JoinAsCode(capped) + FormatterLimits.More(hidden));
            }

            AppendPublicApi(sb, snapshot, limits);

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 커스텀 스크립트별 public API 스키마(필드/프로퍼티/메서드, 값·구현 제외).
        /// 참조 필드는 여기 없다 — "Wired References" 섹션(파일 상단)에서 렌더된다.
        /// </summary>
        private static void AppendPublicApi(StringBuilder sb, Snapshot snapshot, FormatterLimits limits)
        {
            if (snapshot.customApis.Count == 0)
                return;

            sb.AppendLine();
            sb.AppendLine("### Public API (Custom Scripts)");
            sb.AppendLine("_Schema only - `public` members declared directly on the type. No field values, no method bodies. "
                          + "Serialized reference fields are listed in the \"Wired References\" section above._");

            var capped = FormatterLimits.Cap(snapshot.customApis, limits.MaxApiTypes, out var hiddenTypes);
            foreach (var api in capped)
            {
                sb.AppendLine();
                sb.AppendLine("**" + api.type + "**");

                if (api.fields.Count > 0)
                    sb.AppendLine("- Fields: " + JoinFieldSchemas(api.fields, limits.MaxApiMembersPerList));
                if (api.properties.Count > 0)
                    sb.AppendLine("- Properties: " + JoinFieldSchemas(api.properties, limits.MaxApiMembersPerList));
                if (api.methods.Count > 0)
                    sb.AppendLine("- Methods: " + JoinMethodSchemas(api.methods, limits.MaxApiMembersPerList));
            }

            if (hiddenTypes > 0)
            {
                sb.AppendLine();
                sb.Append("-" + FormatterLimits.More(hiddenTypes));
            }
        }

        private static string JoinFieldSchemas(List<FieldSchema> fields, int max)
        {
            var capped = FormatterLimits.Cap(fields, max, out var hidden);
            var codes = new List<string>();
            foreach (var field in capped)
                codes.Add("`" + field.name + ": " + field.type + "`");
            return string.Join(", ", codes) + FormatterLimits.More(hidden);
        }

        private static string JoinMethodSchemas(List<MethodSchema> methods, int max)
        {
            var capped = FormatterLimits.Cap(methods, max, out var hidden);
            var codes = new List<string>();
            foreach (var method in capped)
                codes.Add("`" + method.name + method.signature + "`");
            return string.Join(", ", codes) + FormatterLimits.More(hidden);
        }

        private static string JoinAsCode(IReadOnlyList<string> items)
        {
            var codes = new List<string>(items.Count);
            foreach (var item in items)
                codes.Add("`" + item + "`");
            return string.Join(", ", codes);
        }
    }
}
