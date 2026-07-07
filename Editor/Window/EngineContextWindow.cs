using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using EngineContext.Editor.Formatting;
using EngineContext.Editor.IO;
using EngineContext.Editor.Model;
using EngineContext.Editor.Pipeline;

namespace EngineContext.Editor.Windows
{
    /// <summary>
    /// L1 — 제품의 유일한 화면. 상태 머신(Generating/Review/Done/Error) 소유 (TA §6, IA §2).
    /// 렌더링: IMGUI (사용자 확정). Pipeline과 Writer만 안다 — 수집/포맷 세부는 모른다.
    /// 불변 계약: Apply 이전에는 절대 파일을 쓰지 않는다 (Review 게이트).
    /// </summary>
    public class EngineContextWindow : EditorWindow
    {
        private enum WindowState
        {
            Generating,
            Review,
            Done,
            Error
        }

        // 창 상단 탭 — 새 창이 아니라 기존 창 내부의 모드 전환 (Feature Spec v0.2)
        private enum Tab
        {
            Generate,
            BuildPrompt
        }

        // Aha 유도(첫 작업 제안)에서 제외할 Unity 내장 컴포넌트 (UX §5 — 실존 커스텀 컴포넌트를 제안에 사용)
        private static readonly HashSet<string> BuiltinComponentTypes = new HashSet<string>
        {
            "Transform", "RectTransform", "Camera", "Light", "MeshRenderer", "MeshFilter",
            "SkinnedMeshRenderer", "SpriteRenderer", "Canvas", "CanvasRenderer", "CanvasScaler",
            "GraphicRaycaster", "EventSystem", "StandaloneInputModule", "InputSystemUIInputModule",
            "AudioListener", "AudioSource", "Animator", "Animation", "Rigidbody", "Rigidbody2D",
            "BoxCollider", "SphereCollider", "CapsuleCollider", "MeshCollider", "BoxCollider2D",
            "CircleCollider2D", "Image", "RawImage", "Text", "TextMeshProUGUI", "Button", "Toggle",
            "Slider", "ScrollRect", "Scrollbar", "ParticleSystem", "ParticleSystemRenderer",
            "TrailRenderer", "LineRenderer", "Terrain", "NavMeshAgent", "CharacterController"
        };

        [NonSerialized] private WindowState state = WindowState.Generating;
        [NonSerialized] private ScanScope scope = ScanScope.Full;
        [NonSerialized] private string draft;
        [NonSerialized] private Snapshot snapshot;
        [NonSerialized] private string logSummary;
        [NonSerialized] private string errorMessage;
        [NonSerialized] private string savedPath;
        [NonSerialized] private bool runQueued;
        private Vector2 scroll;

        // Prompt Builder 탭 상태
        [NonSerialized] private Tab tab = Tab.Generate;
        private string promptGoal = "";
        private string promptInstructions = "";
        [NonSerialized] private string promptPreview;
        [NonSerialized] private string promptError;
        [NonSerialized] private bool promptContextAvailable;
        [NonSerialized] private string promptContextMessage;
        private Vector2 promptScroll;

        public static void Open()
        {
            var window = GetWindow<EngineContextWindow>("EngineContext");
            window.minSize = new Vector2(520f, 380f);
            window.tab = Tab.Generate;
            window.StartGeneration(ScanScope.Full);
        }

        /// <summary>Build Prompt 진입점 — 창을 열되 생성은 트리거하지 않는다 (Feature Spec: 새 창 아님).</summary>
        public static void OpenPromptBuilder()
        {
            var window = GetWindow<EngineContextWindow>("EngineContext");
            window.minSize = new Vector2(520f, 380f);
            window.tab = Tab.BuildPrompt;
            window.RefreshPromptContextStatus();
            window.Repaint();
        }

