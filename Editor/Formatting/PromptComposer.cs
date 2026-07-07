using System;
using System.Collections.Generic;
using System.Text;

namespace EngineContext.Editor.Formatting
{
    /// <summary>
    /// L5 — Prompt Builder의 규칙기반 조립기 (Feature Spec v0.2 Option B + Patch v0.3 [B] A-2 강조 레이어).
    /// 목적/지시사항 + 현재 CLAUDE.md 전체를 고정 템플릿으로 감싼다. LLM·네트워크 없음. 결정론적.
    ///
    /// v0.3 [B] A-2: 목적문을 해석하지 않는다. 대신 CLAUDE.md에 실제로 등장하는 식별자를
    /// 목적문에서 역방향으로 찾아(층위 1 정확 + 층위 2 표면 정규화까지만 — 의미 동의어 금지)
    /// 관련 항목을 "# Focus" 블록으로 강조한다. 강조일 뿐이며 포함 여부에는 절대 관여하지 않는다:
    /// 어떤 경우에도 "# Project Context"에는 CLAUDE.md 전체가 그대로 들어간다(삭제/압축 0).
    /// 매칭 0건 또는 추출 실패 시 Focus를 생략하고 기존 Option B와 완전히 동일하게 동작한다(안전한 실패).
    ///
    /// 식별자 추출은 IR 대신 CLAUDE.md 텍스트에서 한다 — Prompt Builder는 파이프라인과 독립적으로
    /// "루트 파일을 그대로 읽는" 것이 기존 계약(F5)이며, Patch Spec B-4가 이 경로를 허용한다.
    /// </summary>
    public static class PromptComposer
    {
        private const int MaxFocusItems = 12;      // 강조 항목 상한 — 초과분은 (+N more), 아무것도 빼지 않음
        private const int MinIdentifierLength = 3; // 너무 짧은 토큰의 오탐 방지

        // 고정 상수 문구 (매 실행 동일). 매칭 0건이면 기존 Option B 문구를 그대로 사용한다.
        private const string RequestBlock =
            "# Request\n" +
            "Please use the project context above as ground truth. Only reference\n" +
            "components, fields, and event bindings that actually appear in it.\n" +
            "If something needed for this task isn't in the context, ask instead\n" +
            "of assuming it exists.";

        private const string RequestBlockWithFocus =
            "# Request\n" +
            "Please use the project context above as ground truth. The Focus section\n" +
            "highlights the parts most likely relevant, but treat the full context as\n" +
            "authoritative. Only reference components, fields, and event bindings that\n" +
            "actually appear above. If something needed isn't there, ask instead of\n" +
            "assuming it exists.";

        // 타입 키워드 등 — 프로젝트 식별자가 아니므로 매칭 대상에서 제외
        private static readonly HashSet<string> IdentifierStopwords = new HashSet<string>(StringComparer.Ordinal)
        {
            "void", "string", "int", "float", "bool", "double", "long",
            "object", "true", "false", "null", "none"
        };

        /// <summary>
        /// Feature Spec §4 템플릿 순서로 조립. 지시사항이 비면 Constraints/Notes 섹션은 통째로 생략.
        /// projectContext는 CLAUDE.md 전체(선별·압축 없음, Option B 핵심 — v0.3에서도 불변).
        /// </summary>
        public static string Compose(string goal, string instructions, string projectContext)
        {
            var focus = BuildFocusBlock(goal, projectContext); // null이면 기존 Option B와 동일 동작

            var sb = new StringBuilder();

            sb.AppendLine("# Task");
            sb.AppendLine((goal ?? string.Empty).Trim());
            sb.AppendLine();

            var trimmedInstructions = (instructions ?? string.Empty).Trim();
            if (trimmedInstructions.Length > 0) // 비면 섹션 자체 생략 (빈 헤더 방치 금지)
            {
                sb.AppendLine("# Constraints / Notes");
                sb.AppendLine(trimmedInstructions);
                sb.AppendLine();
            }

            if (focus != null)
            {
                sb.AppendLine(focus);
                sb.AppendLine();
            }

            sb.AppendLine("# Project Context");
            sb.AppendLine((projectContext ?? string.Empty).TrimEnd()); // 그대로 삽입, 요약 없음 (불변식)
            sb.AppendLine();

            sb.Append(focus != null ? RequestBlockWithFocus : RequestBlock);
            return sb.ToString();
        }

