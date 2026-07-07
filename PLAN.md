# EngineContext v0.1 — 구현 계획 (Scaffold Plan)

> 근거 문서: PRD / Validation / Functional Spec v0.1 / Technical Architecture v0.1 / UX Spec v0.1 / IA Spec v0.1
> 확정된 결정 (사용자 승인, 2026-07-04):
> - 프로젝트 형태: **UPM 패키지 단독** (현재 폴더 = 패키지 루트, git URL/OpenUPM 배포 전제)
> - IR 스키마: **TA §4 기본 + FS의 `components[]{ownerPath,type,keyRefs[]}` 추가 (병합)**
> - Window 렌더링: **IMGUI**
> - 패키지명: **`com.enginecontext`**
> - 확장 대비 (2026-07-04 추가 지시): Snapshot(IR)과 포매터 사이 **중간 Context Model 계층은 v0.2+ 예정** —
>   MCP Resource / .cursorrules / AGENTS.md 등 다형식 출력 대비. v0.1에서는 구현하지 않고
>   `Editor/Formatting/ClaudeMarkdownFormatter.cs`에 TODO 주석만 남김.

---

## 1. 프로젝트 구조

| 항목 | 내용 |
| :--- | :--- |
| 형태 | UPM 패키지 (repo 루트 = 패키지 루트). Unity 프로젝트의 `Packages/manifest.json`에 git URL 또는 로컬 경로로 추가해 사용 |
| 패키지명 | `com.enginecontext` / displayName `EngineContext` / version `0.1.0` |
| 최소 Unity | `2022.3` (2022 LTS ~ Unity 6 지원, FS E1) |
| 어셈블리 | `EngineContext.Editor` 1개 — **Editor 플랫폼 전용 asmdef**. 런타임 코드 0 |
| 테스트 | `EngineContext.Editor.Tests` (EditMode, TA에서 "선택"이므로 골격만) |
| 외부 의존성 | 없음 (서버/LLM/네트워크/서드파티 패키지 0) |
| 실행 모델 | 메인 스레드 동기 실행 + 진행바(취소 가능). 백그라운드 스레드 금지 |

### 계층 (5계층 + 횡단 2, TA §1)

| 계층 | 폴더 | 의존 방향 |
| :--- | :--- | :--- |
| L1 UI | `Editor/Window/` | → L2 Pipeline, L5 Writer |
| L2 Pipeline | `Editor/Pipeline/` | → L3, Inference, L5 Formatter |
| L3 Extraction | `Editor/Extraction/` | → L4 Model, Diagnostics |
| L4 Model (IR) | `Editor/Model/` | → (없음, 순수 데이터) |
| L5 Formatting/IO | `Editor/Formatting/`, `Editor/IO/` | → L4 Model |
| 횡단 Inference | `Editor/Inference/` | → L4 Model |
| 횡단 Diagnostics | `Editor/Diagnostics/` | → (없음) |

불변 계약: 단방향 의존·순환 금지 / Model에 Unity 타입 금지 / Review 게이트(Apply 전 파일 기록 금지) / 인터페이스·팩토리·DI 컨테이너 도입 금지(concrete 클래스 + 명시적 호출).

---

## 2. 폴더 구조

```
engineContext/                              (repo 루트 = UPM 패키지 루트)
├── package.json                            # name: com.enginecontext, unity: 2022.3
├── README.md
├── CHANGELOG.md
├── PLAN.md                                 # (본 문서)
├── Editor/
│   ├── EngineContext.Editor.asmdef         # includePlatforms: ["Editor"]
│   ├── Window/
│   │   ├── EngineContextMenu.cs            # Tools ▸ EngineContext ▸ Generate Context
│   │   └── EngineContextWindow.cs          # 단일 창 + 상태 머신 (IMGUI)
│   ├── Pipeline/
│   │   └── ContextGenerationPipeline.cs
│   ├── Extraction/
│   │   ├── ProjectSettingsExtractor.cs
│   │   ├── PackageExtractor.cs
│   │   ├── AssemblyFolderExtractor.cs
│   │   ├── HierarchyExtractor.cs           # componentTypes + components(keyRefs)도 담당
│   │   ├── PrefabExtractor.cs
│   │   └── ScriptableObjectExtractor.cs
│   ├── Model/
│   │   └── Snapshot.cs                     # IR 루트 + 하위 데이터 클래스 (단일 파일, 비대해지면 분할)
│   ├── Inference/
│   │   └── SemanticInferenceEngine.cs
│   ├── Formatting/
│   │   ├── ClaudeMarkdownFormatter.cs      # 마스터: 10개 섹션 고정 순서 결합
│   │   ├── FormatterLimits.cs              # 상한 상수 + 절단 헬퍼
│   │   └── Sections/
│   │       ├── HeaderSection.cs
│   │       ├── ProjectOverviewSection.cs
│   │       ├── TechStackSection.cs
│   │       ├── ArchitectureConventionsSection.cs
│   │       ├── FolderAssemblyMapSection.cs
│   │       ├── HierarchySummarySection.cs
│   │       ├── PrefabInventorySection.cs
│   │       ├── ScriptableObjectCatalogSection.cs
│   │       ├── KeyComponentsSection.cs
│   │       └── NotesSection.cs
│   ├── IO/
│   │   └── ContextFileWriter.cs
│   └── Diagnostics/
│       └── ExtractionLog.cs
└── Tests/
    └── Editor/
        ├── EngineContext.Editor.Tests.asmdef
        ├── SnapshotSerializationTests.cs   # IR 직렬화/결정론
        └── FormatterDeterminismTests.cs    # 동일 Snapshot → 바이트 동일 출력
```