        /// <summary>F11 Regenerate 포함 — 최초와 재실행이 동일한 단일 경로 (UX §7.2).</summary>
        private void StartGeneration(ScanScope requestedScope)
        {
            scope = requestedScope;
            state = WindowState.Generating;
            errorMessage = null;
            if (!runQueued)
            {
                runQueued = true;
                // 창이 먼저 그려진 뒤 메인 스레드에서 동기 실행 (TA §6.4)
                EditorApplication.delayCall += RunPipeline;
            }
            Repaint();
        }

        private void RunPipeline()
        {
            runQueued = false;
            if (this == null)
                return;

            // F8/E3: 대형 프로젝트 사전 추정 → 현재 씬 폴백 제안 (사후·조건부로만 노출, UX §6)
            if (scope == ScanScope.Full && ContextGenerationPipeline.IsProjectLarge())
            {
                var currentOnly = EditorUtility.DisplayDialog("EngineContext",
                    "프로젝트가 커서 스캔이 오래 걸릴 수 있습니다. 현재 씬만 스캔할까요?",
                    "현재 씬만 스캔", "전체 스캔");
                if (currentOnly)
                    scope = ScanScope.CurrentScene;
            }

            var cancelled = false;
            PipelineResult result = null;
            try
            {
                result = ContextGenerationPipeline.Run(
                    scope,
                    (step, progress) =>
                    {
                        // F7: 진행 표시 + 취소. "읽기만 한다"는 안심 문구 병기 (UX §7.2)
                        if (EditorUtility.DisplayCancelableProgressBar(
                                "EngineContext - Generate Context",
                                step + " (프로젝트를 읽기만 하며 수정하지 않습니다)",
                                progress))
                            cancelled = true;
                    },
                    () => cancelled);
            }
            catch (Exception ex)
            {
                errorMessage = "생성 중 오류가 발생했습니다: " + ex.Message + " — 현재 씬만으로 다시 시도해 보세요.";
                state = WindowState.Error;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (result != null)
            {
                if (!string.IsNullOrEmpty(result.blockedReason))
                {
                    // 사전 차단(예: 미저장 씬) — 파일 미생성. 저장 후 재실행 또는 현재 씬만 스캔.
                    errorMessage = result.blockedReason;
                    state = WindowState.Error;
                }
                else if (result.cancelled)
                {
                    // E8: 즉시 안전 중단 — Apply 전이므로 어떤 파일도 생성/변경되지 않았음
                    errorMessage = "생성을 취소했습니다. 변경된 파일은 없습니다.";
                    state = WindowState.Error;
                }
                else
                {
                    draft = result.draft;
                    snapshot = result.snapshot;
                    logSummary = result.log.Summary();
                    state = WindowState.Review; // Review 게이트 — 자동 기록 금지
                }
            }
            Repaint();
        }

        private void OnGUI()
        {
            DrawTabs();
            switch (tab)
            {
                case Tab.Generate: DrawGenerateTab(); break;
                case Tab.BuildPrompt: DrawPromptBuilder(); break;
            }
        }

        private void DrawTabs()
        {
            var selected = (Tab)GUILayout.Toolbar((int)tab, new[] { "Generate Context", "Build Prompt" });
            if (selected != tab)
            {
                tab = selected;
                if (tab == Tab.BuildPrompt)
                    RefreshPromptContextStatus();
            }
            EditorGUILayout.Space(4f);
        }

        private void DrawGenerateTab()
        {
            switch (state)
            {
                case WindowState.Generating: DrawGenerating(); break;
                case WindowState.Review: DrawReview(); break;
                case WindowState.Done: DrawDone(); break;
                case WindowState.Error: DrawError(); break;
            }
        }

        // --- State 1: Generating ---
        private void DrawGenerating()
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.HelpBox(
                "컨텍스트를 생성하고 있습니다… 프로젝트를 읽기만 하며 수정하지 않습니다.",
                MessageType.Info);

            // 도메인 리로드 등으로 상태가 초기화된 경우의 복구 경로
            if (!runQueued && draft == null)
            {
                EditorGUILayout.Space(8f);
                if (GUILayout.Button("Generate Context 시작", GUILayout.Height(28f)))
                    StartGeneration(ScanScope.Full);
            }
        }

