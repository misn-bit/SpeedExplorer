using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SpeedExplorer;

// Step 1 extraction: move navigation *state* and simple navigation commands out of MainForm.
// NavigateTo(...) remains on MainForm for now, but it reads/writes state from this controller.
internal sealed class NavigationController
{
    private readonly MainForm _owner;

    public Stack<string> BackHistory { get; set; } = new();
    public Stack<string> ForwardHistory { get; set; } = new();
    public Dictionary<string, string> LastSelection { get; set; } = new();
    public Dictionary<string, (SortColumn Column, SortDirection Direction)> FolderSortSettings { get; set; } = new();

    public bool IsNavigating { get; set; }
    public string? PendingPath { get; private set; }
    public List<string>? PendingSelectPaths { get; private set; }

    public NavigationController(MainForm owner)
    {
        _owner = owner;
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
        ForwardHistory.Push(_owner.CurrentPathForNav);
        var prev = BackHistory.Pop();
        _owner.ClearCurrentPathForHistory();
        _owner.ObserveTask(_owner.NavigateTo(prev), "NavController.GoBack");
    }

    public void GoForward()
    {
        if (ForwardHistory.Count <= 0) return;
        BackHistory.Push(_owner.CurrentPathForNav);
        var next = ForwardHistory.Pop();
        _owner.ClearCurrentPathForHistory();
        _owner.ObserveTask(_owner.NavigateTo(next), "NavController.GoForward");
    }

    public void GoUp()
    {
        var path = _owner.CurrentPathForNav;
        if (string.IsNullOrEmpty(path) || path == MainForm.ThisPcPathConst) return;

        if (MainForm.IsShellPathStatic(path))
        {
            var shellParent = _owner.GetShellParentPath(path);
            _owner.ObserveTask(_owner.NavigateTo(shellParent ?? MainForm.ThisPcPathConst), "NavController.GoUpShell");
            return;
        }

        var parent = Directory.GetParent(path);
        _owner.ObserveTask(_owner.NavigateTo(parent != null ? parent.FullName : MainForm.ThisPcPathConst), "NavController.GoUp");
    }

    public async Task RefreshCurrentAsync(List<string>? selectPaths = null)
    {
        await _owner.RefreshCurrentAsync(selectPaths);
    }
}