폴더 = 계층 1:1. 새 폴더를 만들기 전 "기존 계층의 일부인가"를 먼저 묻는다 (TA §2 규칙).

---

## 3. 클래스 구조

### L1 — UI (IMGUI)

**`EngineContextMenu`** (static)
- `[MenuItem("Tools/EngineContext/Generate Context")] static void GenerateContext()` — 창 오픈 + 즉시 생성 트리거 (설정 질문 0개, F1)
- 진입 전 가드: 미지원 Unity 버전(E1), 컴파일 에러(E10, `EditorUtility.scriptCompilationFailed`) 확인 → 한 문장 안내

**`EngineContextWindow : EditorWindow`**
- 상태: `enum WindowState { Generating, Review, Done, Error }` (IA의 4상태와 1:1)
- 보유 데이터: `WindowState state` / `ScanScope scope`(full·currentScene) / 진행값·취소 플래그 / `string draft` / `Snapshot snapshot` / `ExtractionLog` 요약 / 오류 정보
- 행동: `StartGeneration()`(Pipeline 호출, 진행·취소 콜백 전달) / `OnApply()`(Writer 호출, 덮어쓰기 확인 1회 E7 → 성공 시 Done, 실패 시 Error·초안 보존 E6) / `OnRegenerate()`(동일 Pipeline 재진입, F11) / `OnCancel()`(파일 미생성 보장, E8)
- 렌더: `OnGUI()`에서 상태별 분기. Review는 읽기전용 스크롤 TextArea + [Apply]/[Regenerate] 2버튼만. Done은 저장 경로 + "첫 작업 제안" 문구(실존 컴포넌트명 삽입, UX Aha 유도)
- 금지: 자동 파일 기록, 편집 기능, 추가 진입점

### L2 — Pipeline

**`ContextGenerationPipeline`**
- `PipelineResult Run(ScanScope scope, Action<string,float> onProgress, Func<bool> isCancelled)`
- 고정 순서: ProjectSettings → Package → AssemblyFolder → Hierarchy → Prefab → ScriptableObject → Inference → Formatter
- 실행 전 규모 사전 추정 → 임계 초과 시 폴백 신호 반환 (F8/E3)
- **`PipelineResult`** (데이터 홀더): `string draft` / `Snapshot snapshot` / `ExtractionLog log` / `bool fallbackSuggested` / `bool cancelled`
- `enum ScanScope { Full, CurrentScene }`

### L3 — Extraction (전부 static class, 공통 형태 `void Extract(Snapshot snapshot, ExtractionLog log)`)

| 클래스 | 읽는 곳 | 채우는 곳 |
| :--- | :--- | :--- |
| `ProjectSettingsExtractor` | `PlayerSettings`·파이프라인 설정·Input·Define Symbols | `snapshot.project` |
| `PackageExtractor` | `UnityEditor.PackageManager` (실패 시 `manifest.json`, E11 degrade) | `snapshot.packages` |
| `AssemblyFolderExtractor` | `AssetDatabase` 폴더·asmdef | `snapshot.folders`, `snapshot.assemblies` |
| `HierarchyExtractor` | 열린 씬 루트 순회, `GetComponents`, `SerializedObject`(참조 필드명만) | `snapshot.hierarchy`, `snapshot.componentTypes`, `snapshot.components`(keyRefs) |
| `PrefabExtractor` | `FindAssets("t:Prefab")`, `PrefabUtility` | `snapshot.prefabs` |
| `ScriptableObjectExtractor` | `FindAssets("t:ScriptableObject")`, `SerializedObject` 필드 순회(값 제외) | `snapshot.scriptableObjects` |

