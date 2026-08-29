using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpeedExplorer;

public partial class MainForm
{
    string INavigationHost.CurrentPath => State.CurrentPath;
    string INavigationHost.ThisPcPath => ThisPcPath;
    bool INavigationHost.IsShellPath(string path) => IsShellPath(path);
    void INavigationHost.ClearCurrentPathForHistory() => State.CurrentPath = "";
    string? INavigationHost.GetShellParentPath(string shellPath) => GetShellParentPath(shellPath);
    Task INavigationHost.NavigateTo(string path) => NavigateTo(path);
    Task INavigationHost.RefreshCurrentAsync(List<string>? selectPaths) => RefreshCurrentAsync(selectPaths);
    void INavigationHost.ObserveTask(Task task, string source) => ObserveTask(task, source);
}
