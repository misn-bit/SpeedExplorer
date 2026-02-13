using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class SelectionOpenController
    {
        private readonly MainForm _owner;

        public SelectionOpenController(MainForm owner)
        {
            _owner = owner;
        }

        public void TogglePinSelected()
        {
            string path = GetSelectedPath();
            if (string.IsNullOrEmpty(path))
            {
                if (!string.IsNullOrEmpty(_owner._currentPath) && _owner._currentPath != ThisPcPath && !_owner.IsSearchMode)
                    path = _owner._currentPath;
                else
                    return;
            }

            var pinned = AppSettings.Current.PinnedPaths;
            if (pinned.Contains(path))
                pinned.Remove(path);
            else
                pinned.Add(path);

            AppSettings.Current.Save();
            _owner._sidebarController.PopulateSidebar();
        }

        public string GetSelectedPath()
        {
            var active = GetActiveSelectionContainer();
            if (active == _owner._sidebar)
            {
                var path = _owner._sidebar?.SelectedNode?.Tag as string;
                return (path == SidebarSeparatorTag) ? string.Empty : (path ?? string.Empty);
            }
            if (active == _owner._listView)
            {
                if (_owner._listView?.SelectedIndices.Count == 1)
                {
                    int selectedIndex = _owner._listView.SelectedIndices[0];
                    if (selectedIndex >= 0 && selectedIndex < _owner._items.Count)
                        return _owner._items[selectedIndex].FullPath;
                }
            }
            return string.Empty;
        }

        public string[] GetSelectedPaths()
        {
            var active = GetActiveSelectionContainer();
            if (active == _owner._sidebar)
            {
                string path = _owner._sidebar?.SelectedNode?.Tag as string ?? "";
                return string.IsNullOrEmpty(path) || path == SidebarSeparatorTag ? Array.Empty<string>() : new[] { path };
            }
            if (active == _owner._listView)
            {
                var paths = new System.Collections.Generic.List<string>();
                foreach (int index in _owner._listView!.SelectedIndices)
                {
                    if (index >= 0 && index < _owner._items.Count)
                        paths.Add(_owner._items[index].FullPath);
                }
                return paths.ToArray();
            }
            return Array.Empty<string>();
        }

        private Control? GetActiveSelectionContainer()
        {
            // 1. If a control is focused, it wins.
            if (_owner._sidebar != null && _owner._sidebar.Focused) return _owner._sidebar;
            if (_owner._listView != null && _owner._listView.Focused) return _owner._listView;

            // 2. If the context menu is open, use its source.
            if (_owner._contextMenu != null && _owner._contextMenu.Visible)
                return _owner._contextMenu.SourceControl;

            // 3. Fallback: if list view has selection, assume it's the target even if not strictly focused.
            if (_owner._listView != null && _owner._listView.SelectedIndices.Count > 0)
                return _owner._listView;

            return null;
        }

        public void OpenSelectedItem()
        {
            string path = GetSelectedPath();
            if (string.IsNullOrEmpty(path)) return;

            var selectedItem = _owner._items.FirstOrDefault(i => i.FullPath == path);
            if (selectedItem != null && selectedItem.IsShellItem)
            {
                if (selectedItem.IsDirectory)
                    _owner.ObserveTask(_owner.NavigateTo(selectedItem.FullPath), "SelectionOpen.OpenFolder");
                else
                    _owner.OpenShellPath(selectedItem.FullPath);
                return;
            }

            if (Directory.Exists(path))
            {
                _owner.ObserveTask(_owner.NavigateTo(path), "SelectionOpen.OpenShellOrPath");
            }
            else if (FileSystemService.IsImageFile(path) && AppSettings.Current.UseBuiltInImageViewer)
            {
                var imageFiles = _owner._items
                    .Where(x => !x.IsDirectory && FileSystemService.IsImageFile(x.FullPath))
                    .Select(x => x.FullPath)
                    .ToList();

                if (!imageFiles.Contains(path))
                    imageFiles.Insert(0, path);

                if (imageFiles.Count == 0) return;

                var startIndex = imageFiles.IndexOf(path);
                if (startIndex < 0) startIndex = 0;

                var viewer = new ImageViewerForm(imageFiles, startIndex);
                viewer.Show();
            }
            else
            {
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
