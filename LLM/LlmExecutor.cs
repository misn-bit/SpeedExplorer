using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SpeedExplorer;

/// <summary>
/// Executes LLM commands with deduplication and move-anywhere support.
/// Files can only originate from the current directory but can be moved anywhere.
/// </summary>
public class LlmExecutor
{
    private readonly string _baseDirectory;
    private readonly IntPtr _ownerHandle;
    private readonly List<FileOperation> _operations = new();

    public LlmExecutor(string baseDirectory, IntPtr ownerHandle)
    {
        _baseDirectory = Path.GetFullPath(baseDirectory);
        _ownerHandle = ownerHandle;
    }

    /// <summary>
    /// Pre-processes commands to deduplicate file actions, then executes.
    /// If the same file appears in multiple commands, only the LAST action is kept.
    /// </summary>
    public List<FileOperation> ExecuteCommands(List<LlmCommand> commands)
    {
        _operations.Clear();

        // Build a map of final actions per file (last action wins)
        var fileActions = new Dictionary<string, LlmCommand>(StringComparer.OrdinalIgnoreCase);
        var nonFileCommands = new List<LlmCommand>(); // create_folder, create_file

        foreach (var cmd in commands)
        {
            switch (cmd.Cmd?.ToLowerInvariant())
            {
                case "create_folder":
                case "create_file":
                    // These don't operate on existing files, execute in order
                    nonFileCommands.Add(cmd);
                    break;

                case "move":
                    // Track each file's final destination
                    var filesToMove = ResolveFiles(cmd);
                    foreach (var file in filesToMove)
                    {
                        fileActions[file] = cmd; // Last action wins
                    }
                    break;

                case "rename":
                    if (!string.IsNullOrEmpty(cmd.File))
                    {
                        var fullPath = Path.Combine(_baseDirectory, Path.GetFileName(cmd.File));
                        fileActions[fullPath] = cmd;
                    }
                    break;

                case "tag":
                    // Tags don't conflict with move/rename, apply immediately
                    ExecuteTag(cmd);
                    break;

                default:
                    LlmDebugLogger.LogExecution($"Unknown command: {cmd.Cmd}", false);
                    break;
            }
        }

        // Execute non-file commands first (create folders, create files)
        foreach (var cmd in nonFileCommands)
        {
            switch (cmd.Cmd?.ToLowerInvariant())
            {
                case "create_folder":
                    ExecuteCreateFolder(cmd.Path ?? cmd.Name);
                    break;
                case "create_file":
                    ExecuteCreateFile(cmd);
                    break;
            }
        }

        // Execute deduplicated file actions
        // Group by destination for moves
        var movesByDestination = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var renameActions = new List<(string source, string newName)>();

        foreach (var kvp in fileActions)
        {
            var filePath = kvp.Key;
            var cmd = kvp.Value;

            if (cmd.Cmd?.ToLowerInvariant() == "rename")
            {
                renameActions.Add((filePath, cmd.NewName ?? ""));
            }
            else if (cmd.Cmd?.ToLowerInvariant() == "move")
            {
                var dest = ResolvePath(cmd.To);
                if (!movesByDestination.ContainsKey(dest))
                    movesByDestination[dest] = new List<string>();
                movesByDestination[dest].Add(filePath);
            }
        }

        // Execute renames
        foreach (var (source, newName) in renameActions)
        {
            ExecuteRename(source, newName);
        }

        // Execute moves grouped by destination
        foreach (var kvp in movesByDestination)
        {
            ExecuteMove(kvp.Value, kvp.Key);
        }

        return _operations;
    }

