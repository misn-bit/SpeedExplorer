using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SpeedExplorer;

public static class ManualContextActionService
{
    public static List<ToolStripItem> BuildMenuItems(string[] paths)
    {
        var items = new List<ToolStripItem>();
        var actions = AppSettings.Current.ManualContextActions ?? new List<ManualContextAction>();
        if (paths.Length == 0 || actions.Count == 0) return items;

        bool hasFiles = paths.Any(File.Exists);
        bool hasDirs = paths.Any(Directory.Exists);
        bool mixed = hasFiles && hasDirs;
        string first = paths[0];

        foreach (var action in actions.Where(a => a.VisibleInShell))
        {
            if (!action.AllowMultiple && paths.Length > 1) continue;

            string applies = action.AppliesTo?.Trim() ?? "Both";
            if (applies.Equals("Files", StringComparison.OrdinalIgnoreCase) && (hasDirs || mixed)) continue;
            if (applies.Equals("Folders", StringComparison.OrdinalIgnoreCase) && (hasFiles || mixed)) continue;

            if (!string.IsNullOrWhiteSpace(action.Extensions) && !hasDirs)
            {
                var exts = action.Extensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
                    .ToHashSet();
                if (exts.Count > 0)
                {
                    bool allMatch = paths.All(p => exts.Contains(Path.GetExtension(p).ToLowerInvariant()));
                    if (!allMatch) continue;
                }
            }

            var item = new ToolStripMenuItem(action.Name);
            if (string.IsNullOrWhiteSpace(action.Command))
            {
                item.Enabled = false;
            }
            else
            {
                item.Click += (s, e) => ExecuteAction(action, paths);
            }
            items.Add(item);
        }

        return items;
    }

    public static void ExecuteAction(ManualContextAction action, string[] paths)
    {
        if (string.IsNullOrWhiteSpace(action.Command) || paths.Length == 0) return;
        string command = action.Command.Trim();
        if (command.Length == 0) return;

        string first = paths[0];
        string dir = Directory.Exists(first) ? first : (Path.GetDirectoryName(first) ?? "");
        string name = Path.GetFileName(first);
        string ext = Path.GetExtension(first);
        string pathsArg = string.Join(" ", paths.Select(p => $"\"{p}\""));

        string args = action.Args ?? "";
        args = args.Replace("{path}", $"\"{first}\"");
        args = args.Replace("{paths}", pathsArg);
        args = args.Replace("{dir}", $"\"{dir}\"");
        args = args.Replace("{name}", name);
        args = args.Replace("{ext}", ext);

        string workingDir = action.WorkingDir ?? "";
        workingDir = workingDir.Replace("{path}", first)
                               .Replace("{dir}", dir)
                               .Replace("{name}", name)
                               .Replace("{ext}", ext);

        var psi = new ProcessStartInfo(command, args)
        {
            UseShellExecute = true
        };
        if (!string.IsNullOrWhiteSpace(workingDir))
            psi.WorkingDirectory = workingDir.Trim('"');

        try { Process.Start(psi); }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to run action: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
