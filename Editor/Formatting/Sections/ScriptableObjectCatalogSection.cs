using System.Collections.Generic;
using System.Text;
using EngineContext.Editor.Model;

namespace EngineContext.Editor.Formatting.Sections
{
    /// <summary>섹션 8 — ScriptableObject Catalog: SO 타입 + 필드 스키마, 값 제외 (FS §4.1).</summary>
    internal static class ScriptableObjectCatalogSection
    {
        public static string Render(Snapshot snapshot, FormatterLimits limits)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## ScriptableObject Catalog");

            if (snapshot.scriptableObjects.Count == 0)
            {
                sb.Append("_none detected_");
                return sb.ToString();
            }

            var capped = FormatterLimits.Cap(snapshot.scriptableObjects, limits.MaxScriptableObjects, out var hidden);
            foreach (var so in capped)
            {
                var line = "- `" + so.name + "` : `" + so.type + "` (`" + so.path + "`)";
                if (so.fields.Count > 0)
                {
                    var fields = FormatterLimits.Cap(so.fields, limits.MaxFieldsPerSo, out var fieldHidden);
                    var codes = new List<string>();
                    foreach (var field in fields)
                        codes.Add("`" + field.name + ": " + field.type + "`");
                    line += " - fields: " + string.Join(", ", codes) + FormatterLimits.More(fieldHidden);
                }
                sb.AppendLine(line);
            }
            if (hidden > 0)
                sb.AppendLine("-" + FormatterLimits.More(hidden));

            return sb.ToString().TrimEnd();
        }
    }
}
