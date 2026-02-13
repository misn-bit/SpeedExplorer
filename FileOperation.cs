using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SpeedExplorer;

/// <summary>
/// Base class for all reversible file operations
/// </summary>
public abstract class FileOperation
{
    public DateTime Timestamp { get; protected set; }
    public abstract string GetDescription();
    public abstract void Undo();
    public abstract void Redo();

    protected FileOperation()
    {
        Timestamp = DateTime.Now;
    }
}

/// <summary>
/// Represents a delete operation (send to Recycle Bin)
/// </summary>
public class DeleteOperation : FileOperation
{
    public List<string> DeletedPaths { get; private set; }
    private IntPtr _ownerHandle;

    public DeleteOperation(string[] paths, IntPtr ownerHandle)
    {
        DeletedPaths = new List<string>(paths);
        _ownerHandle = ownerHandle;
    }

    public override string GetDescription()
    {
        if (DeletedPaths.Count == 1)
            return $"Delete {Path.GetFileName(DeletedPaths[0])}";
        return $"Delete {DeletedPaths.Count} items";
    }

    public override void Redo()
    {
        // Delete again (send to Recycle Bin)
        FileSystemService.ShellDelete(DeletedPaths.ToArray(), _ownerHandle, recordOperation: false);
    }

    public override void Undo()
    {
        // Restore from Recycle Bin
        FileSystemService.RestoreFromRecycleBin(DeletedPaths.ToArray(), _ownerHandle);
    }
}

/// <summary>
/// Represents a move operation
/// </summary>
public class MoveOperation : FileOperation
{
    public List<string> SourcePaths { get; private set; }
    public string DestinationFolder { get; private set; }
    public List<string> DestinationPaths { get; private set; }
    private IntPtr _ownerHandle;

    public MoveOperation(string[] sourcePaths, string destinationFolder, IEnumerable<string> actualDestPaths, IntPtr ownerHandle)
    {
        SourcePaths = new List<string>(sourcePaths);
        DestinationFolder = destinationFolder;
        _ownerHandle = ownerHandle;
        DestinationPaths = new List<string>(actualDestPaths);
    }

    public override string GetDescription()
    {
        if (SourcePaths.Count == 1)
            return $"Move {Path.GetFileName(SourcePaths[0])}";
        return $"Move {SourcePaths.Count} items";
    }

    public override void Redo()
    {
        // Move from source to destination
        // Note: Redo might be bit tricky if original collision scenarios re-occur
        // For simplicity, we try to move back to the specific paths we knew
        FileSystemService.ShellMove(SourcePaths.ToArray(), DestinationFolder, _ownerHandle, recordOperation: false);
    }

    public override void Undo()
    {
        // Move back from destination to source folders
        // We use the actual recorded destination paths (which might be renamed versions)
        var grouped = SourcePaths.Zip(DestinationPaths, (src, dst) => new { Source = src, Dest = dst })
            .GroupBy(x => Path.GetDirectoryName(x.Source) ?? "");

        foreach (var group in grouped)
        {
            string sourceDir = group.Key;
            var destPaths = group.Select(x => x.Dest).ToArray();
            FileSystemService.ShellMove(destPaths, sourceDir, _ownerHandle, recordOperation: false);
        }
    }
}

/// <summary>
/// Represents a copy operation
/// </summary>
public class CopyOperation : FileOperation
{
    public List<string> SourcePaths { get; private set; }
    public string DestinationFolder { get; private set; }
    public List<string> CreatedPaths { get; private set; }
    private IntPtr _ownerHandle;
    private bool _renameOnCollision;

    public CopyOperation(string[] sourcePaths, string destinationFolder, IEnumerable<string> actualCreatedPaths, IntPtr ownerHandle, bool renameOnCollision)
    {
        SourcePaths = new List<string>(sourcePaths);
        DestinationFolder = destinationFolder;
        _ownerHandle = ownerHandle;
        _renameOnCollision = renameOnCollision;
        CreatedPaths = new List<string>(actualCreatedPaths);
    }

    public override string GetDescription()
    {
        if (SourcePaths.Count == 1)
            return $"Copy {Path.GetFileName(SourcePaths[0])}";
        return $"Copy {SourcePaths.Count} items";
    }

    public override void Redo()
    {
        // Copy from source to destination
        FileSystemService.ShellCopy(SourcePaths.ToArray(), DestinationFolder, _ownerHandle, _renameOnCollision, recordOperation: false);
    }

