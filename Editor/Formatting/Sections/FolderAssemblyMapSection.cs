using System.Collections.Generic;
using System.Text;
using EngineContext.Editor.Model;

namespace EngineContext.Editor.Formatting.Sections
{
    /// <summary>섹션 5 — Folder & Assembly Map: 폴더 트리(깊이 제한) + asmdef 경계 (FS §4.1).</summary>
    internal static class FolderAssemblyMapSection
    {
        public static string Render(Snapshot snapshot, FormatterLimits limits)
        {
            if (snapshot.folders.tree.Count == 0 && snapshot.assemblies.Count == 0)
                return ""; // 데이터 없음 → 섹션 생략 (FS §4.2)

            var sb = new StringBuilder();
            sb.AppendLine("## Folder & Assembly Map");

            if (snapshot.folders.tree.Count > 0)
            {
                sb.AppendLine("### Folders (under `" + snapshot.folders.root + "`)");
                var capped = FormatterLimits.Cap(snapshot.folders.tree, limits.MaxFolders, out var hidden);
                foreach (var folder in capped)
                {
                    var depth = CountSlashes(folder.path);
                    var indent = new string(' ', (depth - 1) * 2);
                    sb.AppendLine(indent + "- `" + folder.path + "` (" + folder.childCount + " items)");
                }
                if (hidden > 0)
                    sb.AppendLine("-" + FormatterLimits.More(hidden));
            }

            if (snapshot.assemblies.Count > 0)
            {
                sb.AppendLine("### Assemblies (asmdef)");
                var capped = FormatterLimits.Cap(snapshot.assemblies, limits.MaxAssemblies, out var hidden);
                foreach (var assembly in capped)
                {
                    var line = "- `" + assembly.name + "` - `" + assembly.path + "`";
                    if (assembly.references.Count > 0)
                    {
                        var refs = FormatterLimits.Cap(assembly.references, 8, out var refHidden);
                        var codes = new List<string>();
                        foreach (var reference in refs)
                            codes.Add("`" + reference + "`");
                        line += " - refs: " + string.Join(", ", codes) + FormatterLimits.More(refHidden);
                    }
                    sb.AppendLine(line);
                }
                if (hidden > 0)
                    sb.AppendLine("-" + FormatterLimits.More(hidden));
            }

            return sb.ToString().TrimEnd();
        }

        private static int CountSlashes(string path)
        {
            var count = 0;
            foreach (var ch in path)
            {
                if (ch == '/')
                    count++;
            }
            return count;
        }
    }
}
