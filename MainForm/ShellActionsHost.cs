namespace SpeedExplorer;

internal interface IShellActionsHost
{
    BrowserState BrowserState { get; }

    string GetSelectedPath();
    string[] GetSelectedPaths();
    void OpenShellPath(string path);
    void SetStatusMessage(string message);
}
