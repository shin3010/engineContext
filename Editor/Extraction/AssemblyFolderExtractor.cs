using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using EngineContext.Editor.Diagnostics;
using EngineContext.Editor.Model;

namespace EngineContext.Editor.Extraction
{
    /// <summary>
    /// L3 — 폴더 트리(깊이 제한) + asmdef·의존관계 수집 (TA §3, FS §3).
    /// 모듈 경계·코드 위치를 알려 AI가 올바른 위치에 코드를 배치하게 한다.
    /// 추가로 Assets 전체의 MonoScript 자산을 스캔해 customComponentTypes와 customApis(public API 스키마)를
    /// 채운다(씬 부착 여부 무관).
    /// </summary>
    internal static class AssemblyFolderExtractor
    {
        private const int MaxFolderDepth = 4; // TA §4: 깊이 상한은 IR 단계에서 적용

        public static void Extract(Snapshot snapshot, ExtractionLog log)
        {
            snapshot.folders.root = "Assets";
            CollectFolders(snapshot, log, "Assets", 1);
            CollectAssemblies(snapshot, log);
            CollectCustomComponentTypes(snapshot, log);
        }

        // public API 스키마 추출 시 타입당 카테고리(필드/프로퍼티/메서드)별 표시 상한 (성능·노이즈 가드)
        private const int MaxApiMembersPerCategory = 30;

        private const BindingFlags PublicDeclaredFlags =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        /// <summary>
        /// customComponentTypes를 Assets 전체의 MonoScript 자산 기준으로 채운다 — 열린 씬/프리팹에
        /// 아직 배치되지 않은 커스텀 스크립트도 "프로젝트에 존재한다"고 AI가 알아야 하기 때문이다
        /// (평가 피드백: 미부착 스크립트 누락이 "존재하지 않는다"는 오판으로 이어짐).
        /// Hierarchy/Prefab Extractor가 씬·프리팹에서 추가로 발견하는 커스텀 타입과는 합집합으로 유지된다.
        /// 새로 발견된 타입에 한해 public API 스키마(customApis)도 함께 추출한다.
        /// </summary>
        private static void CollectCustomComponentTypes(Snapshot snapshot, ExtractionLog log)
        {
            var guids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" });
            var paths = new SortedSet<string>(StringComparer.Ordinal); // 결정론
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }

            var customTypes = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var type in snapshot.customComponentTypes)
                customTypes.Add(type);

            var apiEntries = new List<MonoBehaviourApiEntry>(snapshot.customApis);

            foreach (var path in paths)
            {
                MonoScript script;
                try
                {
                    script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                }
                catch (Exception)
                {
                    log.AddSkip("unloadable script '" + path + "'"); // F10: 무중단
                    continue;
                }
                if (script == null)
                    continue;

                Type scriptClass;
                try
                {
                    scriptClass = script.GetClass();
                }
                catch (Exception)
                {
                    log.AddSkip("unresolvable script class '" + path + "'");
                    continue;
                }

                // 컴포넌트로 부착 불가능한 스크립트(추상/비 MonoBehaviour, 예: 순수 데이터 클래스)는 제외
                if (scriptClass == null || scriptClass.IsAbstract || !typeof(MonoBehaviour).IsAssignableFrom(scriptClass))
                    continue;
                if (TypeClassifier.IsEngineType(scriptClass))
                    continue;

                if (customTypes.Add(scriptClass.Name)) // 새로 발견된 타입일 때만 API 스키마 추출(중복 방지)
                {
                    var api = BuildApiEntry(scriptClass);
                    if (api != null)
                        apiEntries.Add(api);
                }
            }

            snapshot.customComponentTypes = new List<string>(customTypes);

