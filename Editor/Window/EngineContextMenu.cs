using UnityEditor;
using UnityEngine;

namespace EngineContext.Editor.Windows
{
    /// <summary>
    /// L1 — 유일한 진입점 (IA §1: 단일 메뉴 항목, 진입점을 늘리지 않는다).
    /// 클릭 시 창을 열고 즉시 생성 트리거 — 설정 질문 0개 (F1, UX §7.3).
    /// </summary>
    public static class EngineContextMenu
    {
        [MenuItem("Tools/EngineContext/Generate Context")]
        public static void GenerateContext()
        {
            // E1: 미지원 Unity 버전 (package.json의 unity 필드가 설치 단계 1차 방어)
            if (!IsSupportedUnityVersion())
            {
                EditorUtility.DisplayDialog("EngineContext",
                    "이 Unity 버전은 아직 지원되지 않습니다. 지원 범위: 2022 LTS ~ Unity 6.", "확인");
                return;
            }

            // E10: 프로젝트 컴파일 오류 → 생성 보류
            if (EditorUtility.scriptCompilationFailed)
            {
                EditorUtility.DisplayDialog("EngineContext",
                    "프로젝트에 컴파일 오류가 있어 실행할 수 없습니다. 오류를 해결한 뒤 다시 시도해 주세요.", "확인");
                return;
            }

            EngineContextWindow.Open();
        }

        [MenuItem("Tools/EngineContext/Build Prompt")]
        public static void BuildPrompt()
        {
            // 생성을 트리거하지 않고 Prompt Builder 탭으로 진입 (기존 산출물 CLAUDE.md 재사용).
            EngineContextWindow.OpenPromptBuilder();
        }

        private static bool IsSupportedUnityVersion()
        {
            // 예: "2022.3.20f1" → 2022, "6000.0.30f1"(Unity 6) → 6000. 하한만 차단한다.
            var version = Application.unityVersion;
            var dot = version.IndexOf('.');
            return dot > 0
                   && int.TryParse(version.Substring(0, dot), out var major)
                   && major >= 2022;
        }
    }
}
