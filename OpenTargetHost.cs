using System.Collections.Generic;

namespace SpeedExplorer;

internal interface IOpenTargetHost
{
    BrowserState BrowserState { get; }
    bool IsSearchMode { get; }
    Stack<string> BackHistory { get; }
    Stack<string> ForwardHistory { get; }

    string GetSelectedPath();
    string? GetShellParentPath(string shellPath);
    void OpenPathInNewTab(
        string path,
        bool activate,
        Stack<string>? inheritedBackHistory = null,
        Stack<string>? inheritedForwardHistory = null);
}