        // --- State 2: Review ★ 유일한 결정 지점 (IA §3) ---
        private void DrawReview()
        {
            EditorGUILayout.HelpBox(
                "미리보기(읽기전용)입니다. [적용] 전까지 어떤 파일도 기록되지 않습니다.\n" + BuildSummaryLine(),
                MessageType.Info);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            // 반환값을 버려 사실상 읽기전용 — 편집 기능은 넣지 않는다 (UX §7.3: 편집 = 수기 노동의 부활)
            EditorGUILayout.TextArea(draft ?? string.Empty, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("프로젝트 루트에 적용", GUILayout.Height(28f)))
                Apply();
            if (GUILayout.Button("재생성", GUILayout.Height(28f), GUILayout.Width(100f)))
                StartGeneration(scope);
            EditorGUILayout.EndHorizontal();
        }

        // --- State 3: Done ---
        private void DrawDone()
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.HelpBox(
                "CLAUDE.md를 저장했습니다: " + savedPath + "\n" + BuildSummaryLine(),
                MessageType.Info);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("첫 작업 제안", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(BuildFirstTaskSuggestion(), MessageType.None); // Aha를 우연에 맡기지 않는다 (UX §5)

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("재생성", GUILayout.Height(28f)))
                StartGeneration(scope);
        }

        // --- State 0: Error / Fallback ---
        private void DrawError()
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.HelpBox(errorMessage ?? "알 수 없는 오류가 발생했습니다.", MessageType.Warning);

            EditorGUILayout.Space(8f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Retry (현재 씬만)", GUILayout.Height(28f)))
                StartGeneration(ScanScope.CurrentScene);
            if (draft != null && GUILayout.Button("초안으로 돌아가기", GUILayout.Height(28f)))
                state = WindowState.Review; // E6 이후에도 초안은 보존됨
            if (GUILayout.Button("닫기", GUILayout.Height(28f), GUILayout.Width(80f)))
                Close();
            EditorGUILayout.EndHorizontal();
        }

        // --- 행동 ---

        private void Apply()
        {
            var writer = new ContextFileWriter();

            // E7: 기존 파일 존재 시 덮어쓰기 1회 확인 (오류 아님·가드)
            if (writer.RootFileExists() &&
                !EditorUtility.DisplayDialog("EngineContext", "이미 CLAUDE.md가 있습니다. 덮어쓸까요?", "덮어쓰기", "취소"))
                return;

            var result = writer.Write(draft);
            if (result.success)
            {
                savedPath = result.path;
                state = WindowState.Done;
            }
            else
            {
                // E6: 기록 실패 → 초안(draft) 보존, 파일 미변경
                errorMessage = "CLAUDE.md를 저장하지 못했습니다. 파일이 열려 있거나 쓰기 권한이 없는지 확인해 주세요. ("
                               + result.error + ")";
                state = WindowState.Error;
            }
        }

        // --- 표시 헬퍼 ---

        private string BuildSummaryLine()
        {
            if (snapshot == null)
                return "";
            return "담긴 항목 — 씬 " + snapshot.hierarchy.Count
                   + " · 프리팹 " + snapshot.prefabs.Count
                   + " · ScriptableObject " + snapshot.scriptableObjects.Count
                   + " · 패키지 " + snapshot.packages.Count
                   + " · 컴포넌트 타입 " + snapshot.componentTypes.Count
                   + " · " + (logSummary ?? "");
        }

        private string BuildFirstTaskSuggestion()
        {
            var type = PickInterestingComponentType();
            if (type != null)
                return "지금 Claude Code에서 재설명 없이 이렇게 시켜보세요:\n"
                       + "\"" + type + " 컴포넌트가 어떤 오브젝트에 붙어 있고 무슨 역할인지 설명해줘\"";
            return "지금 Claude Code에서 재설명 없이 이렇게 시켜보세요:\n"
                   + "\"이 프로젝트의 구조를 요약하고, 새 기능을 추가할 위치를 제안해줘\"";
        }