        // --- v0.3 [B]: Focus 블록 ---

        private sealed class FocusCandidate
        {
            public int priority;             // 1 = Event Wiring / Wired References, 2 = Public API, 3 = Hierarchy
            public string display;           // Focus에 그대로 실을 라벨 (원문에서 복사한 요약, 원본 대체 아님)
            public List<string> identifiers; // 이 후보가 소유한 whole 식별자들(소문자). 매칭 단위.
        }

        /// <summary>매칭 항목이 있으면 "# Focus" 블록 문자열, 없으면 null (안전한 실패 = 블록 생략).</summary>
        private static string BuildFocusBlock(string goal, string projectContext)
        {
            try
            {
                var candidates = CollectCandidates(projectContext);
                if (candidates.Count == 0)
                    return null;

                var goalLower = (goal ?? string.Empty).ToLowerInvariant();
                var goalCondensed = Condense(goalLower);
                if (goalCondensed.Length == 0)
                    return null;

                // 최장 일치 우선 + 겹침 방지로 목적문에서 "이긴" 식별자 집합을 먼저 확정한다.
                // botMode(7자)가 어떤 글자 구간을 점유하면, 그 구간과 겹치는 bot(3)·mode(4) 단독 매칭은 차단 →
                // 이미 정확히 매칭된 긴 식별자의 조각이 무관한 후보(GameManager.mode/Bot(), ModeManager.Bot() 등)를
                // 끌어오지 못한다. 하위 토큰 분해·교집합·파생 확장은 하지 않는다.
                var matchedIdentifiers = ResolveMatchedIdentifiers(candidates, goalCondensed);
                if (matchedIdentifiers.Count == 0)
                    return null;

                var selected = new List<FocusCandidate>();
                for (var priority = 1; priority <= 3; priority++) // 배선(1) → Public API(2) → Hierarchy(3) 순
                {
                    foreach (var candidate in candidates)
                    {
                        if (candidate.priority == priority && OwnsMatchedIdentifier(candidate, matchedIdentifiers))
                            selected.Add(candidate);
                    }
                }
                if (selected.Count == 0)
                    return null;

                var sb = new StringBuilder();
                sb.AppendLine("# Focus (auto-detected)");
                sb.AppendLine("This task appears related to the following, based on identifiers found");
                sb.AppendLine("in your request. These are highlighted from the full context below -");
                sb.AppendLine("nothing has been removed.");
                var shown = 0;
                foreach (var candidate in selected)
                {
                    if (shown >= MaxFocusItems)
                        break;
                    shown++;
                    sb.AppendLine("- " + candidate.display);
                }
                if (selected.Count > shown)
                    sb.AppendLine("- (+" + (selected.Count - shown) + " more)");
                return sb.ToString().TrimEnd();
            }
            catch (Exception)
            {
                return null; // B-3: 식별자 추출 실패 → 기존 Option B 동작으로 폴백
            }
        }