    /// <summary>
    /// Executes commands inside an agent loop block and returns string feedback for the LLM.
    /// </summary>
    public (List<string> Feedback, List<FileOperation> Operations) ExecuteAgenticCommands(List<LlmCommand> commands)
    {
        var feedback = new List<string>();
        var writeCommands = new List<LlmCommand>();
        var ops = new List<FileOperation>();

        foreach (var cmd in commands)
        {
            try
            {
                switch (cmd.Cmd?.ToLowerInvariant())
                {
                    case "list_dir":
                        feedback.Add(ExecuteListDir(cmd));
                        break;
                    case "search":
                        feedback.Add(ExecuteSearch(cmd));
                        break;
                    case "search_tags":
                        feedback.Add(ExecuteSearchTags(cmd));
                        break;
                    case "move":
                        if (string.IsNullOrWhiteSpace(cmd.To))
                        {
                            feedback.Add("[move] Missing destination path ('to').");
                            break;
                        }
                        if ((cmd.Files == null || cmd.Files.Count == 0) && string.IsNullOrWhiteSpace(cmd.Pattern))
                        {
                            feedback.Add("[move] Missing source selection. Provide either 'files' or 'pattern'.");
                            break;
                        }

                        var resolved = ResolveFiles(cmd);
                        if (resolved.Count == 0)
                        {
                            string selector = !string.IsNullOrWhiteSpace(cmd.Pattern)
                                ? $"pattern '{cmd.Pattern}'"
                                : "provided file names";
                            feedback.Add($"[move] No source files matched for {selector}. This may mean files are already moved. Use list_dir/search to confirm.");
                            break;
                        }

                        writeCommands.Add(cmd);
                        break;
                    default:
                        writeCommands.Add(cmd);
                        break;
                }
            }
            catch (Exception ex)
            {
                feedback.Add($"[{cmd.Cmd}] Error: {ex.Message}");
            }
        }

        if (writeCommands.Any())
        {
            string attempted = SummarizeWriteCommands(writeCommands);
            if (!string.IsNullOrWhiteSpace(attempted))
                feedback.Add($"Write commands attempted: {attempted}");

            ops = ExecuteCommands(writeCommands).ToList(); // .ToList() creates a copy since ExecuteCommands returns a reference to _operations

            if (ops.Count > 0)
            {
                feedback.Add($"Processed {writeCommands.Count} write command(s) successfully; {ops.Count} undo operation record(s) created.");
                feedback.Add("If this fulfills the user request, set is_done=true and return no further commands.");
            }
            else
            {
                feedback.Add($"Processed {writeCommands.Count} write command(s), but no new changes were applied.");
                feedback.Add("If files were already moved/renamed and request is satisfied, set is_done=true and return no further commands.");
            }
        }

        return (feedback, ops);
    }

    private static string SummarizeWriteCommands(List<LlmCommand> commands)
    {
        if (commands == null || commands.Count == 0)
            return "";

        var parts = new List<string>();
        foreach (var cmd in commands.Take(8))
        {
            string name = (cmd.Cmd ?? "").Trim().ToLowerInvariant();
            switch (name)
            {
                case "move":
                    string sourcePart;
                    if (cmd.Files != null && cmd.Files.Count > 0)
                    {
                        sourcePart = cmd.Files.Count <= 2
                            ? string.Join(", ", cmd.Files.Select(f => Path.GetFileName(f ?? "")))
                            : $"{cmd.Files.Count} file(s)";
                    }
                    else if (!string.IsNullOrWhiteSpace(cmd.Pattern))
                    {
                        sourcePart = $"pattern '{cmd.Pattern}'";
                    }
                    else
                    {
                        sourcePart = "unspecified source";
                    }
                    parts.Add($"move {sourcePart} -> {cmd.To}");
                    break;
                case "create_folder":
                    parts.Add($"create_folder {cmd.Path}");
                    break;
                case "rename":
                    parts.Add($"rename {cmd.File} -> {cmd.NewName}");
                    break;
                case "create_file":
                    parts.Add($"create_file {cmd.Name}");
                    break;
                case "tag":
                    parts.Add($"tag {cmd.Files?.Count ?? 0} file(s)");
                    break;
                default:
                    parts.Add(name);
                    break;
            }
        }

        if (commands.Count > 8)
            parts.Add($"+{commands.Count - 8} more");

        return string.Join("; ", parts);
    }

