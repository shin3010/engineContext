using System.Collections.Generic;

namespace EngineContext.Editor.Formatting
{
    /// <summary>
    /// L5 — 섹션별 항목 상한·트리 깊이·전체 문자 예산 + 절단 헬퍼 (TA §5.1, FS §4.3).
    /// 수치는 문서(FS/TA)가 규정하지 않아 PLAN.md의 "제안 기본값"이며, 여기 한 곳에서만 튜닝한다.
    /// </summary>
    public sealed class FormatterLimits
    {
        public int MaxHierarchyDepth = 5;
        public int MaxChildrenPerNode = 20;
        public int MaxComponentsPerNode = 8;
        public int MaxFolders = 80;
        public int MaxPackages = 60;
        public int MaxAssemblies = 40;
        public int MaxPrefabs = 100;
        public int MaxUsedByPerPrefab = 5;
        public int MaxScriptableObjects = 100;
        public int MaxFieldsPerSo = 20;
        public int MaxComponentTypes = 200;
        public int MaxKeyRefEntries = 30;
        public int MaxEventBindings = 80;
        public int MaxDefineSymbols = 20;
        public int MaxBuildScenes = 30;
        public int MaxApiTypes = 50;
        public int MaxApiMembersPerList = 20;
        public int MaxNotes = 20;
        public int MaxTotalChars = 40000;

        public static FormatterLimits Default => new FormatterLimits();

        /// <summary>전체 예산 초과 시 사용하는 조밀한 상한 (E9 자동 요약).</summary>
        public static FormatterLimits Tight => new FormatterLimits
        {
            MaxHierarchyDepth = 3,
            MaxChildrenPerNode = 8,
            MaxComponentsPerNode = 5,
            MaxFolders = 40,
            MaxPackages = 40,
            MaxAssemblies = 25,
            MaxPrefabs = 40,
            MaxUsedByPerPrefab = 3,
            MaxScriptableObjects = 40,
            MaxFieldsPerSo = 10,
            MaxComponentTypes = 120,
            MaxKeyRefEntries = 15,
            MaxEventBindings = 40,
            MaxDefineSymbols = 10,
            MaxBuildScenes = 15,
            MaxApiTypes = 25,
            MaxApiMembersPerList = 10,
            MaxNotes = 10,
            MaxTotalChars = 40000
        };

        /// <summary>상한 초과분 고지 문자열: " (+N more)" (FS §4.2).</summary>
        public static string More(int hidden)
        {
            return hidden > 0 ? " (+" + hidden + " more)" : "";
        }

        /// <summary>목록을 상위 max개로 절단하고 숨겨진 개수를 반환.</summary>
        public static IReadOnlyList<T> Cap<T>(List<T> source, int max, out int hidden)
        {
            if (source == null)
            {
                hidden = 0;
                return new List<T>();
            }
            hidden = source.Count > max ? source.Count - max : 0;
            return hidden == 0 ? source : source.GetRange(0, max);
        }
    }
}
