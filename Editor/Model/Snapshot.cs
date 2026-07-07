using System;
using System.Collections.Generic;

namespace EngineContext.Editor.Model
{
    // L4 Model(IR) — 벤더 독립 순수 데이터 (TA §1.3, §4).
    // 규칙:
    //  - Unity 타입·로직을 절대 참조하지 않는다 (이식성·결정론·테스트 근거).
    //  - JsonUtility로 직렬화 가능해야 한다 (디버그 덤프용, 필수 아님).
    //  - 모든 목록은 추출 단계에서 경로/이름 기준 안정 정렬된다 (FS §3.2).
    //  - 비결정 요소(생성 시각)는 meta에만 격리한다 (FS §4.5).
    // 스키마: TA §4 기본 + FS §2.4의 components[]{ownerPath,type,keyRefs[]} 병합 (사용자 확정).

    /// <summary>스캔 범위 (TA §4 meta.scope: "full" | "current-scene").</summary>
    public enum ScanScope
    {
        Full,
        CurrentScene
    }

    [Serializable]
    public class Snapshot
    {
        public SnapshotMeta meta = new SnapshotMeta();
        public ProjectInfo project = new ProjectInfo();
        public List<PackageEntry> packages = new List<PackageEntry>();
        public List<AssemblyEntry> assemblies = new List<AssemblyEntry>();
        public FolderTree folders = new FolderTree();
        public List<SceneHierarchy> hierarchy = new List<SceneHierarchy>();

        /// <summary>FS §2.4 병합 필드 — 컴포넌트 인스턴스별 소유 경로 + 핵심 참조(keyRefs).</summary>
        public List<ComponentEntry> components = new List<ComponentEntry>();

        /// <summary>프로젝트에 존재하는 컴포넌트 타입 카탈로그 — AI 헛참조 방지의 핵심 (TA §4).</summary>
        public List<string> componentTypes = new List<string>();

        /// <summary>
        /// schemaVersion 1.4 추가 — Inspector에서 배선된 UnityEvent persistent call
        /// (Button.onClick 등, 코드에 안 나타나는 연결). Wiring Capture 패치 §변경1.
        /// </summary>
        public List<EventBinding> eventBindings = new List<EventBinding>();

        /// <summary>
        /// componentTypes 중 엔진 내장이 아닌 것(프로젝트/서드파티 스크립트) — schemaVersion 1.1 추가.
        /// AI에게 가장 가치 높은 신호이므로 출력에서 우선 표기된다.
        /// </summary>
        public List<string> customComponentTypes = new List<string>();

        /// <summary>
        /// schemaVersion 1.3 추가 — Assets 아래 커스텀 MonoBehaviour의 public API 스키마
        /// (필드/프로퍼티/메서드 시그니처만, 값·구현은 제외). ScriptableObject의 FieldSchema와 동일한
        /// "스키마만" 수준을 MonoBehaviour에도 적용한 것.
        /// </summary>
        public List<MonoBehaviourApiEntry> customApis = new List<MonoBehaviourApiEntry>();

        public List<PrefabEntry> prefabs = new List<PrefabEntry>();
        public List<ScriptableObjectEntry> scriptableObjects = new List<ScriptableObjectEntry>();
        public InferredInfo inferred = new InferredInfo();
        public List<string> notes = new List<string>();
    }

    [Serializable]
    public class SnapshotMeta
    {
        public string schemaVersion = "1.3";
        public string generatedAt = "";   // ISO-8601. 비결정 요소는 여기에만.
        public string toolVersion = "";
        public string scope = "";         // "full" | "current-scene"
        public string unityVersion = "";
        public int skippedRefCount;
        public bool truncated;
    }

    [Serializable]
    public class ProjectInfo
    {
        public string productName = "";
        public string scriptingBackend = "unknown"; // "Mono" | "IL2CPP" | ...
        public string renderPipeline = "unknown";   // "BiRP" | "URP" | "HDRP" | "unknown"
        public string inputSystem = "unknown";      // "Old" | "New" | "Both" | "unknown"
        public List<string> defineSymbols = new List<string>();

        /// <summary>schemaVersion 1.2 추가 — EditorBuildSettings에 등록된 씬 목록(빌드 등록 순서 유지).</summary>
        public List<BuildSceneEntry> buildScenes = new List<BuildSceneEntry>();
    }

    /// <summary>빌드에 등록된 씬 1건. 현재 열린 씬 요약(SceneHierarchy)과는 별개 정보.</summary>
    [Serializable]
    public class BuildSceneEntry
    {
        public string name = "";
        public string path = "";
        public bool enabled = true;
    }

