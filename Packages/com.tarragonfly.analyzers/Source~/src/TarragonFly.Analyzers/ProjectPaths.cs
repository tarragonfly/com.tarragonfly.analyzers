using System;

namespace TarragonFly.Analyzers
{
    public static class ProjectPaths
    {
        private static readonly string[] TopFolders = { "/Assets/", "/Packages/", "/Library/" };

        // Unity passes project-relative paths ("Assets/Game/Player.cs"); Rider passes absolute ones.
        // An absolute path is cut at its first Assets/, Packages/ or Library/ folder. A project that
        // itself sits inside a folder with one of those names gets cut too early; fine for now, revisit
        // if that layout turns up.
        public static string ToProjectRelative(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return string.Empty;
            string path = filePath.Replace('\\', '/');
            if (path.StartsWith("./", StringComparison.Ordinal)) path = path.Substring(2);
            if (!IsRooted(path)) return path;

            int cut = -1;
            foreach (string folder in TopFolders)
            {
                int index = path.IndexOf(folder, StringComparison.OrdinalIgnoreCase);
                if (index >= 0 && (cut < 0 || index < cut)) cut = index;
            }
            return cut < 0 ? path : path.Substring(cut + 1);
        }

        // "Assets" and "Assets/" both mean the folder; a .cs entry means that one file.
        public static bool IsUnder(string projectRelativePath, string folderOrFile)
        {
            string entry = folderOrFile.Replace('\\', '/').Trim();
            if (entry.Length == 0) return false;
            if (!entry.EndsWith("/", StringComparison.Ordinal) && !entry.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) entry += "/";
            return entry.EndsWith("/", StringComparison.Ordinal)
                ? projectRelativePath.StartsWith(entry, StringComparison.OrdinalIgnoreCase)
                : projectRelativePath.Equals(entry, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRooted(string path)
        {
            return path.StartsWith("/", StringComparison.Ordinal) || (path.Length > 1 && path[1] == ':');
        }
    }
}