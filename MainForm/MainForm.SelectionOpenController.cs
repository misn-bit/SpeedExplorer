using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    BrowserState ISelectionOpenHost.BrowserState => State;
    bool ISelectionOpenHost.IsSearchMode => IsSearchMode;
    TreeView ISelectionOpenHost.Sidebar => _sidebar;
    ListView ISelectionOpenHost.FileListView => _listView;
    ContextMenuStrip ISelectionOpenHost.ContextMenu => _contextMenu;
    void ISelectionOpenHost.PopulateSidebar() => _sidebarController.PopulateSidebar();
    void ISelectionOpenHost.ObserveTask(Task task, string source) => ObserveTask(task, source);
    Task ISelectionOpenHost.NavigateTo(string path) => NavigateTo(path);
    void ISelectionOpenHost.OpenShellPath(string path) => OpenShellPath(path);
    bool ISelectionOpenHost.TryOpenImageViewerForImagePath(string imagePath, IEnumerable<string> preferredImagePool)
        => TryOpenImageViewerForImagePath(imagePath, preferredImagePool);

    private sealed class SelectionOpenController
    {
        private readonly ISelectionOpenHost _host;
        private BrowserState State => _host.BrowserState;

        public SelectionOpenController(ISelectionOpenHost host)
        {
            _host = host;
        }

        public void TogglePinSelected()
        {
            string path = GetSelectedPath();
            if (string.IsNullOrEmpty(path))
            {
            if (!string.IsNullOrEmpty(State.CurrentPath) && State.CurrentPath != ThisPcPath && !_host.IsSearchMode)
                path = State.CurrentPath;
                else
                    return;
            }

            var pinned = AppSettings.Current.PinnedPaths;
            if (pinned.Contains(path))
                pinned.Remove(path);
            else
                pinned.Add(path);

            AppSettings.Current.Save();
            _host.PopulateSidebar();
        }

        public string GetSelectedPath()
        {
            var active = GetActiveSelectionContainer();
            if (active == _host.Sidebar)
            {
                var path = _host.Sidebar?.SelectedNode?.Tag as string;
                return (path == SidebarSeparatorTag) ? string.Empty : (path ?? string.Empty);
            }
            if (active == _host.FileListView)
            {
                if (_host.FileListView?.SelectedIndices.Count == 1)
                {
                    int selectedIndex = _host.FileListView.SelectedIndices[0];
            if (selectedIndex >= 0 && selectedIndex < State.Items.Count)
                return State.Items[selectedIndex].FullPath;
                }
            }
            return string.Empty;
        }

        public string[] GetSelectedPaths()
        {
            var active = GetActiveSelectionContainer();
            if (active == _host.Sidebar)
            {
                string path = _host.Sidebar?.SelectedNode?.Tag as string ?? "";
                return string.IsNullOrEmpty(path) || path == SidebarSeparatorTag ? Array.Empty<string>() : new[] { path };
            }
            if (active == _host.FileListView)
            {
                var paths = new System.Collections.Generic.List<string>();
                foreach (int index in _host.FileListView!.SelectedIndices)
                {
            if (index >= 0 && index < State.Items.Count)
                paths.Add(State.Items[index].FullPath);
                }
                return paths.ToArray();
            }
            return Array.Empty<string>();
        }

        private Control? GetActiveSelectionContainer()
        {
            // 1. If a control is focused, it wins.
            if (_host.Sidebar != null && _host.Sidebar.Focused) return _host.Sidebar;
            if (_host.FileListView != null && _host.FileListView.Focused) return _host.FileListView;

            // 2. If the context menu is open, use its source.
            if (_host.ContextMenu != null && _host.ContextMenu.Visible)
                return _host.ContextMenu.SourceControl;

            // 3. Fallback: if list view has selection, assume it's the target even if not strictly focused.
            if (_host.FileListView != null && _host.FileListView.SelectedIndices.Count > 0)
                return _host.FileListView;

            return null;
        }

        public void OpenSelectedItem()
        {
            string path = GetSelectedPath();
            if (string.IsNullOrEmpty(path)) return;

            var selectedItem = State.Items.FirstOrDefault(i => i.FullPath == path);
            if (selectedItem != null && selectedItem.IsShellItem)
            {
                if (selectedItem.IsDirectory)
                    _host.ObserveTask(_host.NavigateTo(selectedItem.FullPath), "SelectionOpen.OpenFolder");
                else
                    _host.OpenShellPath(selectedItem.FullPath);
                return;
            }

            if (Directory.Exists(path))
            {
                _host.ObserveTask(_host.NavigateTo(path), "SelectionOpen.OpenShellOrPath");
                return;
            }
            else if (FileSystemService.IsImageFile(path) && AppSettings.Current.UseBuiltInImageViewer)
            {
            if (_host.TryOpenImageViewerForImagePath(path, State.Items.Select(static x => x.FullPath)))
                    return;
            }

            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
