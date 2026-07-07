using System.Text;
using EngineContext.Editor.Model;

namespace EngineContext.Editor.Formatting.Sections
{
    /// <summary>섹션 10 — Notes & Caveats: 스킵 참조·스캔 범위 한계·재생성 안내 (FS §4.1, F10).</summary>
    internal static class NotesSection
    {
        public static string Render(Snapshot snapshot, FormatterLimits limits)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Notes & Caveats");

            var capped = FormatterLimits.Cap(snapshot.notes, limits.MaxNotes, out var hidden);
            foreach (var note in capped)
                sb.AppendLine("- " + note);
            if (hidden > 0)
                sb.AppendLine("-" + FormatterLimits.More(hidden));

            if (snapshot.meta.truncated)
                sb.AppendLine("- Some lists were summarized to fit the size budget; regenerate with a narrower scope if more detail is needed. ");

            sb.Append("- This file is a point-in-time snapshot. If the project has changed or something looks inaccurate, "
                      + "regenerate it via `Tools > EngineContext > Generate Context` instead of editing by hand.");
            return sb.ToString();
        }
    }
}
