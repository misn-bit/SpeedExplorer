using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpeedExplorer;

internal interface IFileOperationsHost
{
    BrowserState BrowserState { get; }
    ListView FileListView { get; }
    TextBox? RenameTextBox { get; set; }
    IntPtr WindowHandle { get; }
    int EffectiveIconSize { get; }

    string[] GetSelectedPaths();
    Task RefreshCurrentAsync(List<string>? selectPaths = null);
    void ApplyMoveToCachedSnapshots(IEnumerable<string> sourcePaths);
    void ShowStatusMessage(string message);
}
