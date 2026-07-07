using System.Collections.Generic;
using System.Text;
using EngineContext.Editor.Model;

namespace EngineContext.Editor.Formatting.Sections
{
    /// <summary>섹션 7 — Prefab Inventory: Prefab 목록 + 사용처 (FS §4.1).</summary>
    internal static class PrefabInventorySection
    {
        public static string Render(Snapshot snapshot, FormatterLimits limits)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Prefab Inventory");

            if (snapshot.prefabs.Count == 0)
            {
                sb.Append("_none detected_");
                return sb.ToString();
            }

            var capped = FormatterLimits.Cap(snapshot.prefabs, limits.MaxPrefabs, out var hidden);
            foreach (var prefab in capped)
            {
                var line = "- `" + prefab.name + "` (`" + prefab.path + "`)";

                if (prefab.rootComponents.Count > 0)
                {
                    var comps = FormatterLimits.Cap(prefab.rootComponents, limits.MaxComponentsPerNode, out var compHidden);
                    var codes = new List<string>();
                    foreach (var component in comps)
                    {
                        if (component != "Transform")
                            codes.Add("`" + component + "`");
                    }
                    if (codes.Count > 0)
                        line += " - root: " + string.Join(", ", codes) + FormatterLimits.More(compHidden);
                }

                if (prefab.usedBy.Count > 0)
                {
                    var users = FormatterLimits.Cap(prefab.usedBy, limits.MaxUsedByPerPrefab, out var usedHidden);
                    var codes = new List<string>();
                    foreach (var user in users)
                        codes.Add("`" + user + "`");
                    line += " - used by: " + string.Join(", ", codes) + FormatterLimits.More(usedHidden);
                }

                sb.AppendLine(line);
            }
            if (hidden > 0)
                sb.AppendLine("-" + FormatterLimits.More(hidden));

            return sb.ToString().TrimEnd();
        }
    }
}