    private string ExecuteListDir(LlmCommand cmd)
    {
        string path = ResolvePath(cmd.Path);
        if (!Directory.Exists(path)) return $"[list_dir] Path not found: {path}";
        var items = new DirectoryInfo(path).GetFileSystemInfos("*", SearchOption.TopDirectoryOnly);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[list_dir {path}]:");
        foreach(var item in items)
        {
            if (cmd.IncludeMetadata)
            {
                if (item is FileInfo fi)
                    sb.AppendLine($"[FILE] {item.Name} ({fi.Length} bytes, {fi.LastWriteTimeUtc:yyyy-MM-dd})");
                else
                    sb.AppendLine($"[DIR]  {item.Name}");
            }
            else
            {
                sb.AppendLine($"{(item is DirectoryInfo ? "[DIR]" : "[FILE]")} {item.Name}");
            }
        }
        sb.AppendLine($"[summary] total_items={items.Length}");
        return sb.ToString().TrimEnd();
    }

    private string ExecuteSearch(LlmCommand cmd)
    {
        string root = ResolvePath(cmd.Root);
        if (!Directory.Exists(root)) return $"[search] Root not found: {root}";
        if (string.IsNullOrWhiteSpace(cmd.Pattern)) return "[search] Missing pattern";
        string pattern = cmd.Pattern.Trim();
        if (pattern == "..." || pattern == "?" || pattern == "??" || pattern == ".." || pattern == ".")
            return "[search] Invalid placeholder pattern. Use real glob patterns like *.jpeg or *.jpg.";

        var matches = Directory.GetFiles(root, pattern, SearchOption.AllDirectories).Take(50).ToList();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[search {pattern} in {root}]:");
        foreach(var m in matches) sb.AppendLine(Path.GetRelativePath(_baseDirectory, m));
        if (matches.Count == 50) sb.AppendLine("...(truncated to 50 results)");
        string res = sb.ToString().TrimEnd();
        return res == $"[search {pattern} in {root}]:" ? $"[search {pattern}]: No matches found." : res;
    }

    private string ExecuteSearchTags(LlmCommand cmd)
    {
        if (cmd.Tags == null || !cmd.Tags.Any()) return "[search_tags] No tags specified";

        var tagFilters = cmd.Tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!tagFilters.Any()) return "[search_tags] No valid tags specified";

        IEnumerable<string>? matchSet = null;
        foreach (var tag in tagFilters)
        {
            var tagMatches = TagManager.Instance.GetPathsWithTag(_baseDirectory, tag);
            matchSet = matchSet == null
                ? tagMatches
                : matchSet.Intersect(tagMatches, StringComparer.OrdinalIgnoreCase);
        }

