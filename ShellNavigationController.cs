using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SpeedExplorer;

internal sealed class ShellNavigationController
{
    private const string ShellPrefix = "SHELL::";
    private const string ShellCompositeSeparator = "|#|";
    private const string ShellIdPrefix = "SHELLID::";

    private readonly string _thisPcPath;
    private readonly Dictionary<string, object> _shellItemMap = new();
    private readonly Dictionary<string, string> _shellParentMap = new();

    public ShellNavigationController(string thisPcPath)
    {
        _thisPcPath = thisPcPath;
    }

    public static bool IsShellPath(string? path)
    {
        return !string.IsNullOrEmpty(path) &&
               (path.StartsWith(ShellPrefix, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(ShellIdPrefix, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsShellIdPath(string? path)
    {
        return !string.IsNullOrEmpty(path) && path.StartsWith(ShellIdPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private string RegisterShellItemInternal(object item, string? parentShellId)
    {
        var id = $"{ShellIdPrefix}{Guid.NewGuid():N}";
        _shellItemMap[id] = item;
        if (!string.IsNullOrEmpty(parentShellId))
            _shellParentMap[id] = parentShellId;
        else
            _shellParentMap[id] = _thisPcPath;
        return id;
    }

    public string RegisterShellItem(object item, string? parentShellId)
    {
        return RegisterShellItemInternal(item, parentShellId);
    }

    private bool TryGetShellItem(string shellId, out object? item)
    {
        return _shellItemMap.TryGetValue(shellId, out item);
    }

    private static bool IsCompositeShellPath(string shellPath)
    {
        return shellPath.Contains(ShellCompositeSeparator, StringComparison.Ordinal);
    }

    private static string ToShellPath(string rawPath)
    {
        return $"{ShellPrefix}{rawPath}";
    }

    private static string FromShellPath(string shellPath)
    {
        if (shellPath.StartsWith(ShellPrefix, StringComparison.OrdinalIgnoreCase))
            return shellPath.Substring(ShellPrefix.Length);
        return shellPath;
    }

    private static bool TryParseCompositeShellPath(string shellPath, out string parentRaw, out string name)
    {
        parentRaw = "";
        name = "";
        if (!IsShellPath(shellPath)) return false;

        var raw = FromShellPath(shellPath);
        var idx = raw.IndexOf(ShellCompositeSeparator, StringComparison.Ordinal);
        if (idx <= 0) return false;

        parentRaw = raw.Substring(0, idx);
        name = raw.Substring(idx + ShellCompositeSeparator.Length);
        return !string.IsNullOrEmpty(parentRaw) && !string.IsNullOrEmpty(name);
    }

    public string GetShellDisplayName(string shellPath)
    {
        try
        {
            if (IsShellIdPath(shellPath) && TryGetShellItem(shellPath, out var shellObj))
            {
                try
                {
                    if (shellObj == null) return "Portable Device";
                    dynamic d = shellObj;
                    string itemName = d.Name;
                    if (!string.IsNullOrEmpty(itemName)) return itemName;
                }
                catch { }
            }
            else if (IsShellIdPath(shellPath))
            {
                return "Portable Device";
            }

            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return "Portable Device";

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) return "Portable Device";
            if (TryParseCompositeShellPath(shellPath, out var parentRaw, out var name))
            {
                object? parentObj = shell.NameSpace(parentRaw);
                if (parentObj != null)
                {
                    dynamic parent = parentObj;
                    object? itemObj = parent.ParseName(name);
                    if (itemObj != null)
                    {
                        dynamic item = itemObj;
                        return item.Name as string ?? name;
                    }
                }
            }
            else
            {
                dynamic folder = ResolveShellFolder(shell, FromShellPath(shellPath));
                if (folder != null)
                {
                    object? selfObj = null;
                    try { selfObj = folder.Self; } catch { }
                    if (selfObj != null)
                    {
                        dynamic self = selfObj;
                        return self.Name as string ?? "Portable Device";
                    }
                }
            }
        }
        catch { }

        return "Portable Device";
    }

    public string? GetShellParentPath(string shellPath)
    {
        try
        {
            if (IsShellIdPath(shellPath) && _shellParentMap.TryGetValue(shellPath, out var parentId))
                return parentId;
            if (IsShellIdPath(shellPath))
                return _thisPcPath;

            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return null;

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) return null;
            if (TryParseCompositeShellPath(shellPath, out var parentRaw, out _))
            {
                // For composite paths, parent is the raw parent itself.
                return ToShellPath(parentRaw);
            }
            else
            {
                dynamic folder = ResolveShellFolder(shell, FromShellPath(shellPath));
                if (folder == null) return null;

                object? parentObj = null;
                try { parentObj = folder.ParentFolder; } catch { }
                if (parentObj != null)
                {
                    dynamic parent = parentObj;
                    object? selfObj = null;
                    try { selfObj = parent.Self; } catch { }
                    if (selfObj != null)
                    {
                        dynamic self = selfObj;
                        string parentPath = self.Path as string ?? "";
                        if (!string.IsNullOrEmpty(parentPath))
                            return ToShellPath(parentPath);
                    }
                }
            }
        }
        catch { }

        return null;
    }

    private static dynamic? ResolveShellFolder(dynamic shell, string rawPath)
    {
        try
        {
            if (rawPath.StartsWith(ShellIdPrefix, StringComparison.OrdinalIgnoreCase))
                return null;

            bool debugShell = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_shell.txt"));
            string debugShellLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_shell.log");

            dynamic folder = shell.NameSpace(rawPath);
            if (folder != null)
            {
                if (debugShell)
                {
                    var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Shell] Resolve NameSpace OK: {rawPath}";
                    Debug.WriteLine(msg);
                    try { File.AppendAllText(debugShellLog, msg + Environment.NewLine); } catch { }
                }
                return folder;
            }
            if (debugShell)
            {
                var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Shell] Resolve NameSpace NULL: {rawPath}";
                Debug.WriteLine(msg);
                try { File.AppendAllText(debugShellLog, msg + Environment.NewLine); } catch { }
            }

            // Fallback: search "This PC" items by exact Path.
            dynamic drives = shell.NameSpace(0x11); // CSIDL_DRIVES
            if (drives != null)
            {
                foreach (var driveItem in drives.Items())
                {
                    try
                    {
                        string path = driveItem.Path;
                        if (!string.IsNullOrEmpty(path) && string.Equals(path, rawPath, StringComparison.OrdinalIgnoreCase))
                        {
                            if (debugShell)
                            {
                                var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Shell] Resolve Drives Match: {rawPath}";
                                Debug.WriteLine(msg);
                                try { File.AppendAllText(debugShellLog, msg + Environment.NewLine); } catch { }
                            }
                            return driveItem.GetFolder;
                        }
                    }
                    catch { }
                }
            }
            if (debugShell)
            {
                var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Shell] Resolve Drives NoMatch: {rawPath}";
                Debug.WriteLine(msg);
                try { File.AppendAllText(debugShellLog, msg + Environment.NewLine); } catch { }
            }

            // Fallback: resolve via Desktop ParseName for shell-only paths.
            dynamic desktop = shell.NameSpace(0);
            if (desktop == null) return null;

            dynamic desktopItem = desktop.ParseName(rawPath);
            if (desktopItem == null) return null;

            if (debugShell)
            {
                var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Shell] Resolve Desktop Parse OK: {rawPath}";
                Debug.WriteLine(msg);
                try { File.AppendAllText(debugShellLog, msg + Environment.NewLine); } catch { }
            }
            return desktopItem.GetFolder;
        }
        catch
        {
            return null;
        }
    }

    private List<FileItem> EnumerateShellItemsOnCurrentThread(string shellPath, CancellationToken ct, string displayPath, bool debugShell, string debugShellLog)
    {
        var items = new List<FileItem>();
        try
        {
            ct.ThrowIfCancellationRequested();

            if (!IsShellIdPath(shellPath) || !TryGetShellItem(shellPath, out var shellObj))
                return items;

            dynamic? folder = null;
            try
            {
                if (shellObj == null) return items;
                dynamic d = shellObj;
                try { folder = d.Folder; } catch { }
                if (folder == null)
                {
                    try { folder = d.GetFolder; } catch { }
                }
            }
            catch { }

            if (folder == null)
            {
                if (debugShell)
                {
                    var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Shell] Folder null for {shellPath}";
                    Debug.WriteLine(msg);
                    try { File.AppendAllText(debugShellLog, msg + Environment.NewLine); } catch { }
                }
                return items;
            }

            int count = 0;
            foreach (var item in folder.Items())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    string itemName = item.Name;
                    string path = item.Path;
                    if (debugShell && count < 5)
                    {
                        var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Shell] Item: {itemName} Path: {path} IsFolder: {item.IsFolder}";
                        Debug.WriteLine(msg);
                        try { File.AppendAllText(debugShellLog, msg + Environment.NewLine); } catch { }
                    }
                    if (string.IsNullOrEmpty(itemName)) continue;

                    if (string.IsNullOrEmpty(path))
                        path = itemName;

                    bool isFolder = item.IsFolder;
                    string fullPath = RegisterShellItemInternal(item, shellPath);

                    items.Add(new FileItem
                    {
                        FullPath = fullPath,
                        Name = itemName,
                        IsDirectory = isFolder,
                        Extension = Path.GetExtension(itemName),
                        IsShellItem = true,
                        ShellParentId = shellPath,
                        DisplayPath = displayPath,
                        DateModified = DateTime.MinValue,
                        DateCreated = DateTime.MinValue
                    });
                    count++;
                }
                catch { }
            }

            if (debugShell)
            {
                var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Shell] Items count: {items.Count}";
                Debug.WriteLine(msg);
                try { File.AppendAllText(debugShellLog, msg + Environment.NewLine); } catch { }
            }
        }
        catch (OperationCanceledException)
        {
            return new List<FileItem>();
        }
        catch
        {
            return new List<FileItem>();
        }

        return items;
    }

    public Task<List<FileItem>> GetShellItemsAsync(string shellPath, CancellationToken ct, string displayPath)
    {
        var tcs = new TaskCompletionSource<List<FileItem>>();
        var debugShell = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_shell.txt"));
        var debugShellLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_shell.log");

        if (IsShellIdPath(shellPath))
        {
            if (debugShell)
            {
                var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Shell] Navigate: {shellPath}";
                Debug.WriteLine(msg);
                try { File.AppendAllText(debugShellLog, msg + Environment.NewLine); } catch { }
            }

            var result = EnumerateShellItemsOnCurrentThread(shellPath, ct, displayPath, debugShell, debugShellLog);
            tcs.SetResult(result);
            return tcs.Task;
        }

        var thread = new Thread(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var items = new List<FileItem>();

                var shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null)
                {
                    tcs.SetResult(items);
                    return;
                }

                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell == null)
                {
                    tcs.SetResult(items);
                    return;
                }
                dynamic? folder = null;
                string parentRawPath = "";
                string? parentShellId = IsShellIdPath(shellPath) ? shellPath : null;
                if (debugShell)
                {
                    var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Shell] Navigate: {shellPath}";
                    Debug.WriteLine(msg);
                    try { File.AppendAllText(debugShellLog, msg + Environment.NewLine); } catch { }
                }

                if (IsShellIdPath(shellPath))
                {
                    if (TryGetShellItem(shellPath, out var shellObj))
                    {
                        try
                        {
                            if (shellObj == null)
                            {
                                tcs.SetResult(items);
                                return;
                            }
                            dynamic d = shellObj;
                            try { folder = d.Folder; } catch { }

                            if (folder == null)
                            {
                                try { folder = d.GetFolder; } catch { }
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        tcs.SetResult(items);
                        return;
                    }
                }
                else if (TryParseCompositeShellPath(shellPath, out var parentRaw, out var name))
                {
                    parentRawPath = parentRaw;
                    dynamic parentFolder = ResolveShellFolder(shell, parentRaw);
                    if (debugShell)
                    {
                        var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Shell] Parent: {parentRaw} Name: {name} ParentFolder: {(parentFolder != null)}";
                        Debug.WriteLine(msg);
                        try { File.AppendAllText(debugShellLog, msg + Environment.NewLine); } catch { }
                    }
                    if (parentFolder != null)
                    {
                        dynamic childItem = parentFolder.ParseName(name);
                        if (debugShell)
                        {
                            var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Shell] ChildItem: {(childItem != null)}";
                            Debug.WriteLine(msg);
                            try { File.AppendAllText(debugShellLog, msg + Environment.NewLine); } catch { }
                        }
                        if (childItem != null)
                            folder = childItem.GetFolder;
                    }
                }
                else
                {
                    folder = ResolveShellFolder(shell, FromShellPath(shellPath));
                    if (debugShell)
                    {
                        var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Shell] NameSpace: {FromShellPath(shellPath)} Folder: {(folder != null)}";
                        Debug.WriteLine(msg);
                        try { File.AppendAllText(debugShellLog, msg + Environment.NewLine); } catch { }
                    }
                }

                if (folder == null)
                {
                    if (debugShell)
                    {
                        var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Shell] Folder null for {shellPath}";
                        Debug.WriteLine(msg);
                        try { File.AppendAllText(debugShellLog, msg + Environment.NewLine); } catch { }
                    }
                    tcs.SetResult(items);
                    return;
                }

                try
                {
                    if (string.IsNullOrEmpty(parentRawPath))
                    {
                        if (folder.Self != null)
                            parentRawPath = folder.Self.Path as string ?? "";

                        if (string.IsNullOrEmpty(parentRawPath) && !IsCompositeShellPath(shellPath))
                            parentRawPath = FromShellPath(shellPath);
                    }
                }
                catch { }

                int count = 0;
                foreach (var item in folder.Items())
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        string itemName = item.Name;
                        string path = item.Path;
                        if (debugShell && count < 5)
                        {
                            var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Shell] Item: {itemName} Path: {path} IsFolder: {item.IsFolder}";
                            Debug.WriteLine(msg);
                            try { File.AppendAllText(debugShellLog, msg + Environment.NewLine); } catch { }
                        }
                        if (string.IsNullOrEmpty(itemName)) continue;
                        if (string.IsNullOrEmpty(path))
                        {
                            if (!string.IsNullOrEmpty(parentRawPath))
                                path = $"{parentRawPath}{ShellCompositeSeparator}{itemName}";
                            else
                                path = itemName;
                        }

                        bool isFolder = item.IsFolder;
                        string fullPath = IsShellIdPath(shellPath)
                            ? RegisterShellItemInternal(item, parentShellId)
                            : ToShellPath(path);

                        items.Add(new FileItem
                        {
                            FullPath = fullPath,
                            Name = itemName,
                            IsDirectory = isFolder,
                            Extension = Path.GetExtension(itemName),
                            IsShellItem = true,
                            ShellParentId = parentShellId ?? "",
                            DisplayPath = displayPath,
                            DateModified = DateTime.MinValue,
                            DateCreated = DateTime.MinValue
                        });
                        count++;
                    }
                    catch { }
                }

                if (debugShell)
                {
                    var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Shell] Items count: {items.Count}";
                    Debug.WriteLine(msg);
                    try { File.AppendAllText(debugShellLog, msg + Environment.NewLine); } catch { }
                }
                tcs.SetResult(items);
            }
            catch (OperationCanceledException)
            {
                tcs.SetCanceled(ct);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        return tcs.Task;
    }

    public void OpenShellPath(string shellPath)
    {
        try
        {
            if (IsShellIdPath(shellPath) && TryGetShellItem(shellPath, out var shellObj))
            {
                if (shellObj == null) return;
                dynamic d = shellObj;
                d.InvokeVerb("open");
                return;
            }

            if (TryParseCompositeShellPath(shellPath, out var parentRaw, out var name))
            {
                var shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return;
                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell == null) return;
                dynamic parent = ResolveShellFolder(shell, parentRaw);
                if (parent == null) return;
                dynamic item = parent.ParseName(name);
                if (item == null) return;
                item.InvokeVerb("open");
            }
            else
            {
                Process.Start(new ProcessStartInfo("explorer.exe", FromShellPath(shellPath)) { UseShellExecute = true });
            }
        }
        catch { }
    }
}
