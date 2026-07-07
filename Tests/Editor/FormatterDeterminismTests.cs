using NUnit.Framework;
using EngineContext.Editor.Formatting;

namespace EngineContext.Editor.Tests
{
    /// <summary>FS §4.5 검증: 동일 Snapshot → 바이트 동일 CLAUDE.md (idempotency).</summary>
    public class FormatterDeterminismTests
    {
        [Test]
        public void SameSnapshot_FormattedTwice_IsByteIdentical()
        {
            var snapshot = SampleSnapshots.Create();
            var first = ClaudeMarkdownFormatter.Format(snapshot);
            var second = ClaudeMarkdownFormatter.Format(snapshot);
            Assert.AreEqual(first, second);
        }

        [Test]
        public void EquivalentSnapshots_ProduceIdenticalOutput()
        {
            var a = ClaudeMarkdownFormatter.Format(SampleSnapshots.Create());
            var b = ClaudeMarkdownFormatter.Format(SampleSnapshots.Create());
            Assert.AreEqual(a, b);
        }

        [Test]
        public void Output_ContainsFixedSectionOrder()
        {
            var output = ClaudeMarkdownFormatter.Format(SampleSnapshots.Create());

            // v0.3 [A-1] 고정 섹션 순서 검증 — 배선(차별점)이 Project Overview 바로 다음으로 승격
            var sections = new[]
            {
                "## Project Overview",
                "## Event Wiring",
                "## Wired References",
                "## Tech Stack / Packages",
                "## Architecture Conventions (Inferred)",
                "## Folder & Assembly Map",
                "## Scene / Hierarchy Summary",
                "## Prefab Inventory",
                "## ScriptableObject Catalog",
                "## Key Components",
                "## Notes & Caveats"
            };

            var lastIndex = -1;
            foreach (var section in sections)
            {
                var index = output.IndexOf(section, System.StringComparison.Ordinal);
                Assert.GreaterOrEqual(index, 0, "섹션이 없음: " + section);
                Assert.Greater(index, lastIndex, "섹션 순서가 어긋남: " + section);
                lastIndex = index;
            }
        }

        [Test]
        public void Output_MarksIdentifiersAsInlineCode()
        {
            var output = ClaudeMarkdownFormatter.Format(SampleSnapshots.Create());
            StringAssert.Contains("`PlayerController`", output); // 식별자는 인라인 코드 (FS §4.2)
            StringAssert.Contains("`jp.hadashikick.vcontainer`", output);
        }

        [Test]
        public void Output_OmitsUnityBuiltinModulePackages()
        {
            // 출력 큐레이션: com.unity.modules.* 는 정보가치가 없어 생략, 개수만 고지
            var output = ClaudeMarkdownFormatter.Format(SampleSnapshots.Create());
            StringAssert.DoesNotContain("com.unity.modules.audio", output);
            StringAssert.Contains("omitted", output);
        }