        /// <summary>
        /// 목적문에서 "이긴" 식별자 집합을 확정한다 (최장 일치 우선 + 겹침 방지).
        /// 후보 전체 식별자를 (표면 정규화 기준) 길이 내림차순으로 훑으며, 목적문에서 아직 더 긴 식별자에
        /// 점유되지 않은 구간에 등장하면 매칭으로 인정하고 그 구간을 점유한다. 이렇게 하면 긴 식별자의
        /// 하위 조각이 같은 위치에서 다시 단독 매칭되는 오탐을 막는다. 매칭은 표면 정규화(영숫자만) 공간에서
        /// 수행 — 층위 1(정확)은 층위 2(정규화)에 포함되므로 한 공간으로 통일한다.
        /// </summary>
        private static HashSet<string> ResolveMatchedIdentifiers(List<FocusCandidate> candidates, string goalCondensed)
        {
            var vocabulary = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in candidates)
            {
                foreach (var identifier in candidate.identifiers)
                {
                    if (seen.Add(identifier))
                        vocabulary.Add(identifier);
                }
            }
            // 길이 내림차순(정규화 기준) + 동률은 ordinal → 결정론
            vocabulary.Sort((a, b) =>
            {
                var byLength = Condense(b).Length.CompareTo(Condense(a).Length);
                return byLength != 0 ? byLength : string.CompareOrdinal(a, b);
            });

            var matched = new HashSet<string>(StringComparer.Ordinal);
            var occupied = new bool[goalCondensed.Length];
            foreach (var identifier in vocabulary)
            {
                var needle = Condense(identifier);
                if (needle.Length < MinIdentifierLength)
                    continue;

                var from = 0;
                while (true)
                {
                    var index = goalCondensed.IndexOf(needle, from, StringComparison.Ordinal);
                    if (index < 0)
                        break;
                    if (!OverlapsOccupied(occupied, index, needle.Length))
                    {
                        for (var k = index; k < index + needle.Length; k++)
                            occupied[k] = true;
                        matched.Add(identifier);
                        break; // 이 식별자는 매칭 확정 — 한 구간 점유로 충분
                    }
                    from = index + 1; // 겹치는 자리면 다음 등장 위치를 계속 탐색(다른 자리엔 단독 매칭 허용)
                }
            }
            return matched;
        }

        private static bool OverlapsOccupied(bool[] occupied, int start, int length)
        {
            for (var k = start; k < start + length; k++)
            {
                if (occupied[k])
                    return true;
            }
            return false;
        }

        private static bool OwnsMatchedIdentifier(FocusCandidate candidate, HashSet<string> matched)
        {
            foreach (var identifier in candidate.identifiers)
            {
                if (matched.Contains(identifier))
                    return true;
            }
            return false;
        }

