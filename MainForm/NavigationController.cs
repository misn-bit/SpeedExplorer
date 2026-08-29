using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SpeedExplorer;

// Step 1 extraction: move navigation *state* and simple navigation commands out of MainForm.
// NavigateTo(...) remains on MainForm for now, but it reads/writes state from this controller.
internal sealed class NavigationController
{
    private readonly INavigationHost _host;

    public Stack<string> BackHistory { get; set; } = new();
    public Stack<string> ForwardHistory { get; set; } = new();
    public Dictionary<string, string> LastSelection { get; set; } = new();
    public Dictionary<string, (SortColumn Column, SortDirection Direction)> FolderSortSettings { get; set; } = new();
    public Dictionary<string, int> FolderIconSizeOverrides { get; set; } = new();

    public bool IsNavigating { get; set; }
    public string? PendingPath { get; private set; }
    public List<string>? PendingSelectPaths { get; private set; }

    public NavigationController(INavigationHost host)
    {
        _host = host;
    }

    public void QueuePending(string path, List<string>? selectPaths)
    {
        PendingPath = path;
        PendingSelectPaths = selectPaths;
    }

    public (string? path, List<string>? selectPaths) DequeuePending()
    {
        var p = PendingPath;
        var s = PendingSelectPaths;
        PendingPath = null;
        PendingSelectPaths = null;
        return (p, s);
    }

    public void GoBack()
    {
        if (BackHistory.Count <= 0) return;
        ForwardHistory.Push(_host.CurrentPath);
        var prev = BackHistory.Pop();
        _host.ClearCurrentPathForHistory();
        _host.ObserveTask(_host.NavigateTo(prev), "NavController.GoBack");
    }

    public void GoForward()
    {
        if (ForwardHistory.Count <= 0) return;
        BackHistory.Push(_host.CurrentPath);
        var next = ForwardHistory.Pop();
        _host.ClearCurrentPathForHistory();
        _host.ObserveTask(_host.NavigateTo(next), "NavController.GoForward");
    }

    public void GoUp()
    {
        var path = _host.CurrentPath;
        if (string.IsNullOrEmpty(path) || path == _host.ThisPcPath) return;

        if (_host.IsShellPath(path))
        {
            var shellParent = _host.GetShellParentPath(path);
            _host.ObserveTask(_host.NavigateTo(shellParent ?? _host.ThisPcPath), "NavController.GoUpShell");
            return;
        }

        var parent = Directory.GetParent(path);
        _host.ObserveTask(_host.NavigateTo(parent != null ? parent.FullName : _host.ThisPcPath), "NavController.GoUp");
    }

    public async Task RefreshCurrentAsync(List<string>? selectPaths = null)
    {
        await _host.RefreshCurrentAsync(selectPaths);
    }
}
