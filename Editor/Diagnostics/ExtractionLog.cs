using System.Collections.Generic;

namespace EngineContext.Editor.Diagnostics
{
    /// <summary>
    /// 횡단 Diagnostics — 스킵된 참조·경고·타이밍 집계 (TA §3 ExtractionLog, FS F10).
    /// 수집 중 어떤 참조 오류에도 크래시 없이 누락만 기록한다.
    /// </summary>
    public class ExtractionLog
    {
        private const int MaxWarnings = 30;
        private const int MaxSkipMessages = 10;

        private readonly HashSet<string> onceKeys = new HashSet<string>();
        private int skipMessageCount;

        public int SkippedRefCount { get; private set; }
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Timings { get; } = new List<string>();

        /// <summary>깨진/누락 참조 스킵 기록 (E4). 카운트는 전부, 메시지는 상한까지만.</summary>
        public void AddSkip(string context)
        {
            SkippedRefCount++;
            if (skipMessageCount < MaxSkipMessages)
            {
                skipMessageCount++;
                AddWarning("Skipped: " + context);
            }
        }

        public void AddWarning(string message)
        {
            if (Warnings.Count < MaxWarnings && !Warnings.Contains(message))
                Warnings.Add(message);
        }

        /// <summary>동일 종류의 경고를 1회만 기록 (예: 깊이 상한 도달 고지, E5).</summary>
        public void AddWarningOnce(string key, string message)
        {
            if (onceKeys.Add(key))
                AddWarning(message);
        }

        public void AddTiming(string step, long milliseconds)
        {
            Timings.Add(step + ": " + milliseconds + " ms");
        }

        public string Summary()
        {
            return "스킵된 참조 " + SkippedRefCount + "건 · 경고 " + Warnings.Count + "건";
        }
    }
}
