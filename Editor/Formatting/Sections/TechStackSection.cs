using System;
using System.Collections.Generic;
using System.Text;
using EngineContext.Editor.Model;

namespace EngineContext.Editor.Formatting.Sections
{
    /// <summary>
    /// 섹션 3 — Tech Stack / Packages: 주요 프레임워크 우선 (FS §4.1).
    /// 출력 큐레이션: 모든 Unity 프로젝트에 동일하게 존재하는 엔진 모듈·에디터 인프라 패키지는
    /// 정보가치가 없으므로 제외하고 개수만 고지한다. IR에는 전체가 보존된다 (사실 기록).
    /// </summary>
    internal static class TechStackSection
    {
        // 의미 추론·코딩에 영향이 큰 프레임워크 키워드 (우선 표기)
        private static readonly string[] NotableKeywords =
        {
            "vcontainer", "zenject", "extenject", "unitask", "addressables",
            "cinemachine", "inputsystem", "render-pipelines", "netcode", "localization"
        };

        // AI에게 가치가 없는 패키지 (엔진 모듈 / 에디터 인프라 / 도구 자신) — 출력에서 생략
        private static readonly string[] OmittedPrefixes =
        {
            "com.unity.modules.",                    // 엔진 모듈 — 모든 프로젝트에 존재
            "com.unity.collab-proxy",                // 에디터 인프라 ↓
            "com.unity.ide.",
            "com.unity.test-framework",
            "com.unity.ext.nunit",
            "com.unity.multiplayer.center",
            "com.unity.nuget.",
            "com.unity.searcher",
            "com.unity.render-pipelines.core",       // URP/HDRP 본체가 이미 파이프라인을 말해줌
            "com.unity.render-pipelines.universal-config",
            "com.enginecontext"                      // 도구 자신
        };

        public static string Render(Snapshot snapshot, FormatterLimits limits)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Tech Stack / Packages");

            if (snapshot.packages.Count == 0)
            {
                sb.Append("_none detected_");
                return sb.ToString();
            }

            var visible = new List<PackageEntry>();
            var omitted = 0;
            foreach (var package in snapshot.packages)
            {
                if (IsOmitted(package.name))
                    omitted++;
                else
                    visible.Add(package);
            }

            if (visible.Count == 0)
            {
                sb.Append("_no notable packages — only Unity built-in modules detected ("
                          + omitted + " omitted)_");
                return sb.ToString();
            }

            // 정렬은 IR에서 완료됨 — 여기서는 notable/기타로 분할만 (안정 분할 = 결정론 유지)
            var notable = new List<PackageEntry>();
            var others = new List<PackageEntry>();
            foreach (var package in visible)
            {
                if (IsNotable(package.name))
                    notable.Add(package);
                else
                    others.Add(package);
            }

            var ordered = new List<PackageEntry>(notable.Count + others.Count);
            ordered.AddRange(notable);
            ordered.AddRange(others);

            var capped = FormatterLimits.Cap(ordered, limits.MaxPackages, out var hidden);
            foreach (var package in capped)
                sb.AppendLine("- `" + package.name + "` " + package.version + " (" + package.source + ")");
            if (hidden > 0)
                sb.AppendLine("-" + FormatterLimits.More(hidden));

            if (omitted > 0)
                sb.AppendLine("\n_(" + omitted + " Unity built-in modules / editor infrastructure packages omitted)_");

            return sb.ToString().TrimEnd();
        }

        private static bool IsOmitted(string packageName)
        {
            foreach (var prefix in OmittedPrefixes)
            {
                if (packageName.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool IsNotable(string packageName)
        {
            var lower = packageName.ToLowerInvariant();
            foreach (var keyword in NotableKeywords)
            {
                if (lower.Contains(keyword))
                    return true;
            }
            return false;
        }
    }
}