- 공통 규칙: read-only / 깨진·null·순환 참조는 스킵+카운트, 무중단 (F10, E4·E5) / 결과는 경로·이름 기준 안정 정렬 (결정론, FS §3.2)
- keyRefs 수집: 컴포넌트의 SerializedProperty 중 ObjectReference 타입 필드의 (필드명 → 참조 대상 이름)만, 컴포넌트당 상한 적용. 값·Transform 좌표·private 값·바이너리는 읽지 않음 (FS §3.1)

**`ExtractionLog`** (Diagnostics)
- `int SkippedRefCount` / `List<string> Warnings` / 섹션별 타이밍
- `AddSkip(context)` / `AddWarning(msg)` / `string Summary()` → Notes 섹션·UI에 공급

### L4 — Model (IR, 순수 데이터 · Unity 타입 0 · `[Serializable]`)

**`Snapshot`** — 병합 스키마 (TA §4 + FS components[])
```
Snapshot
 ├─ SnapshotMeta meta            { schemaVersion="1.0", generatedAt(ISO-8601, 비결정 요소 격리),
 │                                 toolVersion, scope, unityVersion, skippedRefCount, truncated }
 ├─ ProjectInfo project          { productName, scriptingBackend, renderPipeline, inputSystem, defineSymbols[] }
 ├─ List<PackageInfo> packages   { name, version, source }
 ├─ List<AssemblyInfo> assemblies{ name, path, references[] }
 ├─ FolderTree folders           { root="Assets", tree: List<FolderEntry>{ path, childCount } }
 ├─ List<SceneHierarchy> hierarchy { scene, roots: List<HierarchyNode> }
 │      HierarchyNode            { name, active, tag, layer, components[](타입명), children[] (깊이 상한) }
 ├─ List<ComponentEntry> components  ★병합 추가 { ownerPath, type, keyRefs[] }
 ├─ List<string> componentTypes  (존재 타입 카탈로그 — 헛참조 방지 핵심)
 ├─ List<PrefabInfo> prefabs     { name, path, rootComponents[], usedBy[] }
 ├─ List<ScriptableObjectInfo> scriptableObjects { name, path, type, fields: List<FieldSchema>{ name, type } }
 ├─ InferredInfo inferred        { DiInference di{ detected, container, evidence[] },
 │                                 List<PatternInference> architecturePatterns{ pattern, confidence, evidence[] },
 │                                 List<NamingRule> namingConventions{ rule, evidence[] } }
 └─ List<string> notes
```
- 직렬화: 디버그 덤프용 `JsonUtility` (필수 아님, `Temp/`에만). 트리 깊이 상한이 Unity 직렬화 깊이 한계(10)보다 작아 안전
- 규칙: 근거 없는 추론은 `detected:false`/빈 배열 (허위 단정 금지)

### 횡단 — Inference

**`SemanticInferenceEngine`** (static)
- `void Infer(Snapshot snapshot)` → `snapshot.inferred` 채움. 규칙만 (FS §4.4):
  - DI: VContainer/Zenject 패키지 + `LifetimeScope`/`Installer` 계열 타입 흔적
  - 패턴: `*ViewModel`/`*Presenter`/`*View`/`*Controller` 접미사 분포 → MVVM/MVP/MVC 경향 + confidence
  - 네이밍: SO 접미사, `Scripts/Runtime`·`Scripts/Editor` 폴더 규약
- 모든 결과에 evidence 필수. 근거 없으면 미검출

### L5 — Formatting / IO

**`ClaudeMarkdownFormatter`** (static)
- `string Format(Snapshot snapshot)` — 10개 섹션 고정 순서(Header → Project Overview → Tech Stack → Architecture Conventions → Folder & Assembly Map → Scene/Hierarchy Summary → Prefab Inventory → SO Catalog → Key Components → Notes & Caveats) 호출·결합. 전체 문자 예산 초과 시 `meta.truncated` 표기 (E9)
- 동일 Snapshot → 바이트 동일 출력 (idempotency). 시간은 Header meta에만

**`Sections/*Section`** (각 static, `string Render(Snapshot, FormatterLimits)`)
- 데이터 없으면 블록 생략 또는 "none detected" (빈 헤더 금지)
- 식별자는 인라인 코드 표기, 목록 절단 시 `(+N more)`

**`FormatterLimits`** (static 상수 + 헬퍼)
- 섹션별 최대 항목 수 / 트리 최대 깊이 / 전체 문자 예산 / `Truncate<T>(list, n)` `(+N more)` 헬퍼
- 제안 기본값(문서 미규정, 튜닝 가능 상수로 격리): hierarchy 깊이 5·노드당 자식 20 / 폴더 깊이 4 / prefab·SO 각 100 / componentTypes 200 / keyRefs 컴포넌트당 5 / 전체 예산 40,000자 / 대형 프로젝트 임계(F8): 총 GameObject 2만 또는 Prefab 2천 초과

