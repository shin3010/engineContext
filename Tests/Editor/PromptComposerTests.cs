using NUnit.Framework;
using EngineContext.Editor.Formatting;

namespace EngineContext.Editor.Tests
{
    /// <summary>Feature Spec v0.2 §4 검증: Prompt Builder 템플릿 조립(규칙기반, 결정론).</summary>
    public class PromptComposerTests
    {
        [Test]
        public void Compose_FollowsFixedTemplateOrder()
        {
            var output = PromptComposer.Compose("타이틀에 설정 버튼 추가", "기존 스타일 유지",
                "# Project Context - Test1\n- Product: `Test1`");

            var taskIdx = output.IndexOf("# Task", System.StringComparison.Ordinal);
            var constraintsIdx = output.IndexOf("# Constraints / Notes", System.StringComparison.Ordinal);
            var contextIdx = output.IndexOf("# Project Context", System.StringComparison.Ordinal);
            var requestIdx = output.IndexOf("# Request", System.StringComparison.Ordinal);

            Assert.Greater(constraintsIdx, taskIdx);
            Assert.Greater(contextIdx, constraintsIdx);
            Assert.Greater(requestIdx, contextIdx);

            StringAssert.Contains("타이틀에 설정 버튼 추가", output);
            StringAssert.Contains("기존 스타일 유지", output);
            StringAssert.Contains("- Product: `Test1`", output);   // CLAUDE.md 그대로 삽입 (Option B)
            StringAssert.Contains("ask instead", output);           // 가장 중요한 한 줄
        }

        [Test]
        public void Compose_OmitsConstraintsSectionWhenInstructionsEmpty()
        {
            var output = PromptComposer.Compose("버튼 추가", "", "context");
            StringAssert.DoesNotContain("# Constraints / Notes", output); // 빈 헤더 방치 금지
        }

        [Test]
        public void Compose_IsDeterministic()
        {
            var a = PromptComposer.Compose("goal", "notes", "context");
            var b = PromptComposer.Compose("goal", "notes", "context");
            Assert.AreEqual(a, b);
        }

        // --- v0.3 [B] A-2: Focus 강조 레이어 (DoD B-5) ---

        private const string WiredContext =
            "# Project Context - Test1\n" +
            "\n" +
            "## Project Overview\n" +
            "- Product: `Test1`\n" +
            "\n" +
            "## Event Wiring\n" +
            "UnityEvent connections wired in the Inspector. These are NOT visible in code.\n" +
            "- `TitleScene/Canvas/botMode (Button).onClick` → `GameManager.Bot()`\n" +
            "- `TitleScene/Canvas/friendMode (Button).onClick` → `GameManager.Friend()`\n" +
            "\n" +
            "## Key Components\n" +
            "**Project scripts (custom):** `GameManager`, `ModeManager`\n" +
            "\n" +
            "### Public API (Custom Scripts)\n" +
            "\n" +
            "**GameManager**\n" +
            "- Fields: `mode: string`, `modeSelacted: int`\n" +
            "- Methods: `Bot(): void`, `Friend(): void`\n" +
            "\n" +
            "**ModeManager**\n" +
            "- Methods: `Bot(): void`, `Friend(): void`\n";

        [Test]
        public void Focus_KoreanGoal_HighlightsMatchedWiring()
        {
            // DoD: "botMode 버튼 동작 바꿔줘" → Focus에 botMode 배선이 표시, 전체 컨텍스트는 그대로
            var output = PromptComposer.Compose("botMode 버튼 동작 바꿔줘", "", WiredContext);
            StringAssert.Contains("# Focus (auto-detected)", output);
            StringAssert.Contains("[Event Wiring]", output);
            StringAssert.Contains("botMode (Button).onClick", output);
            Assert.Less(
                output.IndexOf("# Focus", System.StringComparison.Ordinal),
                output.IndexOf("# Project Context", System.StringComparison.Ordinal));
        }

        [Test]
        public void Focus_EnglishGoal_MatchesSameIdentifier()
        {
            // DoD: 언어 무관 — 식별자 문자열은 동일하게 존재
            var output = PromptComposer.Compose("change botMode button", "", WiredContext);
            StringAssert.Contains("# Focus (auto-detected)", output);
            StringAssert.Contains("botMode (Button).onClick", output);
        }