        [Test]
        public void Output_ListsCustomScriptsBeforeBuiltins()
        {
            var output = ClaudeMarkdownFormatter.Format(SampleSnapshots.Create());
            var customIndex = output.IndexOf("Project scripts (custom)", System.StringComparison.Ordinal);
            var builtinIndex = output.IndexOf("Unity / package built-ins", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(customIndex, 0, "커스텀 스크립트 구획이 없음");
            Assert.GreaterOrEqual(builtinIndex, 0, "내장 컴포넌트 구획이 없음");
            Assert.Less(customIndex, builtinIndex, "커스텀 스크립트가 내장보다 먼저 나와야 함");
        }

        [Test]
        public void Output_ListsBuildScenesWithDisabledMarked()
        {
            // Assets/Scenes 기준(EditorBuildSettings)이며, 현재 열린 씬 요약과는 별개 정보여야 한다.
            var output = ClaudeMarkdownFormatter.Format(SampleSnapshots.Create());
            StringAssert.Contains("Scenes in build:", output);
            StringAssert.Contains("`TitleScene`", output);
            StringAssert.Contains("`GameScene`", output);
            StringAssert.Contains("disabled: `SampleScene`", output);
        }

        [Test]
        public void Output_ListsCustomScriptNotYetPlacedInAnyScene()
        {
            // customComponentTypes는 이제 Assets 스캔 기준이므로, 씬/프리팹에 없는 스크립트도 노출돼야 한다.
            var output = ClaudeMarkdownFormatter.Format(SampleSnapshots.Create());
            StringAssert.Contains("`UnusedHelper`", output);
        }

        [Test]
        public void Output_ListsPublicApiSchemaForCustomScripts()
        {
            var output = ClaudeMarkdownFormatter.Format(SampleSnapshots.Create());
            StringAssert.Contains("### Public API (Custom Scripts)", output);
            StringAssert.Contains("**PlayerController**", output);
            StringAssert.Contains("- Fields: `maxSpeed: float`", output);
            StringAssert.Contains("- Properties: `IsGrounded: bool`", output);
            StringAssert.Contains("`Jump(): void`", output);
            StringAssert.Contains("`Move(float direction): void`", output);
            StringAssert.Contains("**EnemyAI**", output);
            StringAssert.Contains("`Attack(): void`", output);
        }

        [Test]
        public void Output_PublicApiStaysInsideKeyComponentsSection()
        {
            // 새 최상위 섹션이 아니라 기존 "## Key Components" 안에 위치해야 한다 (구조 유지).
            var output = ClaudeMarkdownFormatter.Format(SampleSnapshots.Create());
            var keyComponentsIndex = output.IndexOf("## Key Components", System.StringComparison.Ordinal);
            var apiIndex = output.IndexOf("### Public API (Custom Scripts)", System.StringComparison.Ordinal);
            var notesIndex = output.IndexOf("## Notes & Caveats", System.StringComparison.Ordinal);
            Assert.Greater(apiIndex, keyComponentsIndex);
            Assert.Less(apiIndex, notesIndex);
        }

        [Test]
        public void Output_ListsEventWiringSection()
        {
            // Wiring Capture 패치 §변경1: UnityEvent 배선이 Event Wiring 섹션에 표시
            var output = ClaudeMarkdownFormatter.Format(SampleSnapshots.Create());
            StringAssert.Contains("## Event Wiring", output);
            StringAssert.Contains("TitleScene/Canvas/botMode (Button).onClick", output);
            StringAssert.Contains("ModeManager.Bot()", output);
        }

        [Test]
        public void Output_WiringSectionsPromotedRightAfterProjectOverview()
        {
            // v0.3 [A-1]: Event Wiring / Wired References가 Project Overview 바로 다음(3~4번째)에 위치
            var output = ClaudeMarkdownFormatter.Format(SampleSnapshots.Create());
            var overviewIndex = output.IndexOf("## Project Overview", System.StringComparison.Ordinal);
            var wiringIndex = output.IndexOf("## Event Wiring", System.StringComparison.Ordinal);
            var wiredRefsIndex = output.IndexOf("## Wired References", System.StringComparison.Ordinal);
            var techStackIndex = output.IndexOf("## Tech Stack / Packages", System.StringComparison.Ordinal);
            Assert.Greater(wiringIndex, overviewIndex);
            Assert.Greater(wiredRefsIndex, wiringIndex);
            Assert.Less(wiredRefsIndex, techStackIndex);
        }

        [Test]
        public void Output_HeaderSummaryShowsHonestWiringCounts()
        {
            // v0.3 [A-2]: 헤더 요약의 카운트는 실제 스캔 결과 그대로 (샘플: 이벤트 1, 참조 필드 2)
            var output = ClaudeMarkdownFormatter.Format(SampleSnapshots.Create());
            StringAssert.Contains(
                "> This snapshot captures 1 Inspector-wired event binding(s) and 2 serialized reference field(s)",
                output);
        }

        [Test]
        public void Output_ShowsReferenceFieldsInWiredReferencesSection()
        {
            // v0.3 [A]: 참조 필드는 독립 "Wired References" 섹션으로 이동 (배선/미할당 모두, 정보 손실 0)
            var output = ClaudeMarkdownFormatter.Format(SampleSnapshots.Create());
            StringAssert.Contains("## Wired References", output);
            StringAssert.Contains("`PlayerController`:", output);
            StringAssert.Contains("targetCamera → MainCamera", output);           // 배선됨
            StringAssert.Contains("spawnPoint → Transform (unassigned)", output); // 선언됐지만 미할당
            // Key Components의 Public API에는 더 이상 참조 필드 줄이 없어야 함 (이동이지 중복 아님)
            StringAssert.DoesNotContain("- Reference fields:", output);
            StringAssert.DoesNotContain("### Key References", output);
        }
    }
}
