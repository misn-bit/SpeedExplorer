using System;
using System.Windows.Forms;

namespace SpeedExplorer;

internal interface ISearchHost
{
    BrowserState BrowserState { get; }
    string ActiveTabId { get; }
    ListView FileListView { get; }
    string SearchText { get; }
    ToolStripStatusLabel StatusLabel { get; }
    ToolStripStatusLabel SearchSpinnerLabel { get; }
    bool IsDisposed { get; }
    bool Disposing { get; }
    bool IsHandleCreated { get; }

    void BeginInvoke(Action action);
    void Invoke(Action action);
    void SetupDriveColumns(ListView listView);
    void SetupFileColumns(ListView listView);
    void InvalidatePendingSearchRestore();
    void UpdateActiveTabTitle();
    void RefreshTabTitle(string tabId);
    void ResetListViewportTopAsync(int preferredIndex, string reason);
    void LogListViewState(string scope, string stage);
    void InvalidateListItem(int index);
}