    [Serializable]
    public class PackageEntry
    {
        public string name = "";
        public string version = "";
        public string source = ""; // "registry" | "git" | "local" | "builtin"
    }

    [Serializable]
    public class AssemblyEntry
    {
        public string name = "";
        public string path = "";
        public List<string> references = new List<string>();
    }

    [Serializable]
    public class FolderTree
    {
        public string root = "Assets";
        public List<FolderEntry> tree = new List<FolderEntry>(); // 깊이 상한 적용, 경로 정렬
    }

    [Serializable]
    public class FolderEntry
    {
        public string path = "";
        public int childCount;
    }

    [Serializable]
    public class SceneHierarchy
    {
        public string scene = "";
        public List<HierarchyNode> roots = new List<HierarchyNode>();
    }

    /// <summary>
    /// 재귀 구조. 추출 깊이 상한(5)이 Unity 직렬화 깊이 한계(10)보다 작으므로
    /// JsonUtility 덤프에 안전하다.
    /// </summary>
    [Serializable]
    public class HierarchyNode
    {
        public string name = "";
        public bool active = true;
        public string tag = "";
        public string layer = "";
        public List<string> components = new List<string>();
        public List<HierarchyNode> children = new List<HierarchyNode>();
    }

    [Serializable]
    public class ComponentEntry
    {
        public string ownerPath = ""; // "SceneName/Parent/Child"
        public string type = "";
        // 참조 필드(public·private [SerializeField] 무관, 참조 타입만). 스칼라 제외.
        // 형식: "field -> TargetType (targetPath)". Wiring Capture 패치 §변경2 (keyRefs 재사용·통합).
        public List<string> keyRefs = new List<string>();
    }

    /// <summary>
    /// Inspector에서 배선된 UnityEvent persistent call 1건. 코드에는 나타나지 않는 연결.
    /// 런타임 AddListener는 직렬화 데이터에 없어 캡처 대상이 아니다(한계, 정상 동작).
    /// </summary>
    [Serializable]
    public class EventBinding
    {
        public string sourcePath = "";      // "TitleScene/Canvas/botMode"
        public string sourceComponent = ""; // "Button"
        public string eventName = "";       // "onClick" ('event'는 C# 예약어라 eventName)
        public string targetPath = "";      // "TitleScene/GameManager" | 자산 경로 | "unresolved"
        public string targetComponent = ""; // "ModeManager" (대상이 GameObject/자산이면 빈 문자열)
        public string method = "";          // "Bot"
    }

    [Serializable]
    public class PrefabEntry
    {
        public string name = "";
        public string path = "";
        public List<string> rootComponents = new List<string>();
        public List<string> usedBy = new List<string>();
    }

    [Serializable]
    public class ScriptableObjectEntry
    {
        public string name = "";
        public string path = "";
        public string type = "";
        public List<FieldSchema> fields = new List<FieldSchema>(); // 값은 제외, 스키마만 (FS §3.1)
    }

    [Serializable]
    public class FieldSchema
    {
        public string name = "";
        public string type = "";
    }

    /// <summary>
    /// 커스텀 MonoBehaviour 1개 타입의 public API 스키마 — 해당 타입에 "직접 선언된"(상속 제외)
    /// public 멤버만 담는다. 값·구현 본문은 절대 포함하지 않는다.
    /// </summary>
    [Serializable]
    public class MonoBehaviourApiEntry
    {
        public string type = "";
        public List<FieldSchema> fields = new List<FieldSchema>();
        public List<FieldSchema> properties = new List<FieldSchema>();
        public List<MethodSchema> methods = new List<MethodSchema>();
    }

    [Serializable]
    public class MethodSchema
    {
        public string name = "";
        public string signature = ""; // "(paramType paramName, ...): returnType" — 구현 본문 제외
    }

    [Serializable]
    public class InferredInfo
    {
        public DiInference di = new DiInference();
        public List<PatternInference> architecturePatterns = new List<PatternInference>();
        public List<NamingRule> namingConventions = new List<NamingRule>();
    }

    [Serializable]
    public class DiInference
    {
        public bool detected;
        public string container = null;
        public List<string> evidence = new List<string>();
    }

    [Serializable]
    public class PatternInference
    {
        public string pattern = "";    // "MVVM" | "MVP" | "MVC" | ...
        public string confidence = ""; // "low" | "medium" | "high"
        public List<string> evidence = new List<string>();
    }

    [Serializable]
    public class NamingRule
    {
        public string rule = "";
        public List<string> evidence = new List<string>();
    }
}
