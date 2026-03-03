using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private void ToggleSidebar()
    {
        AppSettings.Current.ShowSidebar = !AppSettings.Current.ShowSidebar;
        AppSettings.Current.Save();
        _splitContainer.Panel1Collapsed = !AppSettings.Current.ShowSidebar;
    }

    private bool CanManipulateSelected()
    {
        if (string.IsNullOrEmpty(_currentPath)) return false;
        if (IsShellPath(_currentPath)) return false;

        // Block manipulation in This PC if not searching (drives view)
        if (!IsSearchMode && _currentPath == ThisPcPath) return false;

        // If sidebar is focused on a drive or common folder, block
        if (_sidebar.Focused && _sidebar.SelectedNode != null)
        {
            var nodeText = _sidebar.SelectedNode.Text;
            var tag = _sidebar.SelectedNode.Tag as string;
            if (string.IsNullOrEmpty(tag) || tag.Length <= 3 || nodeText.StartsWith("Disk") || nodeText.Contains(" (") || _sidebar.SelectedNode.Parent == null)
                return false;
        }

        // Explicitly block drives even if they appear in list (e.g. search anomalies or general protection)
        if (_listView != null && _listView.SelectedIndices.Count > 0)
        {
            foreach (int index in _listView.SelectedIndices)
            {
                if (index >= 0 && index < _items.Count)
                {
                    var item = _items[index];
                    if (item.IsShellItem) return false;
                    // Any item that looks like a drive root should be protected
                    if (!string.IsNullOrEmpty(item.DriveFormat) || item.Extension == ".drive" || item.Extension == ".usb" || (item.FullPath.Length <= 3 && item.FullPath.EndsWith(":\\")))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private void SelectItems(List<string> paths)
    {
        if (paths == null || paths.Count == 0) return;

        _listView.SelectedIndices.Clear();

        bool firstFound = false;

        // Create a hashset for fast lookup of exact paths
        var pathSet = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        bool hasRootedInput = paths.Any(static p => !string.IsNullOrWhiteSpace(p) && Path.IsPathRooted(p));

        // Keep filename fallback only for non-rooted requests to avoid cross-folder false matches.
        var nameSet = new HashSet<string>(
            paths
                .Select(Path.GetFileName)
                .Where(static n => !string.IsNullOrEmpty(n))
                .Select(static n => n!),
            StringComparer.OrdinalIgnoreCase);

        // Because VirtualMode is on, we iterate indices and check cached value if possible
        // But _allItems is our data source
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];

            // Check full path first
            bool matches = pathSet.Contains(item.FullPath);
            if (!matches && !hasRootedInput)
                matches = nameSet.Contains(item.Name);
            if (matches)
            {
                _listView.SelectedIndices.Add(i);
                if (!firstFound)
                {
                    _listView.EnsureVisible(i);
                    _listView.FocusedItem = _listView.Items[i]; // Virtual mode: this accesses the item via retriever
                    firstFound = true;
                }
            }
        }

        if (firstFound)
        {
            if (_renameTextBox == null || _renameTextBox.IsDisposed)
                _listView.Focus();
        }
    }

    private void SelectAll()
    {
        if (_listView == null || _items.Count == 0) return;
        _listView.BeginUpdate();
        try
        {
            _listView.SelectedIndices.Clear();
            for (int i = 0; i < _items.Count; i++)
            {
                _listView.SelectedIndices.Add(i);
            }
        }
        finally
        {
            _listView.EndUpdate();
        }
    }

    private void EditTags()
    {
        var paths = GetSelectedPaths().ToList();
        if (paths.Count == 0)
        {
            // If no selection, apply to current path
            if (!string.IsNullOrEmpty(_currentPath) && _currentPath != ThisPcPath && !IsSearchMode)
                paths.Add(_currentPath);
            else
                return;
        }

        HashSet<string>? commonTags = null;

        foreach (var path in paths)
        {
            var tags = TagManager.Instance.GetTags(path);
            if (commonTags == null)
            {
                commonTags = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                commonTags.IntersectWith(tags);
            }
        }

        string initialTagsStr = commonTags != null ? string.Join(", ", commonTags) : "";
        var initialCommonSet = commonTags ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var dlg = new EditTagsForm(initialTagsStr);
        if (paths.Count > 1)
        {
            dlg.Text = string.Format(Localization.T("edit_tags_multi"), paths.Count);
        }

        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            var finalTags = dlg.TagsResult.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                           .Select(t => t.Trim())
                                           .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (dlg.ClearAllRequested)
            {
                // Full replacement (effectively clearing everything else)
                TagManager.Instance.SetTagsBatch(paths, finalTags);
            }
            else
            {
                // Differential logic for partial updates across multiple files
                var toAdd = finalTags.Where(t => !initialCommonSet.Contains(t)).ToList();
                var toRemove = initialCommonSet.Where(t => !finalTags.Contains(t)).ToList();
                TagManager.Instance.UpdateTagsBatch(paths, toAdd, toRemove);
            }
            _ = RefreshCurrentAsync();
        }
    }

    private string GenerateUniqueName(string parentPath, string baseName, string extension)
    {
        string name = baseName + extension;
        string fullPath = Path.Combine(parentPath, name);
        int counter = 2;

        while (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            name = $"{baseName} ({counter}){extension}";
            fullPath = Path.Combine(parentPath, name);
            counter++;
        }
        return name;
    }

    private void CreateNewFolder()
    {
        if (string.IsNullOrEmpty(_currentPath) || _currentPath == ThisPcPath) return;

        try
        {
            string name = GenerateUniqueName(_currentPath, "New Folder", "");
            string fullPath = Path.Combine(_currentPath, name);
            Directory.CreateDirectory(fullPath);
            StartRenameAfterCreation(fullPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to create folder: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CreateNewTextFile()
    {
        if (string.IsNullOrEmpty(_currentPath) || _currentPath == ThisPcPath) return;

        try
        {
            string name = GenerateUniqueName(_currentPath, "New Text File", ".txt");
            string fullPath = Path.Combine(_currentPath, name);
            File.WriteAllText(fullPath, "");
            StartRenameAfterCreation(fullPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to create file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