        [Test]
        public void Focus_NoIdentifierInGoal_OmittedSafely()
        {
            // DoD: 영문 식별자 없는 목적문 → Focus 생략, 기존 Option B와 완전히 동일 (안전한 실패)
            var output = PromptComposer.Compose("설정 화면 좀 고쳐줘", "", WiredContext);
            StringAssert.DoesNotContain("# Focus", output);
            StringAssert.Contains("ask instead", output); // 기존 Request 문구 유지
        }

        [Test]
        public void Focus_NeverRemovesAnythingFromProjectContext()
        {
            // 불변식: 어떤 목적문에서도 # Project Context == CLAUDE.md 전체 (누락 0)
            var withFocus = PromptComposer.Compose("botMode 버튼 동작 바꿔줘", "", WiredContext);
            var withoutFocus = PromptComposer.Compose("설정 화면 좀 고쳐줘", "", WiredContext);
            StringAssert.Contains("`TitleScene/Canvas/friendMode (Button).onClick`", withFocus);   // 매칭 안 된 항목도 존재
            StringAssert.Contains("`TitleScene/Canvas/friendMode (Button).onClick`", withoutFocus);
            StringAssert.Contains(WiredContext.TrimEnd(), withFocus);
            StringAssert.Contains(WiredContext.TrimEnd(), withoutFocus);
        }

        [Test]
        public void Focus_IsDeterministic()
        {
            var a = PromptComposer.Compose("botMode 버튼 동작 바꿔줘", "", WiredContext);
            var b = PromptComposer.Compose("botMode 버튼 동작 바꿔줘", "", WiredContext);
            Assert.AreEqual(a, b);
        }

        // --- 회귀: 접미사(Mode)만 겹치는 두 식별자가 서로를 오매칭하지 않아야 함 ---

        [Test]
        public void Focus_SharedSuffixIdentifiers_DoNotCrossMatch()
        {
            // "botMode 버튼 바꿔줘" → Focus에는 botMode/Bot()만, friendMode/Friend()는 절대 없음.
            // (전체 컨텍스트에는 friendMode가 그대로 있으므로 Focus 영역만 슬라이스해 검증한다.)
            var focus = FocusSection(PromptComposer.Compose("botMode 버튼 바꿔줘", "", WiredContext));
            StringAssert.Contains("botMode", focus);
            StringAssert.Contains("Bot()", focus);
            StringAssert.DoesNotContain("friendMode", focus);
            StringAssert.DoesNotContain("Friend", focus);
        }

        [Test]
        public void Focus_ReverseMatch_SelectsOnlyFriendMode()
        {
            // 역방향도 대칭이어야 함: "friendMode" 목적문 → friendMode/Friend()만, botMode/Bot()는 없음.
            var focus = FocusSection(PromptComposer.Compose("friendMode 좀 고쳐줘", "", WiredContext));
            StringAssert.Contains("friendMode", focus);
            StringAssert.Contains("Friend()", focus);
            StringAssert.DoesNotContain("botMode", focus);
            StringAssert.DoesNotContain("Bot()", focus);
        }

        [Test]
        public void Focus_LongestMatch_DoesNotPullFragmentsOfMatchedIdentifier()
        {
            // "botMode"가 통째로 매칭됐으면 그 조각(bot/mode)이 무관한 후보를 끌어오면 안 됨.
            var focus = FocusSection(PromptComposer.Compose("botMode 버튼 바꿔줘", "", WiredContext));

            // 허용: botMode 배선 자체 (그 라인의 타깃이 GameManager.Bot()인 것은 원문 그대로라 OK)
            StringAssert.Contains("botMode", focus);

            // 금지: 조각 매칭으로 끌려오던 항목들
            StringAssert.DoesNotContain("[Public API]", focus);    // Bot()/mode 등 Public API 단독 강조 없음
            StringAssert.DoesNotContain("ModeManager", focus);     // ModeManager.Bot() 없음
            StringAssert.DoesNotContain("GameManager.mode", focus); // mode 필드 없음
            StringAssert.DoesNotContain("friendMode", focus);
        }

        /// <summary>프롬프트에서 "# Focus" 블록만 잘라낸다(그 아래 # Project Context 전체는 제외).</summary>
        private static string FocusSection(string prompt)
        {
            var start = prompt.IndexOf("# Focus", System.StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;
            var end = prompt.IndexOf("# Project Context", start, System.StringComparison.Ordinal);
            return end < 0 ? prompt.Substring(start) : prompt.Substring(start, end - start);
        }
    }
}
