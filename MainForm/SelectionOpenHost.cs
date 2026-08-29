using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpeedExplorer;

internal interface ISelectionOpenHost
{
    BrowserState BrowserState { get; }
    bool IsSearchMode { get; }
    TreeView Sidebar { get; }
    ListView FileListView { get; }
    ContextMenuStrip ContextMenu { get; }

    void PopulateSidebar();
    void ObserveTask(Task task, string source);
    Task NavigateTo(string path);
    void OpenShellPath(string path);
    bool TryOpenImageViewerForImagePath(string imagePath, IEnumerable<string> preferredImagePool);
}
