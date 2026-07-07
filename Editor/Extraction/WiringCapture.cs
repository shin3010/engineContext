using System;
using UnityEditor;
using UnityEngine;
using EngineContext.Editor.Diagnostics;
using EngineContext.Editor.Model;

namespace EngineContext.Editor.Extraction
{
    /// <summary>
    /// L3 공용 헬퍼 — "무엇이 무엇에 연결됐는가"를 캡처 (Wiring Capture 패치).
    /// 씬과 프리팹 양쪽에서 재사용하는 순수 함수 모음(새 계층·인터페이스 아님).
    ///  - CaptureEvents: UnityEvent persistent call (Button.onClick 등) → Snapshot.eventBindings
    ///  - CaptureReferenceFields: 참조 타입 직렬화 필드(public·private [SerializeField] 무관) → ComponentEntry.keyRefs
    /// 스칼라/값 타입은 계속 제외. Read-only. 깨진 참조는 스킵+카운트(E4/F10).
    /// </summary>
    internal static class WiringCapture
    {
        private const int MaxPersistentCallsPerEvent = 10;

        /// <summary>컴포넌트의 모든 UnityEvent 필드에서 persistent call을 읽어 eventBindings에 추가.</summary>
        public static void CaptureEvents(string ownerPath, Component component, Snapshot snapshot, ExtractionLog log)
        {
            try
            {
                using (var serialized = new SerializedObject(component))
                {
                    var property = serialized.GetIterator();
                    var enterChildren = true;
                    while (property.NextVisible(enterChildren))
                    {
                        enterChildren = false; // 최상위 필드만 순회
                        // UnityEvent는 내부적으로 m_PersistentCalls.m_Calls 배열로 직렬화됨
                        var calls = property.FindPropertyRelative("m_PersistentCalls.m_Calls");
                        if (calls == null || !calls.isArray)
                            continue;

                        var eventName = NormalizeEventName(property.name);
                        var count = Math.Min(calls.arraySize, MaxPersistentCallsPerEvent);
                        for (var i = 0; i < count; i++)
                        {
                            var call = calls.GetArrayElementAtIndex(i);
                            var targetProp = call.FindPropertyRelative("m_Target");
                            var methodProp = call.FindPropertyRelative("m_MethodName");
                            if (targetProp == null || methodProp == null)
                                continue;

                            var target = targetProp.objectReferenceValue;
                            var methodName = methodProp.stringValue;
                            if (target == null || string.IsNullOrEmpty(methodName))
                            {
                                if (target == null && !string.IsNullOrEmpty(methodName))
                                    log.AddSkip("event target missing on " + ownerPath + "." + eventName); // E4
                                continue;
                            }

                            ResolveTarget(target, out var targetPath, out var targetComponent);
                            snapshot.eventBindings.Add(new EventBinding
                            {
                                sourcePath = ownerPath,
                                sourceComponent = component.GetType().Name,
                                eventName = eventName,
                                targetPath = targetPath,
                                targetComponent = targetComponent,
                                method = methodName
                            });
                        }
                    }
                }
            }
            catch (Exception)
            {
                log.AddSkip("unreadable events on " + ownerPath); // F10: 무중단
            }
        }