    public override void Undo()
    {
        // Delete the copied files (using the actual recorded paths)
        FileSystemService.ShellDelete(CreatedPaths.ToArray(), _ownerHandle, recordOperation: false);
    }
}

/// <summary>
/// Represents a rename operation
/// </summary>
public class RenameOperation : FileOperation
{
    public string Directory { get; private set; }
    public string OldName { get; private set; }
    public string NewName { get; private set; }
    private IntPtr _ownerHandle;

    public RenameOperation(string directory, string oldName, string newName, IntPtr ownerHandle)
    {
        Directory = directory;
        OldName = oldName;
        NewName = newName;
        _ownerHandle = ownerHandle;
    }

    public override string GetDescription()
    {
        return $"Rename {OldName} to {NewName}";
    }

    public override void Redo()
    {
        // Rename from old to new
        string oldPath = Path.Combine(Directory, OldName);
        FileSystemService.ShellRename(oldPath, NewName, _ownerHandle, recordOperation: false);
    }

    public override void Undo()
    {
        // Rename from new back to old
        string newPath = Path.Combine(Directory, NewName);
        FileSystemService.ShellRename(newPath, OldName, _ownerHandle, recordOperation: false);
    }
}

/// <summary>
/// Represents a file creation operation
/// </summary>
public class CreateFileOperation : FileOperation
{
    public string FilePath { get; private set; }

    public CreateFileOperation(string filePath)
    {
        FilePath = filePath;
    }

    public override string GetDescription()
    {
        return $"Create file {Path.GetFileName(FilePath)}";
    }

    public override void Redo()
    {
        // Cannot easily redo content creation without storing content, 
        // but for now we assume file exists or we don't support redo after undo fully 
        // unless we store content. For simpler "Undo" (delete), it's fine.
        // If we want robust Redo, we need content.
        // Let's assume for this version, Redo is limited or we just don't delete on Undo?
        // Standard undo for "Create" is "Delete". Redo is "Restore from Recycle Bin" if we deleted it.
        // So we can use Shell logic.
    }

    public override void Undo()
    {
        // Delete the created file
        if (File.Exists(FilePath))
        {
            FileSystemService.ShellDelete(new[] { FilePath }, IntPtr.Zero, recordOperation: false);
        }
    }
}

/// <summary>
/// Represents a tagging operation
/// </summary>
public class TagOperation : FileOperation
{
    private readonly List<string> _paths;
    private readonly List<string> _tagsToAdd;
    private readonly Dictionary<string, HashSet<string>> _previousTags = new();

    public TagOperation(IEnumerable<string> paths, IEnumerable<string> tags)
    {
        _paths = paths.ToList();
        _tagsToAdd = tags.ToList();
        
        // Capture state for undo
        foreach (var path in _paths)
        {
            _previousTags[path] = TagManager.Instance.GetTags(path);
        }
    }

    public override string GetDescription()
    {
        return $"Tag {_paths.Count} items with {string.Join(", ", _tagsToAdd)}";
    }

    public override void Redo()
    {
        TagManager.Instance.UpdateTagsBatch(_paths, _tagsToAdd, Enumerable.Empty<string>());
    }

    public override void Undo()
    {
        foreach (var path in _paths)
        {
            if (_previousTags.TryGetValue(path, out var tags))
            {
                TagManager.Instance.SetTags(path, tags);
            }
        }
    }
}

/// <summary>
/// Represents a batch of operations (e.g., from LLM) that are undone/redone together
/// </summary>
public class BatchOperation : FileOperation
{
    public List<FileOperation> Operations { get; private set; }
    public string BatchDescription { get; private set; }

    public BatchOperation(List<FileOperation> operations, string description = "LLM Actions")
    {
        Operations = new List<FileOperation>(operations);
        BatchDescription = description;
    }

    public override string GetDescription()
    {
        if (Operations.Count == 1)
            return Operations[0].GetDescription();
        return $"{BatchDescription} ({Operations.Count} actions)";
    }

    public override void Undo()
    {
        // Undo in reverse order
        for (int i = Operations.Count - 1; i >= 0; i--)
        {
            Operations[i].Undo();
        }
    }

    public override void Redo()
    {
        // Redo in original order
        foreach (var op in Operations)
        {
            op.Redo();
        }
    }
}
