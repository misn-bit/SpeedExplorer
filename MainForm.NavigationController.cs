using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private async Task<T> AwaitWithCancellation<T>(Task<T> task, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>();
        using (ct.Register(() => tcs.TrySetResult(true)))
        {
            if (task != await Task.WhenAny(task, tcs.Task))
            {
                throw new OperationCanceledException(ct);
            }
        }
        return await task;
    }

    public async Task NavigateTo(string path, List<string>? pathsToSelect = null)
    {
        long navTraceId = Interlocked.Increment(ref _navigationTraceSeq);
        var totalSw = Stopwatch.StartNew();
        var gcStart = AppSettings.Current.DebugNavigationGcStats ? CaptureGcSnapshot() : ((int, int, int, long)?)null;
        var previousPath = _currentPath;
        NavigationDebugLogger.Log($"NAV#{navTraceId} START path=\"{TraceText(path)}\" current=\"{TraceText(_currentPath)}\" selectCount={pathsToSelect?.Count ?? 0}");

        if (IsDisposed || Disposing)
        {
            NavigationDebugLogger.Log($"NAV#{navTraceId} SKIP form-disposed");
            return;
        }

        if (_searchBox == null || _listView == null || _statusLabel == null || _pathLabel == null ||
            _searchBox.IsDisposed || _listView.IsDisposed || _statusLabel.IsDisposed || _pathLabel.IsDisposed)
        {
            NavigationDebugLogger.Log($"NAV#{navTraceId} SKIP ui-not-ready");
            return;
        }

        if (string.IsNullOrEmpty(path))
        {
            NavigationDebugLogger.Log($"NAV#{navTraceId} SKIP empty path");
            return;
        }
        bool isShellPath = IsShellPath(path);
        bool leavingThisPc = _currentPath == ThisPcPath && path != ThisPcPath;
        NavigationDebugLogger.Log($"NAV#{navTraceId} FLAGS shell={isShellPath} leavingThisPc={leavingThisPc}");

        if (!isShellPath && path != ThisPcPath && !Directory.Exists(path))
        {
            NavigationDebugLogger.Log($"NAV#{navTraceId} PATH missing; fallback to ThisPC");
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => ObserveTask(NavigateTo(ThisPcPath, pathsToSelect), $"NAV#{navTraceId} fallback-dispatch")));
            }
            else
            {
                _statusLabel.Text = Localization.T("status_path_unavailable");
                ObserveTask(NavigateTo(ThisPcPath, pathsToSelect), $"NAV#{navTraceId} fallback");
            }
            return;
        }

        // Ensure we are on the UI thread for list modifications
        if (this.InvokeRequired)
        {
            NavigationDebugLogger.Log($"NAV#{navTraceId} DISPATCH to UI thread");
            var swDispatch = Stopwatch.StartNew();
            this.Invoke(new Action(() => ObserveTask(NavigateTo(path, pathsToSelect), $"NAV#{navTraceId} dispatch")));
            if (AppSettings.Current.DebugNavigationUiQueue)
            {
                NavigationDebugLogger.Log($"NAV#{navTraceId} DISPATCH_WAIT ms={swDispatch.ElapsedMilliseconds}");
            }
            return;
        }

        // Re-entrancy guard: If we're already navigating, queue this request
        if (_nav.IsNavigating)
        {
            NavigationDebugLogger.Log($"NAV#{navTraceId} QUEUED while navigating. pendingPath=\"{TraceText(path)}\"");
            _nav.QueuePending(path, pathsToSelect);

            // Force the current load to cancel so we can proceed to the pending one
            _loadCts?.Cancel();
            return;
        }
        NavigationDebugLogger.Log($"NAV#{navTraceId} ENTER");
        _nav.IsNavigating = true;
        BeginNavigationFreezeVisual();
        try
        {
            // Clear search when navigating
            _searchController.ExitSearchModeOnNavigate();
            _tileViewController.CancelPopulation();
            _iconLoadService?.CancelPending();
            _isShellMode = isShellPath;
            _suppressSearchTextChanged = true;
            try
            {
                if (_isShellMode)
                {
                    _searchBox.Enabled = false;
                    _searchBox.Text = Localization.T("search_not_supported");
                    _searchBox.ForeColor = Color.Gray;
                }
                else
                {
                    _searchBox.Enabled = true;
                    _searchBox.Text = Localization.T("search_placeholder");
                    _searchBox.ForeColor = Color.Gray;
                }
            }
            finally
            {
                _suppressSearchTextChanged = false;
            }

            if (!_suppressHistoryUpdate && !string.IsNullOrEmpty(_currentPath) && _currentPath != path)
            {
                _nav.BackHistory.Push(_currentPath);
                _nav.ForwardHistory.Clear();
            }

            // Save sort settings for previous folder
            if (!string.IsNullOrEmpty(_currentPath))
            {
                _nav.FolderSortSettings[_currentPath] = (_sortColumn, _sortDirection);
            }

            // Restore sort settings for new path, or default
            if (_nav.FolderSortSettings.TryGetValue(path, out var savedSort))
            {
                _sortColumn = savedSort.Column;
                _sortDirection = savedSort.Direction;
            }
            else
            {
                _sortColumn = path == ThisPcPath ? SortColumn.DriveNumber : SortColumn.Name;
                _sortDirection = SortDirection.Ascending;
            }

            // Update Window Title (Taskbar) and Status Bar
            if (isShellPath)
            {
                _currentDisplayPath = GetShellDisplayName(path);
                this.Text = _currentDisplayPath;
                _pathLabel.Text = _currentDisplayPath;
            }
            else if (path == ThisPcPath)
            {
                this.Text = Localization.T("this_pc");
                _pathLabel.Text = Localization.T("this_pc");
                _currentDisplayPath = Localization.T("this_pc");
            }
            else
            {
                string folderName;
                try
                {
                    var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    folderName = Path.GetFileName(trimmed);
                    if (string.IsNullOrEmpty(folderName))
                        folderName = trimmed;
                    if (string.IsNullOrEmpty(folderName))
                        folderName = path;
                }
                catch
                {
                    folderName = path;
                }

                this.Text = folderName; // Just folder name
                _pathLabel.Text = path; // Show full path in dedicated label
                _currentDisplayPath = path;
            }
            _statusLabel.Text = Localization.T("status_loading"); // status label used for info

            // Save selection (if valid) before navigating away
            if (!string.IsNullOrEmpty(_currentPath) && _listView.FocusedItem != null && _listView.FocusedItem.Tag is FileItem fi)
            {
                _nav.LastSelection[_currentPath] = fi.Name;
            }

            // Sync sidebar selection:
            // 1) exact node match (folder/device),
            // 2) fallback to drive root only if exact node isn't present.
            TreeNode? exactNode = null;
            TreeNode? driveNode = null;

            // If the sidebar already has the target path selected (e.g. duplicate path in pinned/recent),
            // keep that exact node to avoid one-frame highlight jumps.
            if (_sidebar.SelectedNode?.Tag is string selectedPath &&
                string.Equals(selectedPath, path, StringComparison.OrdinalIgnoreCase))
            {
                exactNode = _sidebar.SelectedNode;
            }

            foreach (TreeNode node in _sidebar.Nodes)
            {
                if (node.Tag is not string nodePath || string.IsNullOrWhiteSpace(nodePath))
                    continue;

                if (nodePath.StartsWith(SidebarSeparatorTag, StringComparison.Ordinal))
                    continue;

                if (exactNode == null && string.Equals(nodePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    exactNode = node;
                    break;
                }

                if (driveNode == null &&
                    nodePath.Length <= 3 &&
                    path.StartsWith(nodePath, StringComparison.OrdinalIgnoreCase))
                {
                    driveNode = node;
                }
            }

            var sidebarTarget = exactNode ?? driveNode;
            if (sidebarTarget != null && _sidebar.SelectedNode != sidebarTarget)
            {
                _sidebar.SelectedNode = sidebarTarget;
            }

            _currentPath = path;
            UpdateWatcher(path);
            if (!isShellPath &&
                path != ThisPcPath &&
                !string.Equals(path, previousPath, StringComparison.OrdinalIgnoreCase))
            {
                TryRegisterFolderInWindowsRecent(path);
            }
            UpdateBreadcrumbs(path);
            _addressTextBox.Text = path;
            _statusLabel.Text = Localization.T("status_loading");
            UpdateActiveTabTitle();
            // Push navigation chrome updates immediately so path/title/crumbs do not feel delayed.
            try
            {
                _addressBar?.Invalidate();
                _addressBar?.Update();
                _titleBar?.Invalidate();
                _titleBar?.Update();
                _statusBar?.Invalidate();
                _statusBar?.Update();
            }
            catch { }
            try
            {
                bool preserveAiPanelFocus = _llmChatPanel != null && _llmChatPanel.IsExpanded;
                if (!preserveAiPanelFocus && _listView.Visible && _listView.CanFocus)
                    _listView.Focus();
            }
            catch { }
            await Task.Yield();

            _tileViewController.ApplyViewModeForNavigation();

            bool cacheIsSearchSnapshot = _pendingTabCacheIsSearchMode;
            if (TryTakePendingTabCache(path, out var cachedItems, out var cachedAllItems))
            {
                NavigationDebugLogger.Log($"NAV#{navTraceId} CACHE_HIT count={cachedItems.Count} all={cachedAllItems.Count}");

                _allItems = cachedAllItems;
                _items = cachedItems;

                if (path == ThisPcPath)
                    SetupDriveColumns(_listView);
                else
                    SetupFileColumns(_listView);

                BindItemsToListView(navTraceId, cacheIsSearchSnapshot ? "NAVCACHE_SEARCH" : "NAVCACHE", path, pathsToSelect, totalSw, gcStart);
                return;
            }

            // Fix: Immediately clear items if we are switching context (e.g. from This PC to a folder)
            // This prevents the "stuck" look where Drives are shown with File columns if load fails or takes time.
            if (leavingThisPc || (isShellPath && !_isShellMode) || (!isShellPath && _isShellMode))
            {
                _listView.BeginUpdate();
                _items = new List<FileItem>();
                _allItems = new List<FileItem>();
                _listView.VirtualListSize = 0;
                _listView.EndUpdate();
                _listView.Invalidate();
            }

            // If we are leaving "This PC", defer clearing until we have new items
            if (leavingThisPc)
            {
                _pendingClearFromThisPc = true;
                _retryLoadPath = path;
                _retryLoadPending = true;
                _retryLoadTimer.Stop();
                _retryLoadTimer.Start();
            }

            if (path == ThisPcPath)
            {
                NavigationDebugLogger.Log($"NAV#{navTraceId} LOAD_DRIVES");
                SetupDriveColumns(_listView);
                LoadDrives();
                NavigationDebugLogger.Log($"NAV#{navTraceId} DONE drives totalMs={totalSw.ElapsedMilliseconds} items={_items.Count}");
                return;
            }

            if (isShellPath)
            {
                NavigationDebugLogger.Log($"NAV#{navTraceId} LOAD_SHELL shellPath=\"{TraceText(path)}\"");
                SetupFileColumns(_listView);
                await LoadShellFolder(path);
                NavigationDebugLogger.Log($"NAV#{navTraceId} DONE shell totalMs={totalSw.ElapsedMilliseconds} items={_items.Count}");
                return;
            }

            // Ensure file columns are active (switch back from This PC drive columns when needed).
            if (_listView.Columns.Count == 0 ||
                (_listView.Columns[0].Tag as ColumnMeta)?.Key != "col_name")
            {
                SetupFileColumns(_listView);
            }

            _statusLabel.Text = Localization.T("status_loading_items");
            // Avoid Application.DoEvents re-entrancy; just force a paint and yield once.
            try { _statusBar?.Invalidate(); _statusBar?.Update(); } catch { }
            await Task.Yield();

            // Cancel previous load
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();

            try
            {
                var swEnum = Stopwatch.StartNew();
                // Wrap in AwaitWithCancellation so we can force-cancel even if GetFilesAsync hangs on I/O
                _allItems = await AwaitWithCancellation(FileSystemService.GetFilesAsync(path, _loadCts.Token), _loadCts.Token);
                swEnum.Stop();
                NavigationDebugLogger.Log($"NAV#{navTraceId} ENUM ms={swEnum.ElapsedMilliseconds} count={_allItems.Count}");
                QueueGenericIconsWarmup(_allItems);

                // Sort and clone off the UI thread to avoid long freezes on huge folders
                var swSort = Stopwatch.StartNew();
                _items = await Task.Factory.StartNew(() =>
                {
                    FileSystemService.SortItems(_allItems, _sortColumn, _sortDirection);
                    return new List<FileItem>(_allItems);
                }, _loadCts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                swSort.Stop();
                NavigationDebugLogger.Log($"NAV#{navTraceId} SORT ms={swSort.ElapsedMilliseconds} count={_items.Count} sort={_sortColumn}/{_sortDirection}");

                // Bind items to list view and restore selection
                BindItemsToListView(navTraceId, "NAV", path, pathsToSelect, totalSw, gcStart);
            }
            catch (OperationCanceledException)
            {
                NavigationDebugLogger.Log($"NAV#{navTraceId} CANCELED totalMs={totalSw.ElapsedMilliseconds} current=\"{TraceText(_currentPath)}\" items={_items.Count}");
                LogGcDelta(navTraceId, "NAV", gcStart);

                // If cancelled but list is still empty, trigger a retry (common in background wake scenarios)
                // But ONLY if we don't have a pending navigation waiting (which caused the cancel)
                if (_items.Count == 0 && !string.IsNullOrEmpty(_currentPath) && _currentPath != ThisPcPath && _nav.PendingPath == null)
                {
                    _retryLoadPath = _currentPath;
                    _retryLoadPending = true;
                    _retryLoadTimer.Stop();
                    _retryLoadTimer.Start();
                }
            }
            catch (Exception ex)
            {
                NavigationDebugLogger.Log($"NAV#{navTraceId} ERROR totalMs={totalSw.ElapsedMilliseconds} message=\"{TraceText(ex.Message)}\"");
                LogGcDelta(navTraceId, "NAV", gcStart);

                _statusLabel.Text = string.Format(Localization.T("status_error"), ex.Message);
                // If load failed and list is empty, trigger a retry
                // But ONLY if we don't have a pending navigation
                if (_items.Count == 0 && !string.IsNullOrEmpty(_currentPath) && _currentPath != ThisPcPath && _nav.PendingPath == null)
                {
                    _retryLoadPath = _currentPath;
                    _retryLoadPending = true;
                    _retryLoadTimer.Stop();
                    _retryLoadTimer.Start();
                }
            }
        }
        finally
        {
            EndNavigationFreezeVisual();
            // Run a post-unfreeze viewport guard once the list is visible again.
            // Some virtual list glitches only manifest after visibility is restored.
            EnsureListViewportAndPaint("NAV-final");
            try
            {
                if (!IsDisposed && !Disposing && _listView != null && !_listView.IsDisposed)
                {
                    bool preserveAiPanelFocus = _llmChatPanel != null && _llmChatPanel.IsExpanded;
                    if (!preserveAiPanelFocus &&
                        _listView.Visible && _listView.CanFocus)
                    {
                        _listView.Focus();
                        // If focus still did not stick due message timing, retry once asynchronously.
                        if (!_listView.Focused)
                        {
                            BeginInvoke((Action)(() =>
                            {
                                try
                                {
                                    if (!IsDisposed && !Disposing &&
                                        _listView != null && !_listView.IsDisposed &&
                                        _listView.Visible && _listView.CanFocus)
                                    {
                                        _listView.Focus();
                                    }
                                }
                                catch (Exception ex) { Debug.WriteLine($"NAV finally deferred focus failed: {ex.Message}"); }
                            }));
                        }
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"NAV finally focus failed: {ex.Message}"); }
            try { _listView?.Invalidate(); } catch { }
            NavigationDebugLogger.Log($"NAV#{navTraceId} EXIT totalMs={totalSw.ElapsedMilliseconds}");
            ResetNavigationState();
        }
    }

    private void ResetNavigationState()
    {
        _nav.IsNavigating = false;

        var (pendingPath, pendingSelect) = _nav.DequeuePending();
        if (!string.IsNullOrEmpty(pendingPath))
        {
            NavigationDebugLogger.Log($"NAV QUEUE-DEQUEUE next=\"{TraceText(pendingPath)}\" selectCount={pendingSelect?.Count ?? 0}");
        }

        if (!string.IsNullOrEmpty(pendingPath))
        {
            ObserveTask(NavigateTo(pendingPath, pendingSelect), "NAV queue-next");
        }
    }

    private async Task LoadShellFolder(string shellPath)
    {
        long navTraceId = Interlocked.Increment(ref _navigationTraceSeq);
        var totalSw = Stopwatch.StartNew();
        var gcStart = AppSettings.Current.DebugNavigationGcStats ? CaptureGcSnapshot() : ((int, int, int, long)?)null;
        NavigationDebugLogger.Log($"SHELL#{navTraceId} START path=\"{TraceText(shellPath)}\"");

        // Cancel previous load
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();

        try
        {
            var swEnum = Stopwatch.StartNew();
            _allItems = await AwaitWithCancellation(GetShellItemsAsync(shellPath, _loadCts.Token), _loadCts.Token);
            swEnum.Stop();
            NavigationDebugLogger.Log($"SHELL#{navTraceId} ENUM ms={swEnum.ElapsedMilliseconds} count={_allItems.Count}");
            QueueGenericIconsWarmup(_allItems);

            var swSort = Stopwatch.StartNew();
            _items = await Task.Run(() =>
            {
                FileSystemService.SortItems(_allItems, _sortColumn, _sortDirection);
                return new List<FileItem>(_allItems);
            }, _loadCts.Token);
            swSort.Stop();
            NavigationDebugLogger.Log($"SHELL#{navTraceId} SORT ms={swSort.ElapsedMilliseconds} count={_items.Count} sort={_sortColumn}/{_sortDirection}");

            // Bind items to list view (no selection to restore for shell paths)
            BindItemsToListView(navTraceId, "SHELL", shellPath, null, totalSw, gcStart);
        }
        catch (OperationCanceledException)
        {
            NavigationDebugLogger.Log($"SHELL#{navTraceId} CANCELED totalMs={totalSw.ElapsedMilliseconds}");
            LogGcDelta(navTraceId, "SHELL", gcStart);
        }
        catch (Exception ex)
        {
            NavigationDebugLogger.Log($"SHELL#{navTraceId} ERROR totalMs={totalSw.ElapsedMilliseconds} message=\"{TraceText(ex.Message)}\"");
            LogGcDelta(navTraceId, "SHELL", gcStart);
            _statusLabel.Text = string.Format(Localization.T("status_error"), ex.Message);
        }
    }

    /// <summary>
    /// Shared bind pipeline used by both NavigateTo and LoadShellFolder.
    /// Handles: suspend/resume redraw, BeginUpdate/EndUpdate, virtual list size or tile population,
    /// selection restoration (pathsToSelect -> last selection -> default first item),
    /// viewport normalization, focus, invalidate, status text, and diagnostic logging.
    /// </summary>
    private void BindItemsToListView(
        long navTraceId,
        string scope,
        string path,
        List<string>? pathsToSelect,
        Stopwatch totalSw,
        (int, int, int, long)? gcStart)
    {
        var swBind = Stopwatch.StartNew();
        LogUiQueueDelayAsync(navTraceId, scope, "pre-bind");
        _listView.BeginUpdate();
        try
        {
            _listView.SelectedIndices.Clear();
            SetListSelectionAnchor(-1);
            if (_pendingClearFromThisPc)
            {
                _listView.Items.Clear();
                _listView.VirtualListSize = 0;
                _pendingClearFromThisPc = false;
            }
            if (IsTileView)
            {
                PopulateTileItems();
            }
            else
            {
                // Two-step virtual size reset is more stable for viewport math after empty/non-empty transitions.
                _listView.VirtualListSize = 0;
                _listView.VirtualListSize = _items.Count;
            }

            if (_items.Count > 0)
            {
                bool restored = false;

                // 1. Try to select explicitly requested paths
                if (pathsToSelect != null && pathsToSelect.Any())
                {
                    foreach (var p in pathsToSelect)
                    {
                        var index = _items.FindIndex(x => x.FullPath.Equals(p, StringComparison.OrdinalIgnoreCase) || x.Name.Equals(Path.GetFileName(p), StringComparison.OrdinalIgnoreCase));
                        if (index >= 0)
                        {
                            if (IsTileView && index >= _listView.Items.Count)
                                continue;
                            _listView.SelectedIndices.Add(index);
                            if (!restored)
                            {
                                FocusAndAnchorListIndex(index);
                                restored = true;
                            }
                        }
                    }
                }

                // 2. Try to restore last known selection for this path
                if (!restored)
                {
                    if (_nav.LastSelection.TryGetValue(path, out var lastSelectedName))
                    {
                        var index = _items.FindIndex(x => x.Name == lastSelectedName);
                        if (index >= 0)
                        {
                            if (!IsTileView || index < _listView.Items.Count)
                            {
                                _listView.SelectedIndices.Add(index);
                                FocusAndAnchorListIndex(index);
                                restored = true;
                            }
                        }
                    }
                }

                // 3. Fallback: if pathsToSelect was given but nothing matched above, try SelectItems
                if (!restored && pathsToSelect != null && pathsToSelect.Any())
                {
                    if (!IsTileView || _listView.Items.Count >= _items.Count)
                        SelectItems(pathsToSelect);
                    if (_listView.SelectedIndices.Count > 0)
                    {
                        int index = _listView.SelectedIndices[0];
                        FocusAndAnchorListIndex(index);
                        restored = true;
                    }
                }

                // 4. Default: select first item
                if (!restored)
                {
                    _listView.SelectedIndices.Add(0);
                    FocusAndAnchorListIndex(0);
                }

                // Apply tab-specific top-item restore in the same bind pass to avoid delayed visual jumps.
                if (!string.IsNullOrEmpty(_pendingTabTopRestorePath) &&
                    string.Equals(_pendingTabTopRestorePath, path, StringComparison.OrdinalIgnoreCase) &&
                    _pendingTabTopRestoreIndex >= 0 &&
                    _pendingTabTopRestoreIndex < _items.Count)
                {
                    try
                    {
                        if (!IsTileView && _pendingTabTopRestoreIndex < _listView.Items.Count)
                            _listView.TopItem = _listView.Items[_pendingTabTopRestoreIndex];
                        else
                            _listView.EnsureVisible(_pendingTabTopRestoreIndex);
                    }
                    catch { }
                    finally
                    {
                        _pendingTabTopRestorePath = null;
                        _pendingTabTopRestoreIndex = -1;
                    }
                }
            }
        }
        finally
        {
            _listView.EndUpdate();

            // Run viewport normalization while redraw is still suspended to avoid visible jumps.
            NormalizeViewportAfterBind(navTraceId, scope);
        }

        bool preserveAiPanelFocus = _llmChatPanel != null && _llmChatPanel.IsExpanded;
        if (!preserveAiPanelFocus)
            _listView.Focus();
        _listView.Invalidate();
        try { _listView.Update(); } catch (Exception ex) { Debug.WriteLine($"{scope} Update failed: {ex.Message}"); }
        if (_listView.IsHandleCreated)
        {
            try
            {
                const uint RDW_INVALIDATE = 0x0001;
                const uint RDW_ALLCHILDREN = 0x0080;
                const uint RDW_UPDATENOW = 0x0100;
                RedrawWindow(_listView.Handle, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_ALLCHILDREN | RDW_UPDATENOW);
            }
            catch (Exception ex) { Debug.WriteLine($"{scope} RedrawWindow post-bind failed: {ex.Message}"); }
        }
        LogListViewState($"{scope}#{navTraceId}", "post-bind-pre-reset");
        swBind.Stop();
        NavigationDebugLogger.Log($"{scope}#{navTraceId} BIND ms={swBind.ElapsedMilliseconds} selected={_listView.SelectedIndices.Count}");
        LogUiQueueDelayAsync(navTraceId, scope, "post-bind");
        StartPostBindProbe(navTraceId, scope);
        LogGcDelta(navTraceId, scope, gcStart);

        if (!IsSearchMode &&
            !IsShellPath(path) &&
            !string.Equals(scope, "NAVCACHE_SEARCH", StringComparison.Ordinal))
            _tabsController.SyncPathSnapshot(path, _items, _allItems);

        _statusLabel.Text = string.Format(Localization.T("status_loaded"), totalSw.ElapsedMilliseconds, _items.Count);
        EnsureListViewportAndPaint($"{scope}-post");
        NavigationDebugLogger.Log($"{scope}#{navTraceId} DONE totalMs={totalSw.ElapsedMilliseconds} items={_items.Count}");
        if (_retryLoadPending && !IsDriveItemsOnly()) _retryLoadPending = false;
    }

    private void UpdateWatcher(string path)
    {
        _watcherController.UpdateWatcher(path);
    }

    private void RequestWatcherRefresh()
    {
        _watcherController.RequestWatcherRefresh();
    }

    private void SearchAndSelect(char c)
        => _listViewInteractionController.SearchAndSelect(c);

    private void SortAndRefresh()
        => _listViewInteractionController.SortAndRefresh();

    private void GoBack()
    {
        _nav.GoBack();
    }

    private void GoForward()
    {
        _nav.GoForward();
    }

    private void GoUp()
    {
        if (string.IsNullOrEmpty(_currentPath) || _currentPath == ThisPcPath) return;
        if (IsShellPath(_currentPath))
        {
            var shellParent = GetShellParentPath(_currentPath);
            ObserveTask(NavigateTo(shellParent ?? ThisPcPath), "GoUp shell-parent");
            return;
        }
        var parent = Directory.GetParent(_currentPath);
        if (parent != null)
            ObserveTask(NavigateTo(parent.FullName), "GoUp parent");
        else
            ObserveTask(NavigateTo(ThisPcPath), "GoUp this-pc");
    }

    private void SetListSelectionAnchor(int index)
    {
        if (_listView == null || _listView.IsDisposed || !_listView.IsHandleCreated)
            return;
        try
        {
            const int LVM_SETSELECTIONMARK = 0x1043;
            SendMessage(_listView.Handle, LVM_SETSELECTIONMARK, 0, index);
        }
        catch { }
    }

    private void FocusAndAnchorListIndex(int index, bool ensureVisible = true)
    {
        if (_listView == null || _listView.IsDisposed)
            return;
        if (index < 0 || index >= _items.Count)
            return;

        try { _listView.FocusedItem = _listView.Items[index]; } catch { }
        SetListSelectionAnchor(index);
        if (ensureVisible)
        {
            try { _listView.EnsureVisible(index); } catch { }
        }
    }

    public async Task RefreshCurrentAsync(List<string>? selectPaths = null)
    {
        // Simple refresh logic
        if (IsShellPath(_currentPath))
        {
            await NavigateTo(_currentPath, selectPaths);
            return;
        }

        if (string.IsNullOrEmpty(_currentPath) || (_currentPath != ThisPcPath && !Directory.Exists(_currentPath)))
        {
            await NavigateTo(ThisPcPath, selectPaths);
            return;
        }

        if (IsSearchMode)
        {
            // Rerun search if possible, or just clear
            if (!string.IsNullOrEmpty(_searchBox.Text) && _searchBox.Text != Localization.T("search_placeholder"))
                _searchController.StartSearch(_searchBox.Text);
            else
                await NavigateTo(_currentPath, selectPaths);
        }
        else
        {
            await NavigateTo(_currentPath, selectPaths);
        }
    }

    private void NormalizeViewportAfterBind(long navTraceId, string scope)
    {
        if (_listView == null || _listView.IsDisposed || !_listView.IsHandleCreated)
            return;
        if (IsTileView || !_listView.VirtualMode || _items.Count <= 0)
            return;

        const int LVM_GETTOPINDEX = 0x1027;
        const int LVM_ENSUREVISIBLE = 0x1013;
        const int WM_VSCROLL = 0x0115;
        const int SB_TOP = 6;

        int top = SendMessage(_listView.Handle, LVM_GETTOPINDEX, 0, 0);
        if (top >= 0 && top < _items.Count)
            return;

        int target = _listView.SelectedIndices.Count > 0 ? _listView.SelectedIndices[0] : 0;
        if (target < 0 || target >= _items.Count)
            target = 0;

        NavigationDebugLogger.Log($"{scope}#{navTraceId} VIEWPORT_FIX top={top} count={_items.Count} sel={target}");
        try { SendMessage(_listView.Handle, WM_VSCROLL, SB_TOP, 0); } catch { }
        try { SendMessage(_listView.Handle, LVM_ENSUREVISIBLE, target, 0); } catch { }
        try { _listView.EnsureVisible(target); } catch { }

        int after = SendMessage(_listView.Handle, LVM_GETTOPINDEX, 0, 0);
        if (after >= 0 && after < _items.Count)
            return;

        NavigationDebugLogger.Log($"{scope}#{navTraceId} VIEWPORT_FIX_REBIND top={after} count={_items.Count} sel={target}");
        _listView.BeginUpdate();
        try
        {
            _listView.VirtualListSize = 0;
            _listView.VirtualListSize = _items.Count;
            _listView.SelectedIndices.Clear();
            SetListSelectionAnchor(-1);
            _listView.SelectedIndices.Add(target);
            FocusAndAnchorListIndex(target, ensureVisible: false);
        }
        finally
        {
            _listView.EndUpdate();
        }

        try { _listView.EnsureVisible(target); } catch { }
        int done = SendMessage(_listView.Handle, LVM_GETTOPINDEX, 0, 0);
        NavigationDebugLogger.Log($"{scope}#{navTraceId} VIEWPORT_FIX_DONE top={done} count={_items.Count} sel={target}");
        if (done < 0 || done >= _items.Count)
            HardResetViewport(navTraceId, scope, target);
    }

    private void HardResetViewport(long navTraceId, string scope, int target)
    {
        if (_listView == null || _listView.IsDisposed || !_listView.IsHandleCreated)
            return;
        if (IsTileView || !_listView.VirtualMode || _items.Count <= 0)
            return;

        const int LVM_GETTOPINDEX = 0x1027;
        const int LVM_ENSUREVISIBLE = 0x1013;
        const int WM_VSCROLL = 0x0115;
        const int SB_TOP = 6;

        if (target < 0 || target >= _items.Count)
            target = 0;

        NavigationDebugLogger.Log($"{scope}#{navTraceId} VIEWPORT_HARD_RESET start count={_items.Count} sel={target}");

        // Step 1: safe scroll normalization.
        try { SendMessage(_listView.Handle, WM_VSCROLL, SB_TOP, 0); } catch { }
        try { SendMessage(_listView.Handle, LVM_ENSUREVISIBLE, target, 0); } catch { }
        try { _listView.EnsureVisible(target); } catch { }

        int top = SendMessage(_listView.Handle, LVM_GETTOPINDEX, 0, 0);
        if (top >= 0 && top < _items.Count)
        {
            NavigationDebugLogger.Log($"{scope}#{navTraceId} VIEWPORT_HARD_RESET ok top={top}");
            return;
        }

        // Step 2: reset virtual binding while preserving selection target.
        try
        {
            _listView.BeginUpdate();
            try
            {
                _listView.SelectedIndices.Clear();
                SetListSelectionAnchor(-1);
                _listView.VirtualListSize = 0;
                _listView.VirtualListSize = _items.Count;
                _listView.SelectedIndices.Add(target);
                FocusAndAnchorListIndex(target, ensureVisible: false);
            }
            finally
            {
                _listView.EndUpdate();
            }
        }
        catch { }

        try { SendMessage(_listView.Handle, WM_VSCROLL, SB_TOP, 0); } catch { }
        try { SendMessage(_listView.Handle, LVM_ENSUREVISIBLE, target, 0); } catch { }
        try { _listView.EnsureVisible(target); } catch { }
        try { _listView.Invalidate(); _listView.Update(); } catch { }

        int done = SendMessage(_listView.Handle, LVM_GETTOPINDEX, 0, 0);
        NavigationDebugLogger.Log($"{scope}#{navTraceId} VIEWPORT_HARD_RESET done top={done} count={_items.Count} sel={target}");
        if (done >= 0 && done < _items.Count)
            return;

        // Final fallback: re-create the ListView handle (same effect as window reopen, but localized).
        NavigationDebugLogger.Log($"{scope}#{navTraceId} VIEWPORT_HARD_RESET_RECREATE start top={done} count={_items.Count} sel={target}");
        try
        {
            typeof(Control)
                .GetMethod("RecreateHandle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(_listView, null);
        }
        catch (Exception ex)
        {
            NavigationDebugLogger.Log($"{scope}#{navTraceId} VIEWPORT_HARD_RESET_RECREATE failed message=\"{TraceText(ex.Message)}\"");
            return;
        }

        if (!_listView.IsHandleCreated)
            return;

        try
        {
            _listView.BeginUpdate();
            try
            {
                _listView.SelectedIndices.Clear();
                SetListSelectionAnchor(-1);
                _listView.VirtualListSize = 0;
                _listView.VirtualListSize = _items.Count;
                _listView.SelectedIndices.Add(target);
                FocusAndAnchorListIndex(target, ensureVisible: false);
            }
            finally
            {
                _listView.EndUpdate();
            }
        }
        catch { }

        try { SendMessage(_listView.Handle, WM_VSCROLL, SB_TOP, 0); } catch { }
        try { SendMessage(_listView.Handle, LVM_ENSUREVISIBLE, target, 0); } catch { }
        try { _listView.EnsureVisible(target); } catch { }
        try { _listView.Invalidate(); _listView.Update(); } catch { }

        int doneAfterRecreate = SendMessage(_listView.Handle, LVM_GETTOPINDEX, 0, 0);
        NavigationDebugLogger.Log($"{scope}#{navTraceId} VIEWPORT_HARD_RESET_RECREATE done top={doneAfterRecreate} count={_items.Count} sel={target}");
    }

    private void EnsureListViewportAndPaint(string scope)
    {
        if (_listView == null || _listView.IsDisposed || !_listView.IsHandleCreated)
            return;

        BeginInvoke((Action)(() =>
        {
            if (_listView == null || _listView.IsDisposed || !_listView.IsHandleCreated)
                return;

            try
            {
                if (!IsTileView && _listView.VirtualMode && _items.Count > 0)
                {
                    const int LVM_GETTOPINDEX = 0x1027;
                    int top = SendMessage(_listView.Handle, LVM_GETTOPINDEX, 0, 0);
                    if (top < 0 || top >= _items.Count)
                        NormalizeViewportAfterBind(0, scope);
                }
            }
            catch (Exception ex) { Debug.WriteLine($"EnsureListViewportAndPaint viewport check failed: {ex.Message}"); }

            try
            {
                const uint RDW_INVALIDATE = 0x0001;
                const uint RDW_ALLCHILDREN = 0x0080;
                const uint RDW_UPDATENOW = 0x0100;
                RedrawWindow(_listView.Handle, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_ALLCHILDREN | RDW_UPDATENOW);
            }
            catch (Exception ex) { Debug.WriteLine($"EnsureListViewportAndPaint RedrawWindow failed: {ex.Message}"); }

            try
            {
                _listView.Invalidate();
                _listView.Update();
            }
            catch (Exception ex) { Debug.WriteLine($"EnsureListViewportAndPaint Invalidate/Update failed: {ex.Message}"); }
        }));
    }

    internal void StabilizeStartupVirtualViewport()
    {
        if (_startupListStabilized)
            return;
        _startupListStabilized = true;

        if (_listView == null || _listView.IsDisposed || !_listView.IsHandleCreated)
            return;
        if (IsTileView || !_listView.VirtualMode || _items.Count <= 0)
            return;

        int selected = 0;
        try
        {
            if (_listView.SelectedIndices.Count > 0)
                selected = _listView.SelectedIndices[0];
        }
        catch { }
        if (selected < 0 || selected >= _items.Count)
            selected = 0;

        try
        {
            _listView.BeginUpdate();
            try
            {
                _listView.SelectedIndices.Clear();
                SetListSelectionAnchor(-1);
                _listView.VirtualListSize = 0;
                _listView.VirtualListSize = _items.Count;
                _listView.SelectedIndices.Add(selected);
                FocusAndAnchorListIndex(selected, ensureVisible: false);
            }
            finally
            {
                _listView.EndUpdate();
            }

            try { _listView.EnsureVisible(selected); } catch { }
            _listView.Invalidate();
            _listView.Update();
            LogListViewState("STARTUP", "stabilize");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"StabilizeStartupVirtualViewport failed: {ex.Message}");
        }
    }

    private void QueueGenericIconsWarmup(List<FileItem> source)
    {
        if (_iconLoadService == null || source == null || source.Count == 0)
            return;

        var s = AppSettings.Current;
        if (!s.ShowIcons || s.UseEmojiIcons)
            return;

        string prefix = s.UseSystemIcons ? "sys_" : "gray_";
        bool hasDir = false;
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int scanLimit = Math.Min(source.Count, 4000);
        for (int i = 0; i < scanLimit; i++)
        {
            var it = source[i];
            if (it.IsDirectory)
            {
                hasDir = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(it.Extension))
                continue;
            exts.Add(it.Extension);
            if (exts.Count >= 96 && hasDir)
                break;
        }

        if (hasDir)
            _iconLoadService.EnsureGenericIcon($"{prefix}folder", ".folder", true, s.UseSystemIcons);

        foreach (var ext in exts)
            _iconLoadService.EnsureGenericIcon($"{prefix}{ext}", ext, false, s.UseSystemIcons);

        if (s.ShowThumbnails)
            _iconLoadService.EnsureGenericIcon($"{prefix}image", ".jpg", false, s.UseSystemIcons);
    }

    private bool TryTakePendingTabCache(string path, out List<FileItem> items, out List<FileItem> allItems)
    {
        items = null!;
        allItems = null!;

        if (string.IsNullOrWhiteSpace(_pendingTabCachePath))
            return false;
        if (!string.Equals(_pendingTabCachePath, path, StringComparison.OrdinalIgnoreCase))
            return false;
        if (_pendingTabCacheItems == null || _pendingTabCacheAllItems == null)
            return false;

        items = _pendingTabCacheItems;
        allItems = _pendingTabCacheAllItems;
        _pendingTabCachePath = null;
        _pendingTabCacheItems = null;
        _pendingTabCacheAllItems = null;
        _pendingTabCacheIsSearchMode = false;
        return true;
    }
}
