using System;
using System.Windows.Forms;

namespace SpeedExplorer;

internal interface ISearchHost
{
    BrowserState BrowserState { get; }
    ListView FileListView { get; }
    ToolStripStatusLabel StatusLabel { get; }
    bool IsDisposed { get; }
    bool Disposing { get; }
    bool IsHandleCreated { get; }

    void BeginInvoke(Action action);
    void Invoke(Action action);
    void SetupDriveColumns(ListView listView);
    void SetupFileColumns(ListView listView);
    void UpdateActiveTabTitle();
    void RefreshSearchOverlayVisibility();
    void ResetListViewportTopAsync(int preferredIndex, string reason);
    void LogListViewState(string scope, string stage);
    void InvalidateListItem(int index);
}
