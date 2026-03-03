using System;
using System.IO;
using System.Linq;
using System.Text;

namespace SpeedExplorer;

public static partial class LlmPromptBuilder
{
    private const int MaxDirectoryContextItems = 500;

    /// <summary>
    /// Builds a compact extension/count summary for the current directory.
    /// </summary>
    public static string BuildExtensionContext(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return "(no directory provided)";

        try
        {
            if (!Directory.Exists(directory))
                return $"(directory not found: {directory})";

            var files = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).ToList();
            if (files.Count == 0)
                return "(no files in current directory)";

            var extGroups = files
                .Select(file =>
                {
                    string ext = Path.GetExtension(file);
                    return string.IsNullOrWhiteSpace(ext) ? "[no_ext]" : ext.ToLowerInvariant();
                })
                .GroupBy(ext => ext, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => $"{g.Key} ({g.Count()})");

            return string.Join(", ", extGroups);
        }
        catch
        {
            return "(unable to scan directory)";
        }
    }

    /// <summary>
    /// Builds a detailed top-level file/folder list for the current directory.
    /// </summary>
    public static string BuildFullDirectoryContext(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return "(no directory provided)";

        try
        {
            if (!Directory.Exists(directory))
                return $"(directory not found: {directory})";

            var entries = new DirectoryInfo(directory)
                .EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(e => (e.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxDirectoryContextItems + 1)
                .ToList();

            if (entries.Count == 0)
                return "(empty directory)";

            var sb = new StringBuilder();
            int count = 0;
            foreach (var entry in entries)
            {
                if (count >= MaxDirectoryContextItems)
                {
                    sb.AppendLine("...(truncated)");
                    break;
                }

                bool isDir = (entry.Attributes & FileAttributes.Directory) == FileAttributes.Directory;
                sb.AppendLine($"{(isDir ? "[DIR]" : "[FILE]")} {entry.Name}");
                count++;
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"(error reading directory: {ex.Message})";
        }
    }
}