        var results = (matchSet ?? Enumerable.Empty<string>()).Take(50).ToList();
        var sb = new System.Text.StringBuilder();
        var searchStr = string.Join(",", tagFilters);
        sb.AppendLine($"[search_tags {searchStr}]:");
        foreach(var r in results) sb.AppendLine(Path.GetRelativePath(_baseDirectory, r));
        if (results.Count == 50) sb.AppendLine("...(truncated to 50 results)");
        string res = sb.ToString().TrimEnd();
        return res == $"[search_tags {searchStr}]:" ? $"[search_tags]: No matches found." : res;
    }

    /// <summary>
    /// Resolves files from a move command (by files list or pattern).
    /// </summary>
    private List<string> ResolveFiles(LlmCommand cmd)
    {
        var result = new List<string>();

        // Explicit file list
        if (cmd.Files != null && cmd.Files.Count > 0)
        {
            foreach (var fileName in cmd.Files)
            {
                if (TryResolveFileReference(fileName, out var fullPath))
                {
                    result.Add(fullPath);
                }
                else
                {
                    LlmDebugLogger.LogExecution($"File not found or out of scope: {fileName}", false);
                }
            }
        }
        // Pattern matching
        else if (!string.IsNullOrEmpty(cmd.Pattern))
        {
            try
            {
                var matches = Directory.GetFiles(_baseDirectory, cmd.Pattern, SearchOption.TopDirectoryOnly);
                result.AddRange(matches);
            }
            catch (Exception ex)
            {
                LlmDebugLogger.LogExecution($"Pattern match failed: {cmd.Pattern} - {ex.Message}", false);
            }
        }

        return result;
    }

    private bool TryResolveFileReference(string? fileRef, out string fullPath)
    {
        fullPath = "";
        if (string.IsNullOrWhiteSpace(fileRef))
            return false;

        var candidates = new List<string>();
        string trimmed = fileRef.Trim();

        if (Path.IsPathRooted(trimmed))
        {
            candidates.Add(trimmed);
        }
        else
        {
            // Preferred: honor subfolder-relative references under base directory.
            candidates.Add(Path.Combine(_baseDirectory, trimmed));
            // Backward compatibility: plain basename in current directory.
            candidates.Add(Path.Combine(_baseDirectory, Path.GetFileName(trimmed)));
        }

        foreach (var candidate in candidates)
        {
            try
            {
                string normalized = Path.GetFullPath(candidate);
                if (!normalized.StartsWith(_baseDirectory, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!File.Exists(normalized))
                    continue;

                fullPath = normalized;
                return true;
            }
            catch
            {
                // Ignore bad candidate and continue.
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves a path that can be relative or absolute.
    /// </summary>
    private string ResolvePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return _baseDirectory;

        string trimmed = path.Trim();
        if (trimmed == "~" || trimmed == "." || trimmed == "./" || trimmed == ".\\")
            return _baseDirectory;

        if (trimmed.StartsWith("~/", StringComparison.Ordinal) || trimmed.StartsWith("~\\", StringComparison.Ordinal))
            trimmed = "." + trimmed.Substring(1);

        // Handle malformed mixed forms emitted by models like "./H:\\Testing" or ".\\C:\\Data".
        if ((trimmed.StartsWith("./", StringComparison.Ordinal) || trimmed.StartsWith(".\\", StringComparison.Ordinal)) &&
            trimmed.Length > 2)
        {
            string afterDot = trimmed.Substring(2);
            if (Path.IsPathRooted(afterDot))
                return Path.GetFullPath(afterDot);
        }

        // Absolute path
        if (Path.IsPathRooted(trimmed))
            return Path.GetFullPath(trimmed);

        // Relative path (./Folder, Folder, ..\Folder)
        return Path.GetFullPath(Path.Combine(_baseDirectory, trimmed));
    }

    private void ExecuteCreateFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            LlmDebugLogger.LogExecution("create_folder: missing path", false);
            return;
        }

        string fullPath = ResolvePath(path);

        // Skip if exists
        if (Directory.Exists(fullPath))
        {
            LlmDebugLogger.LogExecution($"create_folder: skipped '{path}' (already exists)");
            return;
        }

        if (File.Exists(fullPath))
        {
            LlmDebugLogger.LogExecution($"create_folder: failed '{path}' (file with same name exists)", false);
            return;
        }

        try
        {
            Directory.CreateDirectory(fullPath);
            LlmDebugLogger.LogExecution($"create_folder: '{path}'");
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogExecution($"create_folder: failed '{path}' - {ex.Message}", false);
        }
    }

    private void ExecuteCreateFile(LlmCommand cmd)
    {
        if (string.IsNullOrEmpty(cmd.Name)) return;

        string fileName = cmd.Name;
        // Clean filename
        foreach (char c in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c, '_');

        string fullPath = Path.Combine(_baseDirectory, fileName);

        // Auto-rename if exists to avoid overwriting
        if (File.Exists(fullPath))
        {
            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            int i = 1;
            while (File.Exists(fullPath))
            {
                fullPath = Path.Combine(_baseDirectory, $"{nameWithoutExt}_{i}{ext}");
                i++;
            }
        }

        try
        {
            File.WriteAllText(fullPath, cmd.Content ?? "");
            _operations.Add(new CreateFileOperation(fullPath));
            LlmDebugLogger.LogExecution($"create_file: '{Path.GetFileName(fullPath)}'");
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Failed to create file {cmd.Name}: {ex.Message}");
        }
    }

    private void ExecuteRename(string sourcePath, string newName)
    {
        if (string.IsNullOrEmpty(newName))
        {
            LlmDebugLogger.LogExecution("rename: missing newName", false);
            return;
        }

        if (!File.Exists(sourcePath))
        {
            LlmDebugLogger.LogExecution($"rename: source not found '{Path.GetFileName(sourcePath)}'", false);
            return;
        }

        // Validate source is in base directory
        if (!sourcePath.StartsWith(_baseDirectory, StringComparison.OrdinalIgnoreCase))
        {
            LlmDebugLogger.LogExecution($"rename: file not in current directory", false);
            return;
        }

        // Clean new name
        foreach (char c in Path.GetInvalidFileNameChars())
            newName = newName.Replace(c, '_');

        string destPath = Path.Combine(Path.GetDirectoryName(sourcePath)!, newName);

        // No overwriting
        if (File.Exists(destPath))
        {
            LlmDebugLogger.LogExecution($"rename: skipped ('{newName}' already exists)", false);
            return;
        }

        try
        {
            File.Move(sourcePath, destPath);
            TagManager.Instance.HandleRename(sourcePath, destPath);
            var directory = Path.GetDirectoryName(sourcePath)!;
            _operations.Add(new RenameOperation(directory, Path.GetFileName(sourcePath), newName, _ownerHandle));
            LlmDebugLogger.LogExecution($"rename: '{Path.GetFileName(sourcePath)}' -> '{newName}'");
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogExecution($"rename: failed - {ex.Message}", false);
        }
    }

    private void ExecuteMove(List<string> files, string destination)
    {
        if (files.Count == 0) return;

        // Ensure destination exists
        if (!Directory.Exists(destination))
        {
            try
            {
                Directory.CreateDirectory(destination);
                LlmDebugLogger.LogExecution($"move: auto-created folder '{destination}'");
            }
            catch (Exception ex)
            {
                LlmDebugLogger.LogExecution($"move: failed to create destination '{destination}' - {ex.Message}", false);
                return;
            }
        }

        var movedFiles = new List<string>();
        var destPaths = new List<string>();

        foreach (var file in files)
        {
            // Validate source is in base directory
            if (!file.StartsWith(_baseDirectory, StringComparison.OrdinalIgnoreCase))
            {
                LlmDebugLogger.LogExecution($"move: skipped '{Path.GetFileName(file)}' (not in current directory)", false);
                continue;
            }

            string fileName = Path.GetFileName(file);
            string newPath = Path.Combine(destination, fileName);

            // No overwrites - skip if exists
            if (File.Exists(newPath))
            {
                LlmDebugLogger.LogExecution($"move: skipped '{fileName}' (already exists at destination)", false);
                continue;
            }

            try
            {
                File.Move(file, newPath);
                TagManager.Instance.HandleRename(file, newPath);
                movedFiles.Add(file);
                destPaths.Add(newPath);
                LlmDebugLogger.LogExecution($"move: '{fileName}' -> '{destination}'");
            }
            catch (Exception ex)
            {
                LlmDebugLogger.LogExecution($"move: failed '{fileName}' - {ex.Message}", false);
            }
        }

        // Record for undo
        if (movedFiles.Count > 0)
        {
            var op = new MoveOperation(movedFiles.ToArray(), destination, destPaths, _ownerHandle);
            _operations.Add(op);
        }
    }

    private void ExecuteTag(LlmCommand cmd)
    {
        if (cmd.Tags == null || cmd.Tags.Count == 0)
        {
            LlmDebugLogger.LogExecution("tag: missing tags", false);
            return;
        }

        var files = new List<string>();
        if (cmd.Files != null)
        {
            foreach (var fileName in cmd.Files)
            {
                var cleanName = Path.GetFileName(fileName);
                var fullPath = Path.Combine(_baseDirectory, cleanName);
                if (File.Exists(fullPath))
                    files.Add(fullPath);
            }
        }

        if (files.Count == 0)
        {
            LlmDebugLogger.LogExecution("tag: no valid files specified", false);
            return;
        }

        var op = new TagOperation(files, cmd.Tags);
        op.Redo(); // Apply tags
        _operations.Add(op);
        
        LlmDebugLogger.LogExecution($"tag: applied [{string.Join(", ", cmd.Tags)}] to {files.Count} files");
    }
    }
