using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpeedExplorer;

internal interface INavigationHost
{
    string CurrentPath { get; }
    string ThisPcPath { get; }

    bool IsShellPath(string path);
    void ClearCurrentPathForHistory();
    string? GetShellParentPath(string shellPath);
    Task NavigateTo(string path);
    Task RefreshCurrentAsync(List<string>? selectPaths);
    void ObserveTask(Task task, string source);
}
