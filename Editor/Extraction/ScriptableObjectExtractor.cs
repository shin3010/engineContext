using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using EngineContext.Editor.Diagnostics;
using EngineContext.Editor.Model;

namespace EngineContext.Editor.Extraction
{
    /// <summary>
    /// L3 — ScriptableObject 자산 목록 + 필드 스키마 수집 (TA §3, FS §3).
    /// 필드의 이름/타입만 읽고 값은 절대 읽지 않는다 (토큰·프라이버시, FS §3.1).
    /// </summary>
    internal static class ScriptableObjectExtractor
    {
        private const int LoadCapFull = 500;          // 성능 가드
        private const int LoadCapCurrentScene = 200;  // F8 폴백 시 축소
        private const int MaxFieldsPerAsset = 20;

        public static void Extract(Snapshot snapshot, ExtractionLog log, ScanScope scope)
        {
            var guids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" });
            var paths = new SortedSet<string>(StringComparer.Ordinal); // 결정론
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }

            var loadCap = scope == ScanScope.CurrentScene ? LoadCapCurrentScene : LoadCapFull;
            if (paths.Count > loadCap)
                log.AddWarning("ScriptableObject scan was capped at " + loadCap + " of " + paths.Count + " assets.");

            var loaded = 0;
            var builtinOmitted = 0;
            foreach (var path in paths)
            {
                if (loaded >= loadCap)
                    break;
                loaded++;

                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset == null)
                {
                    log.AddSkip("unloadable ScriptableObject '" + path + "'"); // E4: 무중단
                    continue;
                }

                // 출력 큐레이션: 엔진 내장 SO(URP 설정, InputActionAsset 등)는 사용자 데이터 규약(P2)이
                // 아니므로 카탈로그에서 제외하고 개수만 고지한다. 커스텀 SO가 AI에게 실제 가치가 있는 정보.
                if (TypeClassifier.IsEngineType(asset.GetType()))
                {
                    builtinOmitted++;
                    continue;
                }

                var entry = new ScriptableObjectEntry
                {
                    name = asset.name,
                    path = path,
                    type = asset.GetType().Name
                };
                CollectFieldSchema(asset, entry, log);
                snapshot.scriptableObjects.Add(entry);
            }

            if (builtinOmitted > 0)
                log.AddWarning(builtinOmitted + " built-in engine ScriptableObject asset(s) (render pipeline settings, input actions, etc.) were omitted from the catalog.");
        }

        private static void CollectFieldSchema(ScriptableObject asset, ScriptableObjectEntry entry, ExtractionLog log)
        {
            try
            {
                using (var serialized = new SerializedObject(asset))
                {
                    var property = serialized.GetIterator();
                    var enterChildren = true;
                    while (property.NextVisible(enterChildren))
                    {
                        enterChildren = false; // 최상위 필드만 (스키마: 이름/타입, 값 제외)
                        if (property.name == "m_Script")
                            continue;
                        if (entry.fields.Count >= MaxFieldsPerAsset)
                            break;
                        entry.fields.Add(new FieldSchema { name = property.name, type = property.type });
                    }
                }
            }
            catch (Exception)
            {
                log.AddSkip("unreadable ScriptableObject fields '" + entry.path + "'"); // F10: 무중단
            }
        }
    }
}