            apiEntries.Sort((a, b) => string.CompareOrdinal(a.type, b.type)); // 결정론 (FS §3.2)
            snapshot.customApis = apiEntries;
        }

        /// <summary>
        /// 타입에 "직접 선언된"(상속 제외) public 필드/프로퍼티/메서드만 스키마로 추출한다.
        /// 값·구현 본문은 절대 포함하지 않는다. public API가 전혀 없으면 null(엔트리 생성 안 함).
        /// </summary>
        private static MonoBehaviourApiEntry BuildApiEntry(Type type)
        {
            var entry = new MonoBehaviourApiEntry { type = type.Name };

            foreach (var field in type.GetFields(PublicDeclaredFlags))
            {
                if (field.IsSpecialName || field.Name.IndexOf('<') >= 0) // 컴파일러 생성 필드 방어
                    continue;
                if (entry.fields.Count >= MaxApiMembersPerCategory)
                    break;
                entry.fields.Add(new FieldSchema { name = field.Name, type = FriendlyTypeName(field.FieldType) });
            }

            foreach (var property in type.GetProperties(PublicDeclaredFlags))
            {
                if (property.GetIndexParameters().Length > 0) // 인덱서 제외
                    continue;
                if (entry.properties.Count >= MaxApiMembersPerCategory)
                    break;
                entry.properties.Add(new FieldSchema { name = property.Name, type = FriendlyTypeName(property.PropertyType) });
            }

            foreach (var method in type.GetMethods(PublicDeclaredFlags))
            {
                // 프로퍼티 접근자(get_/set_)·연산자 오버로드·컴파일러 생성 메서드는 제외
                if (method.IsSpecialName || method.Name.IndexOf('<') >= 0)
                    continue;
                if (entry.methods.Count >= MaxApiMembersPerCategory)
                    break;
                entry.methods.Add(new MethodSchema { name = method.Name, signature = BuildMethodSignature(method) });
            }

            if (entry.fields.Count == 0 && entry.properties.Count == 0 && entry.methods.Count == 0)
                return null; // 공개 API가 전혀 없는 타입(예: 빈 스텁 스크립트)은 스키마를 만들지 않는다

            return entry;
        }

        /// <summary>"(paramType paramName, ...): returnType" — 구현 본문은 포함하지 않는다.</summary>
        private static string BuildMethodSignature(MethodInfo method)
        {
            var parameterParts = new List<string>();
            foreach (var parameter in method.GetParameters())
                parameterParts.Add(FriendlyTypeName(parameter.ParameterType) + " " + parameter.Name);
            return "(" + string.Join(", ", parameterParts) + "): " + FriendlyTypeName(method.ReturnType);
        }

        private static readonly Dictionary<Type, string> TypeAliases = new Dictionary<Type, string>
        {
            { typeof(void), "void" }, { typeof(bool), "bool" }, { typeof(byte), "byte" }, { typeof(sbyte), "sbyte" },
            { typeof(char), "char" }, { typeof(decimal), "decimal" }, { typeof(double), "double" }, { typeof(float), "float" },
            { typeof(int), "int" }, { typeof(uint), "uint" }, { typeof(long), "long" }, { typeof(ulong), "ulong" },
            { typeof(short), "short" }, { typeof(ushort), "ushort" }, { typeof(string), "string" }, { typeof(object), "object" }
        };

        /// <summary>reflection 타입명을 C# 관용 표기로 변환 (int, string, List&lt;T&gt;, T[] 등 최소 지원).</summary>
        private static string FriendlyTypeName(Type type)
        {
            if (TypeAliases.TryGetValue(type, out var alias))
                return alias;
            if (type.IsArray)
                return FriendlyTypeName(type.GetElementType()) + "[]";
            if (type.IsGenericType)
            {
                var backtick = type.Name.IndexOf('`');
                var baseName = backtick >= 0 ? type.Name.Substring(0, backtick) : type.Name;
                var argNames = new List<string>();
                foreach (var arg in type.GetGenericArguments())
                    argNames.Add(FriendlyTypeName(arg));
                return baseName + "<" + string.Join(", ", argNames) + ">";
            }
            return type.Name;
        }

        private static void CollectFolders(Snapshot snapshot, ExtractionLog log, string path, int depth)
        {
            var subFolders = AssetDatabase.GetSubFolders(path);
            Array.Sort(subFolders, StringComparer.Ordinal); // 결정론

            foreach (var sub in subFolders)
            {
                snapshot.folders.tree.Add(new FolderEntry
                {
                    path = sub,
                    childCount = CountEntries(sub)
                });

                if (depth < MaxFolderDepth)
                    CollectFolders(snapshot, log, sub, depth + 1);
                else if (AssetDatabase.GetSubFolders(sub).Length > 0)
                    log.AddWarningOnce("folderDepth", "Folder tree was capped at depth " + MaxFolderDepth + "; deeper folders are omitted.");
            }
        }

        private static int CountEntries(string folderPath)
        {
            try
            {
                var count = 0;
                foreach (var entry in Directory.GetFileSystemEntries(folderPath))
                {
                    if (!entry.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                        count++;
                }
                return count;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static void CollectAssemblies(Snapshot snapshot, ExtractionLog log)
        {
            var guids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset", new[] { "Assets" });
            var paths = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }

            var guidNameCache = new Dictionary<string, string>();
            foreach (var path in paths)
            {
                try
                {
                    var dto = JsonUtility.FromJson<AsmdefDto>(File.ReadAllText(path));
                    var entry = new AssemblyEntry
                    {
                        name = string.IsNullOrEmpty(dto?.name) ? Path.GetFileNameWithoutExtension(path) : dto.name,
                        path = path
                    };
                    if (dto?.references != null)
                    {
                        foreach (var reference in dto.references)
                        {
                            var resolved = ResolveReference(reference, guidNameCache);
                            if (!string.IsNullOrEmpty(resolved))
                                entry.references.Add(resolved);
                        }
                        entry.references.Sort(StringComparer.Ordinal);
                    }
                    snapshot.assemblies.Add(entry);
                }
                catch (Exception)
                {
                    log.AddSkip("unparsable asmdef '" + path + "'"); // F10: 무중단
                }
            }
        }

        /// <summary>"GUID:xxxx" 형식의 asmdef 참조를 어셈블리 이름으로 해석. 실패 시 원문 유지.</summary>
        private static string ResolveReference(string reference, Dictionary<string, string> cache)
        {
            if (string.IsNullOrEmpty(reference))
                return null;
            if (!reference.StartsWith("GUID:", StringComparison.OrdinalIgnoreCase))
                return reference;

            var guid = reference.Substring(5);
            if (cache.TryGetValue(guid, out var cached))
                return cached;

            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    var dto = JsonUtility.FromJson<AsmdefDto>(File.ReadAllText(path));
                    if (!string.IsNullOrEmpty(dto?.name))
                    {
                        cache[guid] = dto.name;
                        return dto.name;
                    }
                }
                catch (Exception)
                {
                    // 해석 실패 → 원문 유지
                }
            }
            cache[guid] = reference;
            return reference;
        }

        [Serializable]
        private class AsmdefDto
        {
            public string name;
            public string[] references;
        }
    }
}
