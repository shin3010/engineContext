using System;
using UnityEditor.PackageManager;
using EngineContext.Editor.Diagnostics;
using EngineContext.Editor.Model;

namespace EngineContext.Editor.Extraction
{
    /// <summary>
    /// L3 — 설치 패키지명·버전·출처 수집 (TA §3, FS §3).
    /// 읽기 실패 시 Packages 섹션만 degrade하고 나머지는 진행 (E11).
    /// </summary>
    internal static class PackageExtractor
    {
        public static void Extract(Snapshot snapshot, ExtractionLog log)
        {
            try
            {
                var infos = PackageInfo.GetAllRegisteredPackages();
                Array.Sort(infos, (a, b) => string.CompareOrdinal(a.name, b.name)); // 결정론

                foreach (var info in infos)
                {
                    snapshot.packages.Add(new PackageEntry
                    {
                        name = info.name,
                        version = info.version,
                        source = ToSourceName(info.source)
                    });
                }
            }
            catch (Exception ex)
            {
                // E11: 패키지 매니페스트/레지스트리 읽기 실패 → 섹션만 제외하고 계속
                log.AddWarning("Package information could not be read; the Packages section may be incomplete. (" + ex.Message + ")");
            }
        }

        private static string ToSourceName(PackageSource source)
        {
            switch (source)
            {
                case PackageSource.Registry: return "registry";
                case PackageSource.Git: return "git";
                case PackageSource.BuiltIn: return "builtin";
                case PackageSource.Embedded:
                case PackageSource.Local:
                case PackageSource.LocalTarball: return "local";
                default: return source.ToString().ToLowerInvariant();
            }
        }
    }
}
