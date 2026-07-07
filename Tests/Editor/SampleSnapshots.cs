using EngineContext.Editor.Model;

namespace EngineContext.Editor.Tests
{
    /// <summary>테스트용 샘플 Snapshot 빌더 — 매 호출마다 동일한 내용을 생성한다 (결정론 검증 전제).</summary>
    internal static class SampleSnapshots
    {
        public static Snapshot Create()
        {
            var snapshot = new Snapshot();

            snapshot.meta.schemaVersion = "1.4";
            snapshot.meta.generatedAt = "2026-01-01T00:00:00Z"; // 고정 시각 (비결정 요소 격리 확인)
            snapshot.meta.toolVersion = "0.1.0";
            snapshot.meta.scope = "full";
            snapshot.meta.unityVersion = "2022.3.20f1";

            snapshot.project.productName = "SampleGame";
            snapshot.project.scriptingBackend = "Mono";
            snapshot.project.renderPipeline = "URP";
            snapshot.project.inputSystem = "New";
            snapshot.project.defineSymbols.Add("SAMPLE_DEFINE");
            snapshot.project.buildScenes.Add(new BuildSceneEntry { name = "TitleScene", path = "Assets/Scenes/TitleScene.unity", enabled = true });
            snapshot.project.buildScenes.Add(new BuildSceneEntry { name = "GameScene", path = "Assets/Scenes/GameScene.unity", enabled = true });
            snapshot.project.buildScenes.Add(new BuildSceneEntry { name = "SampleScene", path = "Assets/Scenes/SampleScene.unity", enabled = false });

            snapshot.packages.Add(new PackageEntry { name = "com.unity.modules.audio", version = "1.0.0", source = "builtin" }); // 출력에서 생략되어야 함
            snapshot.packages.Add(new PackageEntry { name = "com.unity.render-pipelines.universal", version = "14.0.9", source = "registry" });
            snapshot.packages.Add(new PackageEntry { name = "jp.hadashikick.vcontainer", version = "1.14.0", source = "git" });

            snapshot.assemblies.Add(new AssemblyEntry
            {
                name = "Game.Runtime",
                path = "Assets/Scripts/Runtime/Game.Runtime.asmdef",
                references = { "VContainer" }
            });

            snapshot.folders.root = "Assets";
            snapshot.folders.tree.Add(new FolderEntry { path = "Assets/Scripts", childCount = 2 });
            snapshot.folders.tree.Add(new FolderEntry { path = "Assets/Scripts/Runtime", childCount = 5 });

            snapshot.hierarchy.Add(new SceneHierarchy
            {
                scene = "Main",
                roots =
                {
                    new HierarchyNode
                    {
                        name = "Player",
                        active = true,
                        tag = "Player",
                        layer = "Default",
                        components = { "Transform", "PlayerController" },
                        children =
                        {
                            new HierarchyNode
                            {
                                name = "Model",
                                active = true,
                                tag = "Untagged",
                                layer = "Default",
                                components = { "Transform", "MeshRenderer" }
                            }
                        }
                    }
                }
            });

            snapshot.components.Add(new ComponentEntry
            {
                ownerPath = "Main/Player",
                type = "PlayerController",
                keyRefs =
                {
                    "targetCamera → MainCamera (Main/Main Camera)", // 배선된 참조
                    "spawnPoint → Transform (unassigned)"           // 선언됐지만 미할당 (Option A)
                }
            });

            // schemaVersion 1.4: Inspector 배선 UnityEvent (Wiring Capture 패치)
            snapshot.eventBindings.Add(new EventBinding
            {
                sourcePath = "TitleScene/Canvas/botMode",
                sourceComponent = "Button",
                eventName = "onClick",
                targetPath = "TitleScene/GameManager",
                targetComponent = "ModeManager",
                method = "Bot"
            });

            snapshot.componentTypes.Add("MeshRenderer");
            snapshot.componentTypes.Add("PlayerController");
            snapshot.componentTypes.Add("Transform");
            snapshot.customComponentTypes.Add("PlayerController"); // schemaVersion 1.1: 커스텀 분류
            // Assets 스캔 기준이므로 씬에 아직 배치되지 않은 스크립트도 포함될 수 있음을 검증
            snapshot.customComponentTypes.Add("UnusedHelper");

            // schemaVersion 1.3: 커스텀 MonoBehaviour public API 스키마 (정렬된 순서로 미리 구성 — IR은 이미 정렬됨)
            snapshot.customApis.Add(new MonoBehaviourApiEntry
            {
                type = "EnemyAI",
                methods = { new MethodSchema { name = "Attack", signature = "(): void" } }
            });
            snapshot.customApis.Add(new MonoBehaviourApiEntry
            {
                type = "PlayerController",
                fields = { new FieldSchema { name = "maxSpeed", type = "float" } },
                properties = { new FieldSchema { name = "IsGrounded", type = "bool" } },
                methods =
                {
                    new MethodSchema { name = "Jump", signature = "(): void" },
                    new MethodSchema { name = "Move", signature = "(float direction): void" }
                }
            });

            snapshot.prefabs.Add(new PrefabEntry
            {
                name = "Enemy",
                path = "Assets/Prefabs/Enemy.prefab",
                rootComponents = { "Transform", "EnemyAI" },
                usedBy = { "Main/Enemy_01" }
            });

            snapshot.scriptableObjects.Add(new ScriptableObjectEntry
            {
                name = "GameConfig",
                path = "Assets/Data/GameConfig.asset",
                type = "GameConfigData",
                fields = { new FieldSchema { name = "maxHp", type = "int" } }
            });

            snapshot.inferred.di.detected = true;
            snapshot.inferred.di.container = "VContainer";
            snapshot.inferred.di.evidence.Add("package `jp.hadashikick.vcontainer` 1.14.0");

            snapshot.notes.Add("1 broken or missing reference(s) were skipped during extraction.");
            snapshot.meta.skippedRefCount = 1;

            return snapshot;
        }
    }
}