        private string PickInterestingComponentType()
        {
            if (snapshot == null)
                return null;

            // 추출 단계에서 분류된 커스텀 스크립트가 가장 정확한 제안 대상
            foreach (var type in snapshot.customComponentTypes)
                return type;

            // 폴백: 알려진 내장 타입 목록으로 근사 판별
            foreach (var type in snapshot.componentTypes)
            {
                if (!BuiltinComponentTypes.Contains(type))
                    return type;
            }
            return null;
        }

        // --- Prompt Builder 탭 (Feature Spec v0.2, Option B: Wrap-Only) ---

        private void DrawPromptBuilder()
        {
            if (!promptContextAvailable)
                EditorGUILayout.HelpBox(promptContextMessage ?? "먼저 Generate Context를 실행해 CLAUDE.md를 만들어 주세요.",
                    MessageType.Warning);
            else
                EditorGUILayout.HelpBox("현재 프로젝트 루트의 CLAUDE.md를 그대로 사용해 프롬프트를 조립합니다 (재추출하지 않음).",
                    MessageType.Info);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("목적 (필수)", EditorStyles.boldLabel);
            promptGoal = EditorGUILayout.TextArea(promptGoal ?? string.Empty, GUILayout.Height(48f));

            EditorGUILayout.LabelField("추가 지시사항 (선택)", EditorStyles.boldLabel);
            promptInstructions = EditorGUILayout.TextArea(promptInstructions ?? string.Empty, GUILayout.Height(40f));

            EditorGUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(promptGoal) || !promptContextAvailable))
            {
                if (GUILayout.Button("Generate Prompt", GUILayout.Height(26f))) // F1: 목적 비면 비활성
                    GeneratePrompt();
            }

            if (!string.IsNullOrEmpty(promptError))
                EditorGUILayout.HelpBox(promptError, MessageType.Warning);

            if (!string.IsNullOrEmpty(promptPreview))
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("미리보기 (읽기전용)", EditorStyles.boldLabel);
                promptScroll = EditorGUILayout.BeginScrollView(promptScroll);
                EditorGUILayout.TextArea(promptPreview, GUILayout.ExpandHeight(true)); // 반환값 버림 = 읽기전용
                EditorGUILayout.EndScrollView();
                if (GUILayout.Button("Copy to Clipboard", GUILayout.Height(26f)))
                    CopyPrompt();
            }
        }

        private void GeneratePrompt()
        {
            promptError = null;

            // 항상 최신 CLAUDE.md를 그대로 읽어 사용 (재추출하지 않음, 단일 진입 지점)
            var writer = new ContextFileWriter();
            if (!writer.TryReadProjectContext(out var content, out var error)) // F5
            {
                promptContextAvailable = false;
                promptContextMessage = error;
                promptError = error;
                promptPreview = null;
                return;
            }

            promptContextAvailable = true;
            promptPreview = PromptComposer.Compose(promptGoal, promptInstructions, content); // F2
        }

        private void CopyPrompt()
        {
            try
            {
                EditorGUIUtility.systemCopyBuffer = promptPreview; // F4: 파일 저장 아님
                ShowNotification(new GUIContent("클립보드에 복사됨"));
            }
            catch (Exception)
            {
                promptError = "클립보드 복사에 실패했습니다. 미리보기에서 직접 선택해 복사해 주세요.";
            }
        }

        private void RefreshPromptContextStatus()
        {
            var writer = new ContextFileWriter();
            promptContextAvailable = writer.RootFileExists();
            promptContextMessage = promptContextAvailable
                ? null
                : "먼저 Generate Context를 실행해 CLAUDE.md를 만들어 주세요.";
        }
    }
}
