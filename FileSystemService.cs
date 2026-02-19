using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SpeedExplorer;

public enum SortColumn { Name, Size, DateModified, DateCreated, Type, Location, Tags, Format, FreeSpace, DriveNumber }
public enum SortDirection { Ascending, Descending }

public class FileItem
{
    public string FullPath { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime DateModified { get; set; }
    public DateTime DateCreated { get; set; }
    public string Extension { get; set; } = "";
    public bool IsShellItem { get; set; }
    public string DisplayPath { get; set; } = "";
    public string ShellParentId { get; set; } = "";
    
    // Drive specific
    public int DriveNumber { get; set; }
    public long FreeSpace { get; set; }
    public string DriveFormat { get; set; } = "";
    public string DriveType { get; set; } = "";

    public string SizeDisplay => IsDirectory ? "" : FormatSize(Size);
    public string TypeDisplay => IsDirectory ? "Folder" : (string.IsNullOrEmpty(Extension) ? "File" : Extension.TrimStart('.').ToUpperInvariant());

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}

public class FileSystemService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif"
    };

    public static bool IsImageFile(string path)
    {
        return ImageExtensions.Contains(Path.GetExtension(path));
    }

    public static bool IsAccessible(string path)
    {
        try
        {
            // A simple way to check access without enumerating everything
            // Just try to get one item
            Directory.EnumerateFileSystemEntries(path).Any();
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
        catch { return false; }
    }

    public static async Task<List<FileItem>> GetFilesAsync(string path, CancellationToken ct = default)
    {
        return await Task.Factory.StartNew(() =>
        {
            var items = new List<FileItem>();
            var dirInfo = new DirectoryInfo(path);

            if (!dirInfo.Exists)
                throw new DirectoryNotFoundException($"Directory not found: {path}");

            // Get directories first
            try
            {
                foreach (var dir in dirInfo.EnumerateDirectories())
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        items.Add(new FileItem
                        {
                            FullPath = dir.FullName,
                            Name = dir.Name,
                            IsDirectory = true,
                            DateModified = dir.LastWriteTime,
                            DateCreated = dir.CreationTime,
                            Extension = ""
                        });
                    }
                    catch { /* Skip inaccessible individual item */ }
                }
            }
            catch (UnauthorizedAccessException) 
            { 
                throw; 
            }

            // Get files
            try
            {
                foreach (var file in dirInfo.EnumerateFiles())
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        items.Add(new FileItem
                        {
                            FullPath = file.FullName,
                            Name = file.Name,
                            IsDirectory = false,
                            Size = file.Length,
                            DateModified = file.LastWriteTime,
                            DateCreated = file.CreationTime,
                            Extension = file.Extension
                        });
                    }
                    catch { /* Skip inaccessible individual item */ }
                }
            }
            catch (UnauthorizedAccessException) { throw; }

            return items;
        }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public static async Task<List<FileItem>> SearchFilesRecursiveAsync(
        string rootPath, 
        string query, 
        IProgress<(int found, int searched)>? progress = null,
        Action<List<FileItem>>? onResultsFound = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<FileItem>();
            var lowerQuery = query.ToLowerInvariant();
            int searched = 0;

            void SearchDirectory(string path)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    
                    var dirInfo = new DirectoryInfo(path);
                    var currentBatch = new List<FileItem>();

                    // Search files in this directory
                    foreach (var file in dirInfo.EnumerateFiles())
                    {
                        ct.ThrowIfCancellationRequested();
                        searched++;
                        
                        try
                        {
                            // Check name OR tags
                            bool nameMatch = file.Name.ToLowerInvariant().Contains(lowerQuery);
                            bool tagMatch = false;
                            
                            // Check tags
                            if (!nameMatch)
                            {
                                var tags = TagManager.Instance.GetTags(file.FullName);
                                if (tags.Any(t => t.ToLowerInvariant().Contains(lowerQuery)))
                                {
                                    tagMatch = true;
                                }
                            }

                            if (nameMatch || tagMatch)
                            {
                                var item = new FileItem
                                {
                                    FullPath = file.FullName,
                                    Name = file.Name,
                                    IsDirectory = false,
                                    Size = file.Length,
                                    DateModified = file.LastWriteTime,
                                    DateCreated = file.CreationTime,
                                    Extension = file.Extension
                                };
                                results.Add(item);
                                currentBatch.Add(item);
                                
                                // Batch results for efficiency
                                if (currentBatch.Count >= 50)
                                {
                                    onResultsFound?.Invoke(new List<FileItem>(currentBatch));
                                    currentBatch.Clear();
                                }

                                // Report progress every 100 files or on each match
                                if (results.Count % 10 == 0)
                                    progress?.Report((results.Count, searched));
                            }

                            if (searched % 100 == 0)
                                progress?.Report((results.Count, searched));
                        }
                        catch { /* Skip inaccessible */ }
                    }

                    // Flush batch after files in this dir
                    if (currentBatch.Count > 0)
                    {
                        onResultsFound?.Invoke(new List<FileItem>(currentBatch));
                        currentBatch.Clear();
                    }

                    // Search subdirectories
                    foreach (var dir in dirInfo.EnumerateDirectories())
                    {
                        ct.ThrowIfCancellationRequested();
                        searched++;
                        
                        try
                        {
                            // Check if directory name matches OR tags
                            bool nameMatch = dir.Name.ToLowerInvariant().Contains(lowerQuery);
                            bool tagMatch = false;
                            
                            if (!nameMatch)
                            {
                                var tags = TagManager.Instance.GetTags(dir.FullName);
                                if (tags.Any(t => t.ToLowerInvariant().Contains(lowerQuery)))
                                {
                                    tagMatch = true;
                                }
                            }

                            if (nameMatch || tagMatch)
                            {
                                var item = new FileItem
                                {
                                    FullPath = dir.FullName,
                                    Name = dir.Name,
                                    IsDirectory = true,
                                    DateModified = dir.LastWriteTime,
                                    DateCreated = dir.CreationTime,
                                    Extension = ""
                                };
                                results.Add(item);
                                currentBatch.Add(item);
                                
                                // Batch results
                                if (currentBatch.Count >= 50)
                                {
                                    onResultsFound?.Invoke(new List<FileItem>(currentBatch));
                                    currentBatch.Clear();
                                }
                            }
                            
                            // Recurse into subdirectory
                            SearchDirectory(dir.FullName);

                            if (searched % 100 == 0)
                                progress?.Report((results.Count, searched));
                        }
                        catch { /* Skip inaccessible */ }
                    }

                    // Final flush for this dir
                    if (currentBatch.Count > 0)
                    {
                        onResultsFound?.Invoke(new List<FileItem>(currentBatch));
                        currentBatch.Clear();
                    }
                }
                catch (UnauthorizedAccessException) { /* Skip */ }
                catch (OperationCanceledException) { throw; }
                catch { /* Skip other errors */ }
            }

            SearchDirectory(rootPath);
            progress?.Report((results.Count, searched));
            
            return results;
        }, ct);
    }

    public static async Task<List<FileItem>> SearchTagsAsync(
        string rootPath, 
        string query, 
        Action<List<FileItem>>? onResultsFound = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<FileItem>();
            var paths = TagManager.Instance.GetPathsWithTag(rootPath, query);
            var currentBatch = new List<FileItem>();

            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    FileItem? item = null;
                    if (File.Exists(path))
                    {
                        var info = new FileInfo(path);
                        item = new FileItem
                        {
                            FullPath = info.FullName,
                            Name = info.Name,
                            IsDirectory = false,
                            Size = info.Length,
                            DateModified = info.LastWriteTime,
                            DateCreated = info.CreationTime,
                            Extension = info.Extension
                        };
                    }
                    else if (Directory.Exists(path))
                    {
                        var info = new DirectoryInfo(path);
                        item = new FileItem
                        {
                            FullPath = info.FullName,
                            Name = info.Name,
                            IsDirectory = true,
                            DateModified = info.LastWriteTime,
                            DateCreated = info.CreationTime,
                            Extension = ""
                        };
                    }

                    if (item != null)
                    {
                        results.Add(item);
                        currentBatch.Add(item);

                        if (currentBatch.Count >= 50)
                        {
                            onResultsFound?.Invoke(new List<FileItem>(currentBatch));
                            currentBatch.Clear();
                        }
                    }
                }
                catch { /* Skip items that might have been deleted/moved outside */ }
            }

            if (currentBatch.Count > 0)
            {
                onResultsFound?.Invoke(new List<FileItem>(currentBatch));
            }

            return results;
        }, ct);
    }

    public static void SortItems(List<FileItem> items, SortColumn column, SortDirection direction, bool taggedOnTop = false)
    {
        if (items == null || items.Count <= 1)
            return;

        // Always keep directories at top
        var dirs = new List<FileItem>(items.Count);
        var files = new List<FileItem>(items.Count);
        foreach (var item in items)
        {
            if (item.IsDirectory)
                dirs.Add(item);
            else
                files.Add(item);
        }

        // Sort directories (always standard sort)
        SortList(dirs, column, direction);

        if (taggedOnTop)
        {
            // Partition files into Tagged and Untagged
            var taggedFiles = new List<FileItem>(files.Count);
            var untaggedFiles = new List<FileItem>(files.Count);
            foreach (var file in files)
            {
                if (!file.IsShellItem && TagManager.Instance.HasTags(file.FullPath))
                    taggedFiles.Add(file);
                else
                    untaggedFiles.Add(file);
            }

            // Sort each group independently using the selected column
            SortList(taggedFiles, column, direction);
            SortList(untaggedFiles, column, direction);

            // Recombine: Tagged First, then Untagged
            files.Clear();
            files.AddRange(taggedFiles);
            files.AddRange(untaggedFiles);
        }
        else
        {
            // Standard sort for all files
            SortList(files, column, direction);
        }

        items.Clear();
        items.AddRange(dirs);
        items.AddRange(files);
    }

    private static void SortList(List<FileItem> items, SortColumn column, SortDirection direction)
    {
        Dictionary<FileItem, string>? tagSortKeys = null;
        if (column == SortColumn.Tags && items.Count > 0)
        {
            tagSortKeys = new Dictionary<FileItem, string>(items.Count);
            foreach (var item in items)
            {
                string key = item.IsShellItem ? "" : TagManager.Instance.GetPrimaryTagForSort(item.FullPath);
                tagSortKeys[item] = key;
            }
        }

        Comparison<FileItem> comparison = column switch
        {
            SortColumn.Name => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            SortColumn.Size => (a, b) => a.Size.CompareTo(b.Size),
            SortColumn.DateModified => (a, b) => a.DateModified.CompareTo(b.DateModified),
            SortColumn.DateCreated => (a, b) => a.DateCreated.CompareTo(b.DateCreated),
            SortColumn.Type => (a, b) => string.Compare(a.Extension, b.Extension, StringComparison.OrdinalIgnoreCase),
            SortColumn.Tags => (a, b) =>
            {
                // Precomputed keys avoid repeated path normalization + tag set allocations.
                string tagA = tagSortKeys != null && tagSortKeys.TryGetValue(a, out var v1) ? v1 : "";
                string tagB = tagSortKeys != null && tagSortKeys.TryGetValue(b, out var v2) ? v2 : "";
                int byTag = string.Compare(tagA, tagB, StringComparison.OrdinalIgnoreCase);
                if (byTag != 0) return byTag;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            },
            SortColumn.Format => (a, b) => string.Compare(a.DriveFormat, b.DriveFormat, StringComparison.OrdinalIgnoreCase),
            SortColumn.FreeSpace => (a, b) => a.FreeSpace.CompareTo(b.FreeSpace),
            SortColumn.DriveNumber => (a, b) => a.DriveNumber.CompareTo(b.DriveNumber),
            _ => (a, b) => 0
        };

        items.Sort(comparison);

        if (direction == SortDirection.Descending)
            items.Reverse();
    }

    public static List<DriveInfo> GetDrives()
    {
        return DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .ToList();
    }

    // --- Shell API for Safe File Operations ---

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 8)]
    public struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public IntPtr pFrom; // Must be double-null terminated list of strings
        public IntPtr pTo;   // Must be double-null terminated list of strings
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted; // Win32 BOOL (4 bytes)
        public IntPtr hNameMappings;
        public IntPtr lpszProgressTitle;
    }

    private const uint FO_MOVE = 0x0001;
    private const uint FO_COPY = 0x0002;
    private const uint FO_DELETE = 0x0003;
    private const uint FO_RENAME = 0x0004;

    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_RENAMEONCOLLISION = 0x0008;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHNAMEMAPPING
    {
        public IntPtr pszOldPath; // string
        public IntPtr pszNewPath; // string
        public int cchOldPath;
        public int cchNewPath;
    }

    [DllImport("shell32.dll")]
    public static extern void SHFreeNameMappings(IntPtr hNameMappings);

    private const ushort FOF_WANTMAPPINGHANDLE = 0x0020;

    private const uint SHCNE_ALLEVENTS = 0x7FFFFFFF;
    private const uint SHCNF_FLUSH = 0x1000;

    public static List<string> ShellCopy(string[] sourcePaths, string destinationFolder, IntPtr ownerHandle, bool renameOnCollision = false, bool recordOperation = true)
    {
        var actualPaths = ExecuteShellOp(FO_COPY, sourcePaths, destinationFolder, ownerHandle, renameOnCollision);
        
        if (recordOperation && actualPaths.Any())
        {
            var operation = new CopyOperation(sourcePaths, destinationFolder, actualPaths, ownerHandle, renameOnCollision);
            UndoRedoManager.Instance.RecordOperation(operation);
        }

        // Handle Tags Copy
        if (actualPaths.Count == sourcePaths.Length)
        {
            for (int i = 0; i < sourcePaths.Length; i++)
            {
                TagManager.Instance.CopyTags(sourcePaths[i], actualPaths[i]);
            }
        }

        return actualPaths;
    }

    public static List<string> ShellMove(string[] sourcePaths, string destinationFolder, IntPtr ownerHandle, bool renameOnCollision = false, bool recordOperation = true)
    {
        var actualPaths = ExecuteShellOp(FO_MOVE, sourcePaths, destinationFolder, ownerHandle, renameOnCollision);
        
        if (recordOperation && actualPaths.Any())
        {
            var operation = new MoveOperation(sourcePaths, destinationFolder, actualPaths, ownerHandle);
            UndoRedoManager.Instance.RecordOperation(operation);
        }

        // Handle Tags Move
        if (actualPaths.Count == sourcePaths.Length)
        {
            for (int i = 0; i < sourcePaths.Length; i++)
            {
                TagManager.Instance.HandleRename(sourcePaths[i], actualPaths[i]);
            }
        }

        return actualPaths;
    }

    public static void ShellRename(string sourcePath, string newName, IntPtr ownerHandle, bool recordOperation = true)
    {
        string dir = Path.GetDirectoryName(sourcePath) ?? "";
        string oldName = Path.GetFileName(sourcePath) ?? "";
        string targetPath = Path.Combine(dir, newName);
        
        ExecuteShellOp(FO_RENAME, new[] { sourcePath }, targetPath, ownerHandle, false);

        if (recordOperation)
        {
            var operation = new RenameOperation(dir, oldName, newName, ownerHandle);
            UndoRedoManager.Instance.RecordOperation(operation);
        }
    }

    public static void ShellDelete(string[] sourcePaths, IntPtr ownerHandle, bool recordOperation = true, bool permanent = false)
    {
        ExecuteShellOp(FO_DELETE, sourcePaths, null, ownerHandle, false, permanent);

        if (recordOperation)
        {
            // If permanent, we can't really undo it easily (data loss), so ideally we shouldn't record it as an undoable op.
            // However, the standard UndoRedoManager expects operations. 
            // For now, if permanent, we skip recording because we can't undo it.
            if (!permanent)
            {
                var operation = new DeleteOperation(sourcePaths, ownerHandle);
                UndoRedoManager.Instance.RecordOperation(operation);
            }
        }
    }

    /// <summary>
    /// Restores files from the Recycle Bin to their original locations
    /// </summary>
    public static void RestoreFromRecycleBin(string[] originalPaths, IntPtr ownerHandle)
    {
        try
        {
            var pathsToRestore = new HashSet<string>(originalPaths, StringComparer.OrdinalIgnoreCase);
            var restoredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) throw new Exception("Shell.Application not found");

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic recycleBin = shell.NameSpace(10); // ssfBITBUCKET = 10
            if (recycleBin == null) throw new Exception("Recycle Bin not accessible");

            // Find the "Original Location" column index
            // This index can vary depending on OS version and local settings
            int originalLocationIndex = -1;
            for (int i = 0; i < 40; i++)
            {
                string header = recycleBin.GetDetailsOf(null, i);
                if (string.IsNullOrEmpty(header)) continue;

                if (header.Contains("Original Location", StringComparison.OrdinalIgnoreCase) ||
                    header.Contains("Исходное расположение", StringComparison.OrdinalIgnoreCase) ||
                    header.Contains("Original") || header.Contains("расположение"))
                {
                    originalLocationIndex = i;
                    break;
                }
            }

            if (originalLocationIndex == -1) originalLocationIndex = 1; // Common fallback

            // We iterate through items and find matches
            // Using a list to avoid collection modification issues if any
            var items = recycleBin.Items();
            var matches = new List<dynamic>();

            foreach (dynamic item in items)
            {
                string path = recycleBin.GetDetailsOf(item, originalLocationIndex);
                if (pathsToRestore.Contains(path))
                {
                    matches.Add(item);
                }
            }

            foreach (dynamic item in matches)
            {
                string path = recycleBin.GetDetailsOf(item, originalLocationIndex);
                foreach (dynamic verb in item.Verbs())
                {
                    string verbName = verb.Name.Replace("&", "");
                    if (verbName.Equals("Restore", StringComparison.OrdinalIgnoreCase) ||
                        verbName.Equals("Восстановить", StringComparison.OrdinalIgnoreCase))
                    {
                        verb.DoIt();
                        restoredPaths.Add(path);
                        break;
                    }
                }
            }

            // Check what failed
            var failed = pathsToRestore.Where(p => !restoredPaths.Contains(p)).ToList();
            if (failed.Any())
            {
                ShowManualRestoreMessage(failed.ToArray());
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Restore error: {ex.Message}");
            ShowManualRestoreMessage(originalPaths);
        }
    }

    private static void ShowManualRestoreMessage(string[] paths)
    {
        string message = "Unable to automatically restore some items from the Recycle Bin.\n\n";
        message += "Please manually restore these items from the Recycle Bin:\n\n";
        foreach (var path in paths.Take(5))
        {
            message += $"• {Path.GetFileName(path)}\n";
        }
        if (paths.Length > 5)
        {
            message += $"• ... and {paths.Length - 5} more";
        }

        System.Windows.Forms.MessageBox.Show(message, "Undo Delete",
            System.Windows.Forms.MessageBoxButtons.OK,
            System.Windows.Forms.MessageBoxIcon.Information);
    }

    public static void PerformUndo()
    {
        UndoRedoManager.Instance.Undo();
    }

    public static void PerformRedo()
    {
        UndoRedoManager.Instance.Redo();
    }

    // Diagnostic method - can be removed later
    public static void ShellUndo()
    {
        // No-op
    }

    /// <summary>
    /// Executes shell operation and returns list of actual destination paths
    /// </summary>
    private static List<string> ExecuteShellOp(uint func, string[] sourcePaths, string? destFolder, IntPtr ownerHandle, bool renameOnCollision, bool permanent = false)
    {
        // Marshalling strings manually to ensure double-null termination
        // pFrom and pTo must be list of strings, each null-terminated, ending with an extra null
        string from = string.Join("\0", sourcePaths) + "\0\0";
        string? to = destFolder != null ? (destFolder + "\0\0") : null;

        IntPtr pFrom = Marshal.StringToHGlobalUni(from);
        IntPtr pTo = to != null ? Marshal.StringToHGlobalUni(to) : IntPtr.Zero;

        List<string> resultingPaths = new List<string>();

        // Pre-calculate default destination paths (if no rename happens)
        if (destFolder != null)
        {
            foreach (var src in sourcePaths)
            {
                resultingPaths.Add(Path.Combine(destFolder, Path.GetFileName(src)));
            }
        }

        try
        {
            var op = new SHFILEOPSTRUCT
            {
                hwnd = ownerHandle,
                wFunc = func,
                pFrom = pFrom,
                pTo = pTo,
                fFlags = FOF_WANTMAPPINGHANDLE
            };

            // FOF_ALLOWUNDO moves to recycle bin. Omit it for permanent delete.
            if (!permanent)
            {
                op.fFlags |= FOF_ALLOWUNDO;
            }

            // Important: We NEVER set FOF_NOCONFIRMATION for permanent delete to ensure safety dialog.
            // For Recycle Bin operations, we usually rely on system defaults or could suppress if desired, 
            // but standard Explorer behavior is to show dialogs unless user disabled them globally or we explicitly suppress.
            // Our previous code had FOF_ALLOWUNDO | FOF_WANTMAPPINGHANDLE, meaning it might show dialogs depending on global settings.

            if (renameOnCollision)
                op.fFlags |= FOF_RENAMEONCOLLISION;

            int result = SHFileOperation(ref op);
            
            // If success (0), notify the system to refresh caches
            if (result == 0 && !op.fAnyOperationsAborted)
            {
                SHChangeNotify(SHCNE_ALLEVENTS, SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);

                // Check for renames
                if (op.hNameMappings != IntPtr.Zero)
                {
                    try 
                    {
                        int numMappings = Marshal.ReadInt32(op.hNameMappings);
                        IntPtr arrayPtr = Marshal.ReadIntPtr(op.hNameMappings, IntPtr.Size); 
                        
                        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        for (int i = 0; i < numMappings; i++)
                        {
                            IntPtr itemPtr = IntPtr.Add(arrayPtr, i * Marshal.SizeOf(typeof(SHNAMEMAPPING)));
                            SHNAMEMAPPING mapping = Marshal.PtrToStructure<SHNAMEMAPPING>(itemPtr);
                            
                            var oldPath = Marshal.PtrToStringUni(mapping.pszOldPath, mapping.cchOldPath);
                            var newPath = Marshal.PtrToStringUni(mapping.pszNewPath, mapping.cchNewPath);

                            if (!string.IsNullOrEmpty(oldPath) && !string.IsNullOrEmpty(newPath))
                                map[oldPath] = newPath;
                        }

                        // Update resultingPaths based on map
                        for (int i = 0; i < sourcePaths.Length; i++)
                        {
                            if (map.TryGetValue(sourcePaths[i], out var actualNewPath) &&
                                !string.IsNullOrEmpty(actualNewPath))
                            {
                                resultingPaths[i] = actualNewPath;
                            }
                        }
                    }
                    finally
                    {
                        SHFreeNameMappings(op.hNameMappings);
                    }
                }
            }
            else
            {
                // If failed or aborted, return empty to avoid recording partial ops
                resultingPaths.Clear();
            }
        }
        finally
        {
            if (pFrom != IntPtr.Zero) Marshal.FreeHGlobal(pFrom);
            if (pTo != IntPtr.Zero) Marshal.FreeHGlobal(pTo);
        }

        return resultingPaths;
    }
}