**`ContextFileWriter`** (IO)
- `bool RootFileExists()` / `WriteResult Write(string draft)` — 프로젝트 루트(`Application.dataPath`의 부모) 해석, `CLAUDE.md` 기록. 실패(권한·잠금) 시 초안 보존 + 실패 사유 반환 (E6)

---

## 4. 구현 계획 (TA §7 순서 = FS §6 Week 1→3)

> 원칙: ① IR 먼저 고정 ② Extractor 안정화 전 Formatter 금지 ③ 추론은 마지막 ④ 단계별 독립 검증

| # | 단계 | 내용 | 검증 (DoD) |
| :--- | :--- | :--- | :--- |
| 1 | 스캐폴드 & 진입점 | `package.json`, README, CHANGELOG, Editor 전용 asmdef, `EngineContextMenu` + 빈 `EngineContextWindow` | Unity에 패키지 추가 시 컴파일 통과, 메뉴 노출·창 오픈 |
| 2 | Model(IR) 고정 | `Snapshot.cs` — §3의 병합 스키마 전체 (순수 데이터) | 빈 Snapshot JSON 직렬화·역직렬화 왕복 성공 (EditMode 테스트) |
| 3a | Extractor: Settings+Package | 쉽고 가치 큼. E11 degrade 포함 | `snapshot.project`·`packages` 채워짐 |
| 3b | Extractor: Assembly/Folder | 깊이 제한 폴더 트리 + asmdef 의존관계 | `folders`·`assemblies` 채워짐, 안정 정렬 |
| 3c | Extractor: Hierarchy | GO/Component + componentTypes + components(keyRefs). 씬 없음 시 부분 생성 (E2) | 열린 씬 트리 수집, 깨진 참조 무중단 |
| 3d | Extractor: Prefab | 인벤토리 + usedBy. 순환·과중첩 깊이 상한 (E5) | `prefabs` 채워짐 |
| 3e | Extractor: ScriptableObject | 필드 스키마만 (값 제외) | `scriptableObjects` 채워짐 |
| 4 | Pipeline 결합 + 진행/취소 | 고정 순서 호출, 진행 콜백, 취소 전파 (F7), 규모 사전 추정 (F8 기초) | 1회 실행 → 완전한 Snapshot. JSON 덤프 2회 비교로 결정론 확인 |
| 5 | Formatter | `FormatterLimits` → 10개 섹션 렌더러 → 마스터 결합 | 동일 Snapshot → 바이트 동일 CLAUDE.md (테스트) |
| 6 | Review 연결 | 초안을 Window Review 상태에 읽기전용 표시 + 담긴 항목·스킵 수 요약 | Generating→Review 자동 전이 |
| 7 | Writer + Apply → Done | 루트 해석·덮어쓰기 확인 (E7)·기록 (E6). Done에 경로+첫 작업 제안 | **1클릭 → 프로젝트 루트 CLAUDE.md 생성** (엔드투엔드) |
| 8 | Semantic Inference | DI/패턴/네이밍 규칙 추론 → Architecture Conventions 섹션 | 근거 있는 추정만 표기, 없으면 "미검출" |
| 9 | Error Handling + Fallback + Regenerate | E1~E11 전 케이스 한 문장 메시지, F8 폴백(current-scene 재실행), F11 재생성 ≤3클릭 | 오류 케이스 체크리스트 통과, Cancel 시 파일 무변경 |
| 10 | 하드닝 | 결정론 재확인, 문자 예산, 깨진 참조 스트레스, 대형 프로젝트 성능 가드, Notes 최종화 | v0.1 릴리스 후보 |

### 문서가 비워둔 값 → 본 계획의 제안 기본값 (구현 시 상수로 격리, 변경 용이)
- FormatterLimits 수치·F8 임계값: §3의 제안값 (문서에 수치 없음)
- E1 버전 체크: `package.json unity: 2022.3` (설치 차단) + 메뉴 진입 시 버전 가드
- IR 디버그 덤프: 기본 off, 상수 토글로 `Temp/EngineContext/snapshot.json` (TA §4 "필수 아님")
- Tests: 결정론·직렬화 2종만 골격 포함 (TA에서 "선택")

### 명시적 비범위 (v0.1에서 절대 하지 않음)
Context Store / 수기 태깅 UI / 목적별 선별(Compiler) / 다형식 출력 / 스크린샷 폴백 / Unreal / 실시간 동기화 / 계정·서버·LLM / 런타임 코드 / 자동 덮어쓰기
