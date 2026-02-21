using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class ListViewInteractionController
    {
        private readonly MainForm _owner;
        private bool _virtualRepairPending;

        public ListViewInteractionController(MainForm owner)
        {
            _owner = owner;
        }

        public void RetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
        {
            _ = sender;
            try
            {
                if (_owner._searchController.TryBuildProgressVirtualItem(e.ItemIndex, out var progressItem))
                {
                    e.Item = progressItem;
                    return;
                }

                if (e.ItemIndex >= 0 && e.ItemIndex < _owner._items.Count)
                {
                    var item = _owner._items[e.ItemIndex];
                    e.Item = BuildListViewItem(item, includeSubItems: true);
                }
                else
                {
                    // Index out of bounds should never happen in steady state.
                    // Self-heal VirtualListSize and return a safe fallback item for this frame.
                    _owner.LogListViewState("RVI", $"oob idx={e.ItemIndex} count={_owner._items.Count} vsize={_owner._listView.VirtualListSize}");
                    QueueVirtualListRepair($"RetrieveVirtualItem oob idx={e.ItemIndex} count={_owner._items.Count} vsize={_owner._listView.VirtualListSize}");
                    if (_owner._items.Count > 0)
                    {
                        int safeIndex = Math.Min(Math.Max(e.ItemIndex, 0), _owner._items.Count - 1);
                        e.Item = BuildListViewItem(_owner._items[safeIndex], includeSubItems: true);
                    }
                    else
                    {
                        e.Item = new ListViewItem("");
                    }
                }
            }
            catch (Exception ex)
            {
                // Fallback for corrupted item.
                e.Item = new ListViewItem("Error") { Tag = null };
                System.Diagnostics.Debug.WriteLine($"RetrieveVirtualItem error: {ex.Message}");
            }
        }

        private void QueueVirtualListRepair(string reason)
        {
            if (_virtualRepairPending)
                return;
            _virtualRepairPending = true;
            try
            {
                _owner.BeginInvoke((Action)(() =>
                {
                    _virtualRepairPending = false;
                    try
                    {
                        if (_owner._listView == null || _owner._listView.IsDisposed || !_owner._listView.IsHandleCreated)
                            return;
                        if (!_owner._listView.VirtualMode)
                            return;

                        int target = _owner._items.Count;
                        if (_owner._listView.VirtualListSize == target)
                            return;

                        _owner._listView.BeginUpdate();
                        try
                        {
                            _owner._listView.VirtualListSize = 0;
                            _owner._listView.VirtualListSize = target;
                        }
                        finally
                        {
                            _owner._listView.EndUpdate();
                        }
                        _owner._listView.Invalidate();
                        _owner._listView.Update();
                        System.Diagnostics.Debug.WriteLine($"ListView virtual repair: {reason} -> {target}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"ListView virtual repair failed: {ex.Message}");
                    }
                }));
            }
            catch
            {
                _virtualRepairPending = false;
            }
        }

        public void ColumnClick(object? sender, ColumnClickEventArgs e)
        {
            _ = sender;
            SortColumn? newColumn;

            if (_owner._currentPath == ThisPcPath && !_owner.IsSearchMode)
            {
                newColumn = e.Column switch
                {
                    0 => SortColumn.DriveNumber,
                    1 => SortColumn.Name,
                    2 => SortColumn.Type,
                    3 => SortColumn.Format,
                    4 => SortColumn.Size,
                    5 => SortColumn.Size, // Sorting Capacity bar sorts by Size
                    6 => SortColumn.FreeSpace,
                    _ => null
                };
            }
            else
            {
                newColumn = e.Column switch
                {
                    0 => SortColumn.Name,
                    1 => SortColumn.Location,
                    2 => SortColumn.Size,
                    3 => SortColumn.DateModified,
                    4 => SortColumn.DateCreated,
                    5 => SortColumn.Type,
                    6 => SortColumn.Tags,
                    _ => null
                };
            }

            if (newColumn == null)
                return;

            // Special tag cycling logic for files.
            if (_owner._currentPath != ThisPcPath && newColumn == SortColumn.Tags)
            {
                if (!_owner._taggedFilesOnTop)
                {
                    _owner._taggedFilesOnTop = true;
                    _owner._sortColumn = SortColumn.Tags;
                    _owner._sortDirection = SortDirection.Ascending;
                }
                else if (_owner._sortColumn == SortColumn.Tags)
                {
                    if (_owner._sortDirection == SortDirection.Ascending)
                    {
                        _owner._sortDirection = SortDirection.Descending;
                    }
                    else
                    {
                        _owner._taggedFilesOnTop = false;
                        _owner._sortColumn = SortColumn.Name;
                        _owner._sortDirection = SortDirection.Ascending;
                    }
                }
                else
                {
                    _owner._sortColumn = SortColumn.Tags;
                    _owner._sortDirection = SortDirection.Ascending;
                }
            }
            else
            {
                if (newColumn == _owner._sortColumn)
                {
                    _owner._sortDirection = _owner._sortDirection == SortDirection.Ascending
                        ? SortDirection.Descending
                        : SortDirection.Ascending;
                }
                else
                {
                    _owner._sortColumn = newColumn.Value;
                    _owner._sortDirection = SortDirection.Ascending;
                }
            }

            // Persist current folder sort immediately so it survives app close without navigation.
            if (!string.IsNullOrWhiteSpace(_owner._currentPath) &&
                _owner._currentPath != ThisPcPath &&
                !ShellNavigationController.IsShellPath(_owner._currentPath))
            {
                _owner._nav.FolderSortSettings[_owner._currentPath] = (_owner._sortColumn, _owner._sortDirection);
                _owner.SaveFolderSettings();
            }

            SortAndRefresh();
        }

        public void MouseDoubleClick(object? sender, MouseEventArgs e)
        {
            _ = sender;
            _ = e;
            _owner.OpenSelectedItem();
        }

        public void MouseDown(object? sender, MouseEventArgs e)
        {
            _ = sender;
            if (e.Button == MouseButtons.Middle)
            {
                var hit = _owner._listView.HitTest(e.Location);
                if (hit.Item != null && hit.Item.Tag is FileItem fi)
                {
                    // Search mode: middle-click opens item's location (file => parent folder, folder => itself).
                    if (_owner.IsSearchMode)
                    {
                        string? targetPath = fi.IsDirectory ? fi.FullPath : Path.GetDirectoryName(fi.FullPath);
                        if (string.IsNullOrWhiteSpace(targetPath))
                            return;

                        if (FileSystemService.IsAccessible(targetPath))
                        {
                            _owner._openTargetController.OpenPathByMiddleClickPreference(targetPath, activateTab: false);
                        }
                        else
                        {
                            _owner._statusLabel.Text = string.Format(Localization.T("status_access_denied"), targetPath);
                        }
                        return;
                    }

                    // Non-search mode: preserve existing behavior (folders only).
                    if (fi.IsDirectory)
                    {
                        if (FileSystemService.IsAccessible(fi.FullPath))
                        {
                            _owner._openTargetController.OpenPathByMiddleClickPreference(fi.FullPath, activateTab: false);
                        }
                        else
                        {
                            _owner._statusLabel.Text = string.Format(Localization.T("status_access_denied"), fi.FullPath);
                        }
                    }
                }
            }
            else if (e.Button == MouseButtons.Left)
            {
                // Native ListView marquee selection handles drag-select.
            }
        }

        public ListViewItem BuildListViewItem(FileItem item, bool includeSubItems)
        {
            var s = AppSettings.Current;

            string imageKey = "";
            string displayName = item.Name;
            string? pendingUniqueKey = null;
            string? pendingResolvedKey = null;

            if (s.ShowIcons)
            {
                if (s.UseEmojiIcons)
                {
                    // Emoji mode: use text prefixes like sidebar.
                    string emoji = item.IsDirectory ? "📁 " :
                                   FileSystemService.IsImageFile(item.IsShellItem ? item.Name : item.FullPath) ? "🖼️ " : "📄 ";
                    displayName = emoji + item.Name;
                    imageKey = "_emoji_"; // Special marker to skip icon space in DrawSubItem.
                }
                else
                {
                    if (item.IsShellItem)
                    {
                        imageKey = item.IsDirectory ? "folder" : "file";
                    }
                    else
                    {
                        // Image icon mode.
                        bool colored = s.UseSystemIcons;
                        bool isExeOrLnk = item.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                                          item.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                                          item.Extension.Equals(".url", StringComparison.OrdinalIgnoreCase) ||
                                          item.Extension.Equals(".ico", StringComparison.OrdinalIgnoreCase);
                        // At large icon sizes prefer unique per-item extraction for all files
                        // so we can fetch higher-quality shell icons.
                        bool preferHighQualityLarge = s.IconSize >= 64;
                        bool unique = (s.ResolveUniqueIcons && isExeOrLnk) || preferHighQualityLarge;

                        string prefix = colored ? "sys_" : "gray_";
                        bool isImage = FileSystemService.IsImageFile(item.IsShellItem ? item.Name : item.FullPath);
                        bool hasExtension = !string.IsNullOrWhiteSpace(item.Extension);
                        string effectiveExt = hasExtension ? item.Extension : ".noext";
                        string extLookup = hasExtension ? item.Extension : "file";

                        // Use full path as key if unique icons are forced OR if it's an image and previews are on.
                        if (unique || (isImage && s.ShowThumbnails))
                        {
                            string uniqueKey = item.FullPath;
                            string genericKey = item.IsDirectory
                                ? $"{prefix}folder"
                                : (isImage ? $"{prefix}image" : $"{prefix}{effectiveExt}");
                            imageKey = uniqueKey;

                            if (!_owner._smallIcons.Images.ContainsKey(uniqueKey))
                            {
                                // Queue generic placeholder asynchronously (non-blocking for UI).
                                _owner._iconLoadService?.EnsureGenericIcon(genericKey, extLookup, item.IsDirectory, colored);
                                // Queue async load for unique icon/thumbnail.
                                if (!_owner.IsTileView || _owner._tileViewController.ShouldQueueUniqueIconNow(uniqueKey))
                                    _owner._iconLoadService?.QueueIconLoad(item.FullPath, item.IsDirectory, colored);

                                // Use already loaded generic icon if available, otherwise fallback immediately.
                                if (_owner._smallIcons.Images.ContainsKey(genericKey))
                                {
                                    imageKey = genericKey;
                                }
                                else
                                {
                                    imageKey = item.IsDirectory ? "folder" : (isImage ? "image" : "file");
                                    pendingResolvedKey = genericKey;
                                }
                                pendingUniqueKey = uniqueKey;
                            }
                        }
                        else
                        {
                            // Generic icons by extension/folder.
                            imageKey = item.IsDirectory ? $"{prefix}folder" : $"{prefix}{effectiveExt}";

                            if (!_owner._smallIcons.Images.ContainsKey(imageKey))
                            {
                                // Use fallback placeholder immediately, load real icon async.
                                string fallbackKey = item.IsDirectory ? "folder" : "file";
                                if (_owner._smallIcons.Images.ContainsKey(fallbackKey))
                                    imageKey = fallbackKey;

                                // Queue async load for proper icon.
                                string targetKey = item.IsDirectory ? $"{prefix}folder" : $"{prefix}{effectiveExt}";
                                _owner._iconLoadService?.QueueIconLoad(targetKey, item.IsDirectory, colored, lookupPath: item.IsDirectory ? null : extLookup);
                                pendingResolvedKey = targetKey;
                            }
                        }
                    }
                }
            }

            var lvi = new ListViewItem(displayName)
            {
                Tag = item,
                ImageKey = imageKey
            };

            // In tile mode includeSubItems=false, so keep drive/usb icon assignment here too.
            if (!s.UseEmojiIcons && (item.Extension == ".drive" || item.Extension == ".usb"))
            {
                if (_owner.IsTileView)
                {
                    string driveTileKey = BuildDriveTileIconKey(item);
                    EnsureDriveTileIcon(driveTileKey, item);
                    lvi.ImageKey = _owner._smallIcons.Images.ContainsKey(driveTileKey)
                        ? driveTileKey
                        : (item.Extension == ".usb" ? "usb" : "drive");
                }
                else
                {
                    lvi.ImageKey = item.Extension == ".usb" ? "usb" : "drive";
                }
            }

            if (_owner.IsTileView && !string.IsNullOrEmpty(pendingUniqueKey))
            {
                if (_owner._smallIcons.Images.ContainsKey(pendingUniqueKey))
                    lvi.ImageKey = pendingUniqueKey;
                else
                    _owner._tileViewController.RegisterIconBinding(pendingUniqueKey, lvi);
            }

            if (_owner.IsTileView && !string.IsNullOrEmpty(pendingResolvedKey))
            {
                if (_owner._smallIcons.Images.ContainsKey(pendingResolvedKey))
                    lvi.ImageKey = pendingResolvedKey;
                else
                    _owner._tileViewController.RegisterIconBinding(pendingResolvedKey, lvi);
            }

            if (!includeSubItems)
                return lvi;

            if (item.Extension == ".drive" || item.Extension == ".usb")
            {
                // Drive Columns: №, Name, Type, Format, Size (Text), Capacity (Bar), Free Space.
                lvi.Text = item.DriveNumber > 0 ? item.DriveNumber.ToString() : "";
                lvi.SubItems.Add(item.Name);
                lvi.SubItems.Add(item.DriveType);
                lvi.SubItems.Add(item.DriveFormat);
                lvi.SubItems.Add(FileItem.FormatSize(item.Size));
                lvi.SubItems.Add(""); // Capacity Bar placeholder.
                lvi.SubItems.Add(FileItem.FormatSize(item.FreeSpace));

                lvi.ImageKey = item.Extension == ".usb" ? "usb" : "drive";
            }
            else
            {
                // File Columns: Name, Location, Size, Date Modified, Date Created, Type, Tags.
                lvi.SubItems.Add(item.IsShellItem ? item.DisplayPath : (Path.GetDirectoryName(item.FullPath) ?? ""));

                if (item.IsDirectory)
                    lvi.SubItems.Add("");
                else
                    lvi.SubItems.Add(item.SizeDisplay);

                lvi.SubItems.Add(item.DateModified == DateTime.MinValue ? "" : item.DateModified.ToString("g"));
                lvi.SubItems.Add(item.DateCreated == DateTime.MinValue ? "" : item.DateCreated.ToString("g"));
                lvi.SubItems.Add(item.TypeDisplay);

                var tagStr = "";
                if (!item.IsShellItem)
                {
                    var tags = TagManager.Instance.GetTags(item.FullPath);
                    tagStr = tags.Count > 0 ? string.Join(", ", tags) : "";
                }
                lvi.SubItems.Add(tagStr);
            }

            // Safety: ensure subitem count matches column count to prevent crash.
            while (lvi.SubItems.Count < _owner._listView.Columns.Count)
            {
                lvi.SubItems.Add("");
            }

            return lvi;
        }
        
        private static string BuildDriveTileIconKey(FileItem item)
        {
            int hash = (item.FullPath ?? "").GetHashCode(StringComparison.OrdinalIgnoreCase);
            string type = item.Extension == ".usb" ? "u" : "d";
            return $"drvbar_{type}_{hash}_{item.Size}_{item.FreeSpace}";
        }

        private void EnsureDriveTileIcon(string key, FileItem item)
        {
            if (_owner._smallIcons.Images.ContainsKey(key) && _owner._largeIcons.Images.ContainsKey(key))
                return;

            string baseKey = item.Extension == ".usb" ? "usb" : "drive";
            if (!_owner._smallIcons.Images.ContainsKey(baseKey) || !_owner._largeIcons.Images.ContainsKey(baseKey))
                return;

            try
            {
                var smallBase = _owner._smallIcons.Images[baseKey];
                var largeBase = _owner._largeIcons.Images[baseKey];
                if (smallBase == null || largeBase == null)
                    return;

                double ratio = 0;
                if (item.Size > 0)
                {
                    ratio = (double)(item.Size - item.FreeSpace) / item.Size;
                    if (ratio < 0) ratio = 0;
                    if (ratio > 1) ratio = 1;
                }

                var small = RenderDriveIconWithBar(smallBase, _owner._smallIcons.ImageSize.Width, ratio);
                var large = RenderDriveIconWithBar(largeBase, _owner._largeIcons.ImageSize.Width, ratio);

                if (!_owner._smallIcons.Images.ContainsKey(key))
                    _owner._smallIcons.Images.Add(key, small);
                else
                    small.Dispose();

                if (!_owner._largeIcons.Images.ContainsKey(key))
                    _owner._largeIcons.Images.Add(key, large);
                else
                    large.Dispose();
            }
            catch
            {
                // Best-effort icon enrichment.
            }
        }

        private static Bitmap RenderDriveIconWithBar(Image baseImage, int size, double ratio)
        {
            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(baseImage, 0, 0, size, size);

            int pad = Math.Max(1, size / 10);
            int barHeight = Math.Max(3, size / 7);
            int barWidth = Math.Max(8, size - (pad * 2));
            int x = (size - barWidth) / 2;
            int y = size - barHeight - pad;

            using var bg = new SolidBrush(Color.FromArgb(60, 60, 60));
            g.FillRectangle(bg, x, y, barWidth, barHeight);

            int fillWidth = (int)Math.Round(barWidth * ratio);
            if (ratio > 0 && fillWidth < 1) fillWidth = 1;
            if (fillWidth > barWidth) fillWidth = barWidth;

            Color fillColor = Color.LimeGreen;
            if (ratio > 0.90) fillColor = Color.Red;
            else if (ratio > 0.75) fillColor = Color.Yellow;
            using var fill = new SolidBrush(fillColor);
            g.FillRectangle(fill, x, y, fillWidth, barHeight);

            using var border = new Pen(Color.FromArgb(100, 100, 100), 1);
            g.DrawRectangle(border, x, y, barWidth - 1, barHeight - 1);

            return bmp;
        }

        public void MouseMove(object? sender, MouseEventArgs e)
        {
            _ = sender;
            try
            {
                var hit = _owner._listView.HitTest(e.Location);
                int newHover = hit.Item != null ? hit.Item.Index : -1;

                if (newHover != _owner._hoveredIndex)
                {
                    int oldHover = _owner._hoveredIndex;
                    _owner._hoveredIndex = newHover;
                    InvalidateHoverTransition(oldHover, _owner._hoveredIndex);
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                // WinForms ListView.HitTest can internally crash with index -1 in some resize/virtual scenarios.
                _owner._hoveredIndex = -1;
            }
            catch { }
        }

        public void InvalidateListItem(int index)
        {
            if (index >= 0 && index < _owner._listView.VirtualListSize)
            {
                try
                {
                    var rect = _owner._listView.GetItemRect(index, ItemBoundsPortion.Entire);
                    rect.X = 0;
                    rect.Width = _owner._listView.ClientSize.Width;
                    _owner._listView.Invalidate(rect);
                }
                catch { }
            }
        }

        public void MouseWheel(object? sender, MouseEventArgs e)
        {
            _ = sender;
            int oldHover = _owner._hoveredIndex;
            if (oldHover != -1)
            {
                _owner._hoveredIndex = -1;
                InvalidateListItem(oldHover);
            }

            // Re-evaluate hover after wheel scroll settles to avoid one-frame stale hover flicker.
            _owner.BeginInvoke((Action)(() => RefreshHoverFromCursor()));
        }

        private void RefreshHoverFromCursor()
        {
            if (_owner._listView == null || _owner._listView.IsDisposed || !_owner._listView.IsHandleCreated)
                return;

            try
            {
                var pt = _owner._listView.PointToClient(Cursor.Position);
                if (!_owner._listView.ClientRectangle.Contains(pt))
                    return;

                var hit = _owner._listView.HitTest(pt);
                int newHover = hit.Item != null ? hit.Item.Index : -1;
                if (newHover == _owner._hoveredIndex)
                    return;

                int oldHover = _owner._hoveredIndex;
                _owner._hoveredIndex = newHover;
                InvalidateHoverTransition(oldHover, _owner._hoveredIndex);
            }
            catch { }
        }

        private void InvalidateHoverTransition(int oldIndex, int newIndex)
        {
            if (_owner._listView == null || _owner._listView.IsDisposed)
                return;

            Rectangle union = Rectangle.Empty;

            bool TryGetRect(int index, out Rectangle rect)
            {
                rect = Rectangle.Empty;
                if (index < 0 || index >= _owner._listView.VirtualListSize)
                    return false;
                try
                {
                    rect = _owner._listView.GetItemRect(index, ItemBoundsPortion.Entire);
                    rect.X = 0;
                    rect.Width = _owner._listView.ClientSize.Width;
                    return rect.Height > 0;
                }
                catch
                {
                    return false;
                }
            }

            if (TryGetRect(oldIndex, out var oldRect))
                union = oldRect;
            if (TryGetRect(newIndex, out var newRect))
                union = union.IsEmpty ? newRect : Rectangle.Union(union, newRect);

            if (!union.IsEmpty)
                _owner._listView.Invalidate(union);
        }

        public void KeyDown(object? sender, KeyEventArgs e)
        {
            _ = sender;
            if (_owner._hotkeyController.IsActionKeyCode("QuickLook", e.KeyCode))
            {
                _owner.ShowQuickLook();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (_owner._currentPath == ThisPcPath)
            {
                if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.F2)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }
            }

            if (e.KeyCode == Keys.Enter)
            {
                _owner.OpenSelectedItem();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Back && !e.Control && !e.Alt && !e.Shift)
            {
                _owner.GoUp();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (!e.Control && !e.Alt && !e.Shift &&
                     ((e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z) || (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9)))
            {
                SearchAndSelect((char)e.KeyValue);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        public void KeyUp(object? sender, KeyEventArgs e)
        {
            _ = sender;
            if (_owner._hotkeyController.IsActionKeyCode("QuickLook", e.KeyCode))
            {
                _owner.HideQuickLook();
                e.Handled = true;
            }
        }

        public void SearchAndSelect(char c)
        {
            if (_owner._items.Count == 0)
                return;

            c = char.ToUpper(c);
            int startIndex;
            if (c == _owner._lastSearchChar)
            {
                startIndex = _owner._lastSearchIndex + 1;
            }
            else
            {
                _owner._lastSearchChar = c;
                _owner._lastSearchIndex = -1;
                startIndex = 0;
            }

            // Single-pass loop that wraps around the entire list.
            int count = _owner._items.Count;
            for (int i = 0; i < count; i++)
            {
                int idx = (startIndex + i) % count;
                if (_owner._items[idx].Name.Length > 0 && char.ToUpper(_owner._items[idx].Name[0]) == c)
                {
                    _owner._listView.SelectedIndices.Clear();
                    _owner._listView.SelectedIndices.Add(idx);
                    try { _owner._listView.FocusedItem = _owner._listView.Items[idx]; } catch { }
                    _owner._listView.EnsureVisible(idx);
                    _owner._lastSearchIndex = idx;
                    return;
                }
            }

            // No match found — reset search state.
            _owner._lastSearchIndex = -1;
        }

        public void SortAndRefresh()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            FileSystemService.SortItems(_owner._items, _owner._sortColumn, _owner._sortDirection, _owner._taggedFilesOnTop);
            if (!_owner.IsSearchMode)
                FileSystemService.SortItems(_owner._allItems, _owner._sortColumn, _owner._sortDirection, _owner._taggedFilesOnTop);
            sw.Stop();

            _owner._listView.Invalidate();
            if (_owner._headerHandle != IntPtr.Zero)
                InvalidateRect(_owner._headerHandle, IntPtr.Zero, true);
            _owner._statusLabel.Text = string.Format(Localization.T("status_sorted"), sw.ElapsedMilliseconds);
        }
    }
}
