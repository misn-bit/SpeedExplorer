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
                var cleanName = Path.GetFileName(fileName);
                var fullPath = Path.Combine(_baseDirectory, cleanName);
                if (File.Exists(fullPath))
                    result.Add(fullPath);
                else
                    LlmDebugLogger.LogExecution($"File not found: {fileName}", false);
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

    /// <summary>
    /// Resolves a path that can be relative or absolute.
    /// </summary>
    private string ResolvePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return _baseDirectory;

        // Absolute path
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        // Relative path (./Folder, Folder, ..\Folder)
        return Path.GetFullPath(Path.Combine(_baseDirectory, path));
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
