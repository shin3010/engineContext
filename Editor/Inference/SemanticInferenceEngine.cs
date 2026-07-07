using System;
using System.Collections.Generic;
using EngineContext.Editor.Model;

namespace EngineContext.Editor.Inference
{
    /// <summary>
    /// 횡단 Inference — IR 위에서 규칙만으로 DI/아키텍처 패턴/네이밍 추론 (TA §3, FS §4.4).
    /// 원칙: 단정 금지·근거 필수. 근거가 없으면 detected:false / 빈 배열로 둔다 (허위 단정 금지).
    /// </summary>
    public static class SemanticInferenceEngine
    {
        public static void Infer(Snapshot snapshot)
        {
            InferDi(snapshot);
            InferArchitecturePatterns(snapshot);
            InferNamingConventions(snapshot);
        }

        // --- DI 추론: 패키지 흔적 + LifetimeScope/Installer 계열 타입 흔적 ---
        private static void InferDi(Snapshot snapshot)
        {
            var di = snapshot.inferred.di;

            foreach (var package in snapshot.packages)
            {
                var lower = package.name.ToLowerInvariant();
                if (lower.Contains("vcontainer"))
                {
                    di.container = "VContainer";
                    di.evidence.Add("package `" + package.name + "` " + package.version);
                }
                else if (lower.Contains("zenject") || lower.Contains("extenject"))
                {
                    di.container = "Zenject";
                    di.evidence.Add("package `" + package.name + "` " + package.version);
                }
            }

            // 커스텀 타입만 검사 — 엔진 내장 타입은 DI 흔적이 될 수 없다
            foreach (var type in snapshot.customComponentTypes)
            {
                if (type.EndsWith("LifetimeScope", StringComparison.Ordinal))
                {
                    if (di.container == null) di.container = "VContainer";
                    di.evidence.Add("type `" + type + "`");
                }
                else if (type.EndsWith("Installer", StringComparison.Ordinal))
                {
                    if (di.container == null) di.container = "Zenject";
                    di.evidence.Add("type `" + type + "`");
                }
            }

            di.detected = di.container != null && di.evidence.Count > 0;
            if (!di.detected)
            {
                di.container = null;
                di.evidence.Clear();
            }
        }

        // --- 패턴 추론: 타입명 접미사 분포로 MVVM/MVP/MVC 경향 추정 ---
        private static void InferArchitecturePatterns(Snapshot snapshot)
        {
            var types = CollectTypeUniverse(snapshot);
            var viewModels = CountSuffix(types, "ViewModel");
            var presenters = CountSuffix(types, "Presenter");
            var views = CountSuffix(types, "View");
            var controllers = CountSuffix(types, "Controller");

            if (viewModels >= 2)
            {
                snapshot.inferred.architecturePatterns.Add(new PatternInference
                {
                    pattern = "MVVM",
                    confidence = ToConfidence(viewModels),
                    evidence = { viewModels + " types ending with 'ViewModel'" + (views > 0 ? ", " + views + " with 'View'" : "") }
                });
            }

            if (presenters >= 2)
            {
                snapshot.inferred.architecturePatterns.Add(new PatternInference
                {
                    pattern = "MVP",
                    confidence = ToConfidence(presenters),
                    evidence = { presenters + " types ending with 'Presenter'" + (views > 0 ? ", " + views + " with 'View'" : "") }
                });
            }

            if (controllers >= 3 && viewModels < 2 && presenters < 2)
            {
                snapshot.inferred.architecturePatterns.Add(new PatternInference
                {
                    pattern = "MVC",
                    confidence = ToConfidence(controllers),
                    evidence = { controllers + " types ending with 'Controller'" }
                });
            }
        }

        // --- 네이밍 규칙 추론: SO 접미사 + Runtime/Editor 폴더 분리 감지 ---
        private static void InferNamingConventions(Snapshot snapshot)
        {
            var soTypes = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var so in snapshot.scriptableObjects)
                soTypes.Add(so.type);

            var suffixes = new[] { "Data", "Config", "Settings", "Definition", "Event" };
            foreach (var suffix in suffixes)
            {
                var matches = new List<string>();
                foreach (var type in soTypes)
                {
                    if (type.EndsWith(suffix, StringComparison.Ordinal))
                        matches.Add(type);
                }
                if (matches.Count >= 3)
                {
                    var samples = string.Join(", ", matches.GetRange(0, Math.Min(3, matches.Count)));
                    snapshot.inferred.namingConventions.Add(new NamingRule
                    {
                        rule = "ScriptableObject types commonly end with '" + suffix + "'",
                        evidence = { matches.Count + "/" + soTypes.Count + " types, e.g. " + samples }
                    });
                }
            }

            var hasRuntime = false;
            var hasEditor = false;
            string runtimeSample = null;
            string editorSample = null;
            foreach (var folder in snapshot.folders.tree)
            {
                if (folder.path.EndsWith("/Runtime", StringComparison.Ordinal)) { hasRuntime = true; runtimeSample = runtimeSample ?? folder.path; }
                if (folder.path.EndsWith("/Editor", StringComparison.Ordinal)) { hasEditor = true; editorSample = editorSample ?? folder.path; }
            }
            if (hasRuntime && hasEditor)
            {
                snapshot.inferred.namingConventions.Add(new NamingRule
                {
                    rule = "Code is organized into Runtime/Editor folder split",
                    evidence = { "`" + runtimeSample + "`, `" + editorSample + "`" }
                });
            }
        }

        private static SortedSet<string> CollectTypeUniverse(Snapshot snapshot)
        {
            // 커스텀 타입만 대상 — 내장 타입(CharacterController 등)이 접미사 패턴
            // 카운트를 오염시켜 MVC 등을 오탐하는 것을 방지한다.
            var types = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var type in snapshot.customComponentTypes)
                types.Add(type);
            foreach (var so in snapshot.scriptableObjects)
                types.Add(so.type); // SO 카탈로그는 추출 단계에서 이미 커스텀만 수집됨
            return types;
        }

        private static int CountSuffix(SortedSet<string> types, string suffix)
        {
            var count = 0;
            foreach (var type in types)
            {
                if (type.EndsWith(suffix, StringComparison.Ordinal))
                    count++;
            }
            return count;
        }

        private static string ToConfidence(int count)
        {
            return count >= 8 ? "high" : count >= 4 ? "medium" : "low";
        }
    }
}