        /// <summary>
        /// 컴포넌트의 참조 타입 직렬화 필드(ObjectReference)만 keyRefs로 캡처.
        /// public/private 구분 없이 직렬화 여부만 본다(reflection 기반 Public API 스캐너와 다른 경로).
        /// 참조가 하나도 없으면 ComponentEntry 자체를 추가하지 않는다(빈 항목 노이즈 방지).
        /// </summary>
        public static void CaptureReferenceFields(string ownerPath, MonoBehaviour behaviour, Snapshot snapshot, ExtractionLog log, int maxRefs)
        {
            var entry = new ComponentEntry { ownerPath = ownerPath, type = behaviour.GetType().Name };
            try
            {
                using (var serialized = new SerializedObject(behaviour))
                {
                    var property = serialized.GetIterator();
                    var enterChildren = true;
                    while (property.NextVisible(enterChildren))
                    {
                        enterChildren = false; // 최상위 필드만 (값은 읽지 않음, 참조 대상만)
                        if (property.propertyType != SerializedPropertyType.ObjectReference || property.name == "m_Script")
                            continue;

                        if (entry.keyRefs.Count >= maxRefs)
                            continue;

                        var value = property.objectReferenceValue;
                        if (value == null)
                        {
                            // 선언은 됐으나 아직 안 꽂힌(또는 깨진) 참조 필드도 스키마로 노출한다.
                            // (private + 미할당이 Public API·Wired 양쪽에서 빠져 보이지 않던 빈틈을 메움.)
                            var declaredType = DeclaredObjectTypeName(property);
                            if (property.objectReferenceInstanceIDValue != 0)
                            {
                                log.AddSkip("broken reference '" + property.name + "' on " + ownerPath); // E4
                                entry.keyRefs.Add(property.name + " → " + declaredType + " (missing)");
                            }
                            else
                            {
                                entry.keyRefs.Add(property.name + " → " + declaredType + " (unassigned)");
                            }
                            continue;
                        }

                        ResolveTarget(value, out var targetPath, out var targetComponent);
                        var typeName = string.IsNullOrEmpty(targetComponent) ? value.GetType().Name : targetComponent;
                        entry.keyRefs.Add(property.name + " → " + typeName + " (" + targetPath + ")");
                    }
                }
            }
            catch (Exception)
            {
                log.AddSkip("unreadable component '" + entry.type + "' on " + ownerPath); // F10: 무중단
            }

            if (entry.keyRefs.Count > 0)
                snapshot.components.Add(entry);
        }

        /// <summary>미할당 ObjectReference 필드의 선언 타입명. property.type의 "PPtr&lt;$Timer&gt;" → "Timer".</summary>
        private static string DeclaredObjectTypeName(SerializedProperty property)
        {
            var type = property.type;
            const string prefix = "PPtr<$";
            if (type.StartsWith(prefix, StringComparison.Ordinal) && type.EndsWith(">", StringComparison.Ordinal))
                return type.Substring(prefix.Length, type.Length - prefix.Length - 1);
            return type;
        }

        /// <summary>참조 대상의 경로/컴포넌트 타입을 해석. 씬 오브젝트면 씬 경로, 자산이면 자산 경로, 못 찾으면 "unresolved".</summary>
        private static void ResolveTarget(UnityEngine.Object target, out string path, out string component)
        {
            component = "";
            if (target is Component comp)
            {
                component = comp.GetType().Name;
                path = ScenePathOf(comp.gameObject);
            }
            else if (target is GameObject go)
            {
                path = ScenePathOf(go);
            }
            else
            {
                // ScriptableObject 등 자산 참조
                var assetPath = AssetDatabase.GetAssetPath(target);
                path = string.IsNullOrEmpty(assetPath) ? "unresolved" : assetPath;
                component = target.GetType().Name;
            }
        }

        /// <summary>GameObject의 계층 경로. 씬에 속하면 "SceneName/Parent/Child", 아니면(프리팹 자산 등) "Parent/Child".</summary>
        private static string ScenePathOf(GameObject go)
        {
            var transform = go.transform;
            var path = go.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            var scene = go.scene;
            return scene.IsValid() && !string.IsNullOrEmpty(scene.name) ? scene.name + "/" + path : path;
        }

        /// <summary>"m_OnClick" → "onClick", 커스텀 "onDeath" → "onDeath".</summary>
        private static string NormalizeEventName(string rawName)
        {
            var name = rawName.StartsWith("m_", StringComparison.Ordinal) ? rawName.Substring(2) : rawName;
            if (name.Length > 0)
                name = char.ToLowerInvariant(name[0]) + name.Substring(1);
            return name;
        }
    }
}