        /// <summary>CLAUDE.md 텍스트에서 강조 후보(배선/Public API/Hierarchy 항목)를 문서 순서대로 수집.</summary>
        private static List<FocusCandidate> CollectCandidates(string projectContext)
        {
            var candidates = new List<FocusCandidate>();
            if (string.IsNullOrEmpty(projectContext))
                return candidates;

            var section = "";
            string currentType = null;
            foreach (var rawLine in projectContext.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    section = line.Substring(3).Trim();
                    currentType = null;
                    continue;
                }

                var trimmed = line.Trim();
                if (trimmed.StartsWith("- (+", StringComparison.Ordinal))
                    continue; // 절단 고지 라인은 후보 아님

                if (section == "Event Wiring")
                {
                    if (trimmed.StartsWith("- `", StringComparison.Ordinal))
                        candidates.Add(new FocusCandidate
                        {
                            priority = 1,
                            display = trimmed.Substring(2).Trim() + "   [Event Wiring]",
                            identifiers = ExtractIdentifiers(trimmed)
                        });
                }
                else if (section == "Wired References")
                {
                    if (trimmed.StartsWith("- `", StringComparison.Ordinal))
                        candidates.Add(new FocusCandidate
                        {
                            priority = 1,
                            display = trimmed.Substring(2).Trim() + "   [Wired References]",
                            identifiers = ExtractIdentifiers(trimmed)
                        });
                }
                else if (section == "Key Components")
                {
                    // "**GameManager**" 형태만 스크립트 블록 시작으로 인정
                    // ("**Project scripts (custom):** ..." 같은 라인은 **로 끝나지 않아 제외됨)
                    if (trimmed.StartsWith("**", StringComparison.Ordinal)
                        && trimmed.EndsWith("**", StringComparison.Ordinal) && trimmed.Length > 4)
                    {
                        currentType = trimmed.Substring(2, trimmed.Length - 4);
                        continue;
                    }
                    if (currentType != null
                        && (trimmed.StartsWith("- Fields:", StringComparison.Ordinal)
                            || trimmed.StartsWith("- Properties:", StringComparison.Ordinal)
                            || trimmed.StartsWith("- Methods:", StringComparison.Ordinal)))
                    {
                        foreach (var span in ExtractCodeSpans(trimmed))
                            candidates.Add(new FocusCandidate
                            {
                                priority = 2,
                                display = "`" + currentType + "." + span + "`   [Public API]",
                                identifiers = ExtractIdentifiers(currentType + " " + span)
                            });
                    }
                }
                else if (section == "Scene / Hierarchy Summary")
                {
                    if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                        candidates.Add(new FocusCandidate
                        {
                            priority = 3,
                            display = trimmed.Substring(2).Trim() + "   [Hierarchy]",
                            identifiers = ExtractIdentifiers(trimmed)
                        });
                }
            }
            return candidates;
        }

        /// <summary>
        /// 텍스트에서 whole 식별자만 추출한다(소문자). 식별자 경계 = 영숫자/언더스코어가 아닌 문자.
        /// 카멜케이스는 쪼개지 않는다 — "botMode"는 "botmode" 하나, 언더스코어도 유지("p1_button" 하나).
        /// 이렇게 해야 접미사만 겹치는 식별자끼리 하위 토큰 교집합으로 오매칭되지 않는다.
        /// 길이·스톱워드·순수숫자 필터 적용, 중복 제거(등장 순서 유지).
        /// </summary>
        private static List<string> ExtractIdentifiers(string text)
        {
            var identifiers = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var lower = text.ToLowerInvariant();
            var current = new StringBuilder();
            for (var i = 0; i <= lower.Length; i++)
            {
                var isIdentChar = i < lower.Length && (char.IsLetterOrDigit(lower[i]) || lower[i] == '_');
                if (isIdentChar)
                {
                    current.Append(lower[i]);
                    continue;
                }
                if (current.Length == 0)
                    continue;

                var token = current.ToString();
                current.Length = 0;
                if (IsMatchableIdentifier(token) && seen.Add(token))
                    identifiers.Add(token);
            }
            return identifiers;
        }

        /// <summary>매칭에 쓸 만한 식별자인가 — 스톱워드 아님, (언더스코어 제외) 길이 충분, 순수 숫자 아님.</summary>
        private static bool IsMatchableIdentifier(string token)
        {
            if (IdentifierStopwords.Contains(token))
                return false;
            var condensed = Condense(token);
            if (condensed.Length < MinIdentifierLength)
                return false;
            foreach (var ch in condensed)
            {
                if (!char.IsDigit(ch))
                    return true; // 문자가 하나라도 있으면 유효 (버전 숫자 같은 순수 숫자 토큰 제외)
            }
            return false;
        }

        /// <summary>층위 2 표면 정규화: 영숫자만 남긴다 ("bot_mode 버튼" / "bot mode" → 동일 비교 가능).</summary>
        private static string Condense(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                if (char.IsLetterOrDigit(ch))
                    sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>라인 안의 백틱 코드 스팬 내용 추출 ("- Methods: `Bot(): void`, `Friend(): void`").</summary>
        private static List<string> ExtractCodeSpans(string line)
        {
            var spans = new List<string>();
            var start = -1;
            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] != '`')
                    continue;
                if (start < 0)
                {
                    start = i + 1;
                }
                else
                {
                    if (i > start)
                        spans.Add(line.Substring(start, i - start));
                    start = -1;
                }
            }
            return spans;
        }
    }
}
