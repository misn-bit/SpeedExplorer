using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class ListViewInteractionController
    {
        private readonly MainForm _owner;
        private BrowserState State => _owner.State;
        private bool _virtualRepairPending;
        private readonly System.Windows.Forms.Timer _middleAutoScrollTimer;
        // Resumes low-priority (thumbnail) icon loading a moment after scrolling stops.
        private readonly System.Windows.Forms.Timer _scrollIdleTimer;
        private bool _scrollInProgress;
        private bool _scrollRepaintPending;
        private bool _middleButtonDown;
        private bool _middleMovementExceededOpenThreshold;
        private bool _middleScrollEngaged;
        private string? _pendingMiddleOpenPath;
        private Point _middleAnchor;
        private double _middleScrollAccumulator;
        private int _middleIndicatorDeltaY;
        private MiddleIndicatorOverlayForm? _middleIndicatorOverlay;

        private const int WM_VSCROLL = 0x0115;
        private const int SB_LINEUP = 0;
        private const int SB_LINEDOWN = 1;
        // Middle-scroll tuning:
        // Dead-zone around click point where no scrolling occurs.
        private const int MiddleDeadZonePx = 5;
        // Movement threshold that cancels open-on-release.
        private const int MiddleClickCancelOpenThresholdPx = 12;
        // Speed curve tuning (lines per second).
        private const double MiddleMinLinesPerSecond = 0.75;
        private const double MiddleMaxLinesPerSecond = 1200.0;
        // Higher = slower near center, faster near edges.
        private const double MiddleSpeedGamma = 1.9;

        public ListViewInteractionController(MainForm owner)
        {
            _owner = owner;
            _middleAutoScrollTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _middleAutoScrollTimer.Tick += MiddleAutoScrollTimer_Tick;
            _scrollIdleTimer = new System.Windows.Forms.Timer { Interval = 400 };
            _scrollIdleTimer.Tick += (s, e) =>
            {
                _scrollIdleTimer.Stop();
                _scrollInProgress = false;
                _owner._iconLoadService?.SuspendLowPriority = false;
                QueueIconsForVisibleRange();
                if (_scrollRepaintPending)
                {
                    _scrollRepaintPending = false;
                    _owner._listView.Invalidate();
                }
            };
        }

        /// <summary>
        /// Called on every list scroll (wheel or scrollbar). Suspends thumbnail
        /// generation while the user is actively scrolling: otherwise the worker
        /// keeps finishing thumbnails for newly visible rows and each completed
        /// batch forces a full list repaint on top of the scroll repaints, which
        /// is what makes scrolling image folders feel choppy. Regular (high
        /// priority) extension icons keep loading. Loading resumes 400ms after
        /// the last scroll activity.
        /// </summary>
        public void NotifyScrollActivity()
        {
            _scrollInProgress = true;
            _owner._iconLoadService?.SuspendLowPriority = true;
            _scrollIdleTimer.Stop();
            _scrollIdleTimer.Start();
        }

        public bool IsScrollInteractionActive => _scrollInProgress;

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

                if (e.ItemIndex >= 0 && e.ItemIndex < State.Items.Count)
                {
                    if (!_owner.IsTileView)
                    {
                        EnsureRowCache();
                        e.Item = GetCachedListRow(e.ItemIndex);
                        if (_viewportQueuePending)
                        {
                            // Defer until the control has completed the current
                            // retrieval burst, then queue icons for the viewport.
                            _viewportQueuePending = false;
                            try { _owner.BeginInvoke((Action)(() => QueueIconsForVisibleRange())); }
                            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
                        }
                    }
                    else
                    {
                        e.Item = BuildListViewItem(State.Items[e.ItemIndex], includeSubItems: true);
                    }
                }
                else
                {
                    // Index out of bounds should never happen in steady state.
                    // Self-heal VirtualListSize and return a safe fallback item for this frame.
                    _owner.LogListViewState("RVI", $"oob idx={e.ItemIndex} count={State.Items.Count} vsize={_owner._listView.VirtualListSize}");
                    QueueVirtualListRepair($"RetrieveVirtualItem oob idx={e.ItemIndex} count={State.Items.Count} vsize={_owner._listView.VirtualListSize}");
                    if (State.Items.Count > 0)
                    {
                        int safeIndex = Math.Min(Math.Max(e.ItemIndex, 0), State.Items.Count - 1);
                        e.Item = BuildListViewItem(State.Items[safeIndex], includeSubItems: true);
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

                        int target = State.Items.Count;
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

            if (State.CurrentPath == ThisPcPath && !_owner.IsSearchMode)
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
            if (State.CurrentPath != ThisPcPath && newColumn == SortColumn.Tags)
            {
                if (!State.TaggedFilesOnTop)
                {
                    State.TaggedFilesOnTop = true;
                    State.SortColumn = SortColumn.Tags;
                    State.SortDirection = SortDirection.Ascending;
                }
                else if (State.SortColumn == SortColumn.Tags)
                {
                    if (State.SortDirection == SortDirection.Ascending)
                    {
                        State.SortDirection = SortDirection.Descending;
                    }
                    else
                    {
                        State.TaggedFilesOnTop = false;
                        State.SortColumn = SortColumn.Name;
                        State.SortDirection = SortDirection.Ascending;
                    }
                }
                else
                {
                    State.SortColumn = SortColumn.Tags;
                    State.SortDirection = SortDirection.Ascending;
                }
            }
            else
            {
                if (newColumn == State.SortColumn)
                {
                    State.SortDirection = State.SortDirection == SortDirection.Ascending
                        ? SortDirection.Descending
                        : SortDirection.Ascending;
                }
                else
                {
                    State.SortColumn = newColumn.Value;
                    State.SortDirection = SortDirection.Ascending;
                }
            }

            // Persist current folder sort immediately so it survives app close without navigation.
            if (!string.IsNullOrWhiteSpace(State.CurrentPath) &&
                State.CurrentPath != ThisPcPath &&
                !ShellNavigationController.IsShellPath(State.CurrentPath))
            {
                _owner._nav.FolderSortSettings[State.CurrentPath] = (State.SortColumn, State.SortDirection);
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
                _middleButtonDown = true;
                _middleMovementExceededOpenThreshold = false;
                _middleScrollEngaged = false;
                _middleAnchor = e.Location;
                _pendingMiddleOpenPath = ResolveMiddleClickTargetPath(e.Location);
                _middleScrollAccumulator = 0;
                _middleIndicatorDeltaY = 0;
                if (_owner._iconLoadService != null)
                    _owner._iconLoadService.SuspendLowPriority = true;
                EnsureMiddleIndicatorOverlay();
                SyncMiddleOverlayBounds();
                UpdateMiddleOverlayVisual();
                _middleIndicatorOverlay?.Show(_owner);
                _middleAutoScrollTimer.Start();
                _owner._listView.Invalidate();
                return;
            }
            else if (e.Button == MouseButtons.Left)
            {
                // Native ListView marquee selection handles drag-select.
            }
        }

        public void MouseUp(object? sender, MouseEventArgs e)
        {
            _ = sender;
            if (e.Button != MouseButtons.Middle)
                return;

            _middleAutoScrollTimer.Stop();
            if (_owner._iconLoadService != null)
                _owner._iconLoadService.SuspendLowPriority = false;
            bool shouldOpenTarget = _middleButtonDown &&
                                    !_middleMovementExceededOpenThreshold &&
                                    !_middleScrollEngaged &&
                                    !string.IsNullOrWhiteSpace(_pendingMiddleOpenPath);
            string? targetPath = _pendingMiddleOpenPath;
            _middleButtonDown = false;
            _pendingMiddleOpenPath = null;
            _middleScrollAccumulator = 0;
            _middleIndicatorDeltaY = 0;
            HideMiddleOverlay();
            _owner._listView.Invalidate();

            if (!shouldOpenTarget || string.IsNullOrWhiteSpace(targetPath))
                return;

            if (FileSystemService.IsAccessible(targetPath))
            {
                _owner._openTargetController.OpenPathByMiddleClickPreference(targetPath, activateTab: false);
            }
            else
            {
                _owner._statusLabel.Text = string.Format(Localization.T("status_access_denied"), targetPath);
            }
        }

        public ListViewItem BuildListViewItem(FileItem item, bool includeSubItems, bool cacheIconBindings = false, int cacheRowIndex = -1)
        {
            var s = AppSettings.Current;
            // Cached list-mode rows are built without side effects; icon loads
            // for them are queued viewport-by-viewport instead (see
            // QueueIconsForVisibleRange).
            bool allowQueue = !cacheIconBindings;

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
                        bool preferHighQualityLarge = _owner.GetEffectiveIconSize() >= 64;
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
                                if (allowQueue)
                                {
                                    // Queue generic placeholder asynchronously (non-blocking for UI).
                                    _owner._iconLoadService?.EnsureGenericIcon(genericKey, extLookup, item.IsDirectory, colored);
                                    // Queue async load for unique icon/thumbnail.
                                    if (!_owner.IsTileView || _owner._tileViewController.ShouldQueueUniqueIconNow(uniqueKey))
                                        _owner._iconLoadService?.QueueIconLoad(item.FullPath, item.IsDirectory, colored);
                                }

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
                                if (allowQueue)
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

            // Cached list-mode rows can't rely on per-frame rebuilds to pick up
            // icons that load later, so remember who is waiting for each key;
            // HandleIconReady updates them in place when the load completes.
            if (cacheIconBindings && !_owner.IsTileView)
            {
                if (!string.IsNullOrEmpty(pendingUniqueKey))
                {
                    if (_owner._smallIcons.Images.ContainsKey(pendingUniqueKey))
                        lvi.ImageKey = pendingUniqueKey;
                    else
                        RegisterListIconBinding(pendingUniqueKey, cacheRowIndex);
                }
                if (!string.IsNullOrEmpty(pendingResolvedKey))
                {
                    if (_owner._smallIcons.Images.ContainsKey(pendingResolvedKey))
                        lvi.ImageKey = pendingResolvedKey;
                    else
                        RegisterListIconBinding(pendingResolvedKey, cacheRowIndex);
                }
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
                lvi.SubItems.Add(item.DirectoryNameDisplay);

                if (item.IsDirectory)
                    lvi.SubItems.Add("");
                else
                    lvi.SubItems.Add(item.SizeDisplay);

                lvi.SubItems.Add(item.DateModifiedDisplay);
                lvi.SubItems.Add(item.DateCreatedDisplay);
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
        
        // ---------- List-mode row cache ----------
        // Keep only recently requested rows. A full per-folder cache does a lot
        // of work during the first paint and retains one ListViewItem plus seven
        // subitems for every file in a large folder.
        private const int CachedListRowCapacity = 384;

        private sealed class CachedListRow
        {
            public ListViewItem Item { get; }
            public LinkedListNode<int> LruNode { get; }

            public CachedListRow(ListViewItem item, LinkedListNode<int> lruNode)
            {
                Item = item;
                LruNode = lruNode;
            }
        }

        private readonly Dictionary<int, CachedListRow> _cachedRows = new();
        private readonly LinkedList<int> _cachedRowLru = new();
        private object? _cacheSource;
        private readonly Dictionary<string, HashSet<int>> _iconBindings = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, HashSet<string>> _rowIconBindings = new();
        private bool _viewportQueuePending;

        private void EnsureRowCache()
        {
            var items = State.Items;
            if (_cacheSource != null && ReferenceEquals(_cacheSource, items))
                return;

            ClearRowCache();
            _cacheSource = items;
            _viewportQueuePending = true;
        }

        private ListViewItem GetCachedListRow(int index)
        {
            var fileItem = State.Items[index];
            if (_cachedRows.TryGetValue(index, out var cached))
            {
                if (ReferenceEquals(cached.Item.Tag, fileItem))
                {
                    _cachedRowLru.Remove(cached.LruNode);
                    _cachedRowLru.AddFirst(cached.LruNode);
                    return cached.Item;
                }

                RemoveCachedListRow(index);
            }

            var row = BuildListViewItem(fileItem, includeSubItems: true, cacheIconBindings: true, cacheRowIndex: index);
            var node = _cachedRowLru.AddFirst(index);
            _cachedRows[index] = new CachedListRow(row, node);
            while (_cachedRows.Count > CachedListRowCapacity)
                RemoveCachedListRow(_cachedRowLru.Last!.Value);
            return row;
        }

        public void InvalidateRowCache()
        {
            ClearRowCache();
            _viewportQueuePending = true;
        }

        private void ClearRowCache()
        {
            _cachedRows.Clear();
            _cachedRowLru.Clear();
            _iconBindings.Clear();
            _rowIconBindings.Clear();
            _cacheSource = null;
            _viewportQueuePending = false;
        }

        private void RemoveCachedListRow(int index)
        {
            if (_cachedRows.Remove(index, out var row))
                _cachedRowLru.Remove(row.LruNode);

            if (!_rowIconBindings.Remove(index, out var keys))
                return;

            foreach (string key in keys)
            {
                if (_iconBindings.TryGetValue(key, out var rows))
                {
                    rows.Remove(index);
                    if (rows.Count == 0)
                        _iconBindings.Remove(key);
                }
            }
        }

        private void QueueIconsForVisibleRange(bool prioritize = false)
        {
            if (_owner.IsTileView || (_scrollInProgress && !prioritize)) return;
            var lv = _owner._listView;
            if (lv == null || lv.IsDisposed) return;
            var items = State.Items;
            if (items.Count == 0) return;

            int top = 0;
            int rowHeight = Math.Max(1, lv.Font.Height + 4);
            try
            {
                top = lv.TopItem?.Index ?? 0;
                int measuredHeight = lv.GetItemRect(top, ItemBoundsPortion.Entire).Height;
                if (measuredHeight > 0)
                    rowHeight = measuredHeight;
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }

            top = Math.Clamp(top, 0, items.Count - 1);
            int visibleRows = Math.Max(1, (lv.ClientSize.Height / rowHeight) + 3);
            int limit = Math.Min(top + visibleRows + 8, items.Count);
            for (int i = top; i < limit; i++)
                QueueIconsForItem(items[i], prioritize);
        }

        // Side-effect mirror of BuildListViewItem's icon decision: only queues
        // loads whose keys are missing from the ImageList (dedup is also done
        // inside IconLoadService). Keep in sync with that method.
        private void QueueIconsForItem(FileItem item, bool prioritize = false)
        {
            var s = AppSettings.Current;
            if (!s.ShowIcons || s.UseEmojiIcons || item.IsShellItem) return;

            bool colored = s.UseSystemIcons;
            bool isExeOrLnk = item.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                              item.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                              item.Extension.Equals(".url", StringComparison.OrdinalIgnoreCase) ||
                              item.Extension.Equals(".ico", StringComparison.OrdinalIgnoreCase);
            bool preferHighQualityLarge = _owner.GetEffectiveIconSize() >= 64;
            bool unique = (s.ResolveUniqueIcons && isExeOrLnk) || preferHighQualityLarge;

            string prefix = colored ? "sys_" : "gray_";
            bool isImage = FileSystemService.IsImageFile(item.FullPath);
            bool hasExtension = !string.IsNullOrWhiteSpace(item.Extension);
            string effectiveExt = hasExtension ? item.Extension : ".noext";
            string extLookup = hasExtension ? item.Extension : "file";

            if (unique || (isImage && s.ShowThumbnails))
            {
                string uniqueKey = item.FullPath;
                if (_owner._smallIcons.Images.ContainsKey(uniqueKey)) return;

                string genericKey = item.IsDirectory
                    ? $"{prefix}folder"
                    : (isImage ? $"{prefix}image" : $"{prefix}{effectiveExt}");
                _owner._iconLoadService?.EnsureGenericIcon(genericKey, extLookup, item.IsDirectory, colored);
                _owner._iconLoadService?.QueueIconLoad(item.FullPath, item.IsDirectory, colored, prioritize: prioritize);
            }
            else
            {
                string targetKey = item.IsDirectory ? $"{prefix}folder" : $"{prefix}{effectiveExt}";
                if (_owner._smallIcons.Images.ContainsKey(targetKey)) return;
                _owner._iconLoadService?.QueueIconLoad(targetKey, item.IsDirectory, colored, lookupPath: item.IsDirectory ? null : extLookup);
            }
        }

        private void RegisterListIconBinding(string key, int rowIndex)
        {
            if (rowIndex < 0)
                return;

            if (!_iconBindings.TryGetValue(key, out var rows))
                _iconBindings[key] = rows = new HashSet<int>();
            rows.Add(rowIndex);

            if (!_rowIconBindings.TryGetValue(rowIndex, out var keys))
                _rowIconBindings[rowIndex] = keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            keys.Add(key);
        }

        /// <summary>
        /// List-mode counterpart of TileViewController.HandleIconReady: an async
        /// icon/thumbnail just landed in the ImageList under `key`, so update
        /// every cached row that was waiting for it.
        /// </summary>
        public void HandleIconReady(string key)
        {
            if (_owner.IsTileView || string.IsNullOrWhiteSpace(key)) return;
            if (!_iconBindings.Remove(key, out var rows)) return;

            bool updatedCachedRow = false;
            foreach (int index in rows)
            {
                if (_cachedRows.TryGetValue(index, out var cached))
                {
                    if (index >= State.Items.Count || !ReferenceEquals(cached.Item.Tag, State.Items[index]))
                    {
                        RemoveCachedListRow(index);
                        continue;
                    }

                    try
                    {
                        cached.Item.ImageKey = key;
                        updatedCachedRow = true;
                    }
                    catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
                }

                if (_rowIconBindings.TryGetValue(index, out var keys))
                {
                    keys.Remove(key);
                    if (keys.Count == 0)
                        _rowIconBindings.Remove(index);
                }
            }

            if (updatedCachedRow)
            {
                if (_scrollInProgress)
                    _scrollRepaintPending = true;
                else
                    _owner._listView.Invalidate();
            }
        }

        private static string BuildDriveTileIconKey(FileItem item)
        {
            string cleanPath = (item.FullPath ?? "").Replace(":\\", "").ToLowerInvariant();
            string type = item.Extension == ".usb" ? "u" : "d";
            return $"drvbar_{type}_{cleanPath}_{item.Size}_{item.FreeSpace}";
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
            if (_middleButtonDown &&
                (Math.Abs(e.X - _middleAnchor.X) > MiddleClickCancelOpenThresholdPx ||
                 Math.Abs(e.Y - _middleAnchor.Y) > MiddleClickCancelOpenThresholdPx))
            {
                _middleMovementExceededOpenThreshold = true;
            }

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
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
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
                catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
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
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
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

        public void Paint(object? sender, PaintEventArgs e)
            => _ = (sender, e);

        private string? ResolveMiddleClickTargetPath(Point location)
        {
            var hit = _owner._listView.HitTest(location);
            if (hit.Item?.Tag is not FileItem fi)
                return null;

            if (_owner.IsSearchMode)
            {
                string? targetPath = fi.IsDirectory ? fi.FullPath : Path.GetDirectoryName(fi.FullPath);
                return string.IsNullOrWhiteSpace(targetPath) ? null : targetPath;
            }

            return fi.IsDirectory ? fi.FullPath : null;
        }

        private void MiddleAutoScrollTimer_Tick(object? sender, EventArgs e)
        {
            _ = sender;
            if (!_middleButtonDown || _owner._listView == null || _owner._listView.IsDisposed || !_owner._listView.IsHandleCreated)
                return;
            if ((Control.MouseButtons & MouseButtons.Middle) == 0)
            {
                _middleAutoScrollTimer.Stop();
                if (_owner._iconLoadService != null)
                    _owner._iconLoadService.SuspendLowPriority = false;
                _middleButtonDown = false;
                _pendingMiddleOpenPath = null;
                _middleScrollAccumulator = 0;
                _middleIndicatorDeltaY = 0;
                HideMiddleOverlay();
                _owner._listView.Invalidate();
                return;
            }

            Point p = _owner._listView.PointToClient(Cursor.Position);
            int deltaY = p.Y - _middleAnchor.Y;
            _middleIndicatorDeltaY = deltaY;
            SyncMiddleOverlayBounds();
            UpdateMiddleOverlayVisual();
            int abs = Math.Abs(deltaY);
            if (abs <= MiddleDeadZonePx)
                return;

            int availableToEdge = deltaY < 0
                ? Math.Max(MiddleDeadZonePx + 1, _middleAnchor.Y)
                : Math.Max(MiddleDeadZonePx + 1, _owner._listView.ClientSize.Height - _middleAnchor.Y);
            int over = abs - MiddleDeadZonePx;
            int availableOver = Math.Max(1, availableToEdge - MiddleDeadZonePx);
            double linesPerSecond = ComputeMiddleScrollSpeed(over, availableOver);
            double dt = _middleAutoScrollTimer.Interval / 1000.0;
            _middleScrollAccumulator += linesPerSecond * dt;
            int steps = (int)Math.Min(512, Math.Floor(_middleScrollAccumulator));
            if (steps <= 0)
                return;
            _middleScrollAccumulator -= steps;

            int scrollCmd = deltaY < 0 ? SB_LINEUP : SB_LINEDOWN;
            for (int i = 0; i < steps; i++)
                SendMessage(_owner._listView.Handle, WM_VSCROLL, scrollCmd, 0);

            _middleScrollEngaged = true;
            _owner._listView.Invalidate();
        }

        private static double ComputeMiddleScrollSpeed(int overPx, int availableOverPx)
        {
            if (overPx <= 0)
                return 0;

            double t = overPx / (double)Math.Max(1, availableOverPx);
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            t = Math.Pow(t, MiddleSpeedGamma);
            return MiddleMinLinesPerSecond + (MiddleMaxLinesPerSecond - MiddleMinLinesPerSecond) * t;
        }

        private void EnsureMiddleIndicatorOverlay()
        {
            if (_middleIndicatorOverlay != null && !_middleIndicatorOverlay.IsDisposed)
                return;
            _middleIndicatorOverlay = new MiddleIndicatorOverlayForm();
            if (_owner._listView != null && !_owner._listView.IsDisposed)
                _middleIndicatorOverlay.BackgroundKeyColor = _owner._listView.BackColor;
        }

        private void SyncMiddleOverlayBounds()
        {
            if (_middleIndicatorOverlay == null || _middleIndicatorOverlay.IsDisposed || _owner._listView == null || _owner._listView.IsDisposed)
                return;

            Rectangle screenRect = _owner._listView.RectangleToScreen(_owner._listView.ClientRectangle);
            if (_middleIndicatorOverlay.Bounds != screenRect)
                _middleIndicatorOverlay.Bounds = screenRect;
        }

        private void UpdateMiddleOverlayVisual()
        {
            if (_middleIndicatorOverlay == null || _middleIndicatorOverlay.IsDisposed)
                return;

            _middleIndicatorOverlay.AnchorPoint = _middleAnchor;
            _middleIndicatorOverlay.DeltaY = _middleIndicatorDeltaY;
            _middleIndicatorOverlay.DeadZonePx = MiddleDeadZonePx;
            _middleIndicatorOverlay.Invalidate();
        }

        private void HideMiddleOverlay()
        {
            if (_middleIndicatorOverlay != null && !_middleIndicatorOverlay.IsDisposed)
                _middleIndicatorOverlay.Hide();
        }

        private sealed class MiddleIndicatorOverlayForm : Form
        {
            private const int WS_EX_TOOLWINDOW = 0x00000080;
            private const int WS_EX_NOACTIVATE = 0x08000000;
            private const int WS_EX_TRANSPARENT = 0x00000020;
            private Color _backgroundKeyColor = Color.Black;

            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public Point AnchorPoint { get; set; }

            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public int DeltaY { get; set; }

            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public int DeadZonePx { get; set; }

            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public Color BackgroundKeyColor
            {
                get => _backgroundKeyColor;
                set
                {
                    _backgroundKeyColor = value;
                    BackColor = value;
                    TransparencyKey = value;
                }
            }

            public MiddleIndicatorOverlayForm()
            {
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                TopMost = false;
                BackgroundKeyColor = Color.Black;
                DoubleBuffered = true;
            }

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
                    return cp;
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                int r = DeadZonePx;
                var center = AnchorPoint;
                bool upActive = DeltaY < -DeadZonePx;
                bool downActive = DeltaY > DeadZonePx;

                Color offWhite = Color.FromArgb(245, 242, 232);

                const int circleDiameter = 12;
                int circleX = center.X - (circleDiameter / 2);
                int circleY = center.Y - (circleDiameter / 2);
                using (var fill = new SolidBrush(offWhite))
                    g.FillEllipse(fill, circleX, circleY, circleDiameter, circleDiameter);

                const int triHalfW = 5;
                const int triH = 7;
                int tx = center.X;
                int upBaseY = center.Y - r - 8;
                int downBaseY = center.Y + r + 8;
                using (var brush = new SolidBrush(offWhite))
                {
                    if (upActive)
                    {
                        g.FillPolygon(brush, new[]
                        {
                            new Point(tx, upBaseY - triH),
                            new Point(tx - triHalfW, upBaseY),
                            new Point(tx + triHalfW, upBaseY)
                        });
                    }
                    else if (downActive)
                    {
                        g.FillPolygon(brush, new[]
                        {
                            new Point(tx, downBaseY + triH),
                            new Point(tx - triHalfW, downBaseY),
                            new Point(tx + triHalfW, downBaseY)
                        });
                    }
                }
            }
        }

        public void KeyDown(object? sender, KeyEventArgs e)
        {
            _ = sender;
            if (_owner._hotkeyController.IsActionKeyData("QuickLook", e.KeyData))
            {
                _owner.ShowQuickLook();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (State.CurrentPath == ThisPcPath)
            {
                if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.F2)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }
            }

            if (_owner._hotkeyController.IsActionKeyData("OpenSelected", e.KeyData))
            {
                _owner.OpenSelectedItem();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (_owner._hotkeyController.IsActionKeyData("NavigateUp", e.KeyData))
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
            if (_owner._hotkeyController.IsActionKeyData("QuickLook", e.KeyData))
            {
                _owner.HideQuickLook();
                e.Handled = true;
            }
        }

        public void SearchAndSelect(char c)
        {
            if (State.Items.Count == 0)
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
            int count = State.Items.Count;
            for (int i = 0; i < count; i++)
            {
                int idx = (startIndex + i) % count;
                if (State.Items[idx].Name.Length > 0 && char.ToUpper(State.Items[idx].Name[0]) == c)
                {
                    _owner._listView.SelectedIndices.Clear();
                    _owner._listView.SelectedIndices.Add(idx);
                    try { _owner._listView.FocusedItem = _owner._listView.Items[idx]; } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
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
            FileSystemService.SortItems(State.Items, State.SortColumn, State.SortDirection, State.TaggedFilesOnTop);
            if (!_owner.IsSearchMode)
                FileSystemService.SortItems(State.AllItems, State.SortColumn, State.SortDirection, State.TaggedFilesOnTop);
            sw.Stop();

            // A header click ends the scrollbar/wheel interaction. Don't let
            // its idle debounce hold thumbnails for the newly sorted viewport.
            _scrollIdleTimer.Stop();
            _scrollInProgress = false;
            _scrollRepaintPending = false;
            _owner._iconLoadService?.SuspendLowPriority = true;
            _owner._iconLoadService?.ResetVisiblePriorities();

            InvalidateRowCache();
            _owner._listView.Invalidate();
            QueueIconsForVisibleRange(prioritize: true);
            _owner._iconLoadService?.SuspendLowPriority = false;
            if (_owner._headerHandle != IntPtr.Zero)
                InvalidateRect(_owner._headerHandle, IntPtr.Zero, true);
            _owner._statusLabel.Text = string.Format(Localization.T("status_sorted"), sw.ElapsedMilliseconds);
        }
    }
}
