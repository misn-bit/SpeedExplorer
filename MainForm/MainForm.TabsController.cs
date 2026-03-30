using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class TabsController
    {
        private readonly MainForm _owner;

        private readonly List<TabState> _tabs = new();
        private int _activeTabIndex = -1;

        private FlowLayoutPanel? _tabStrip;
        private Panel? _titleBar;
        private Panel? _windowButtonsPanel;

        private readonly Dictionary<string, Panel> _tabPanelsById = new();
        private string _tabDragId = "";
        private Point _tabDragStart;

        private Button? _addTabButton;
        private Button? _tabOverflowButton;
        private int _tabLoadRequestId;
        private string? _pendingSearchRestoreTabId;

        public int Count => _tabs.Count;
        public int ActiveIndex => _activeTabIndex;

        public TabsController(MainForm owner)
        {
            _owner = owner;
        }

        public void AttachTabStrip(FlowLayoutPanel tabStrip, Panel titleBar, Panel windowButtonsPanel)
        {
            _tabStrip = tabStrip;
            _titleBar = titleBar;
            _windowButtonsPanel = windowButtonsPanel;

            EnableDoubleBuffering(_tabStrip);
            _tabStrip.AllowDrop = true;
            _tabStrip.DragOver += TabStrip_DragOver;
            _tabStrip.DragDrop += TabStrip_DragDrop;
            _tabStrip.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) _owner.TitleBarMouseDown(e.Location); };
            _tabStrip.MouseUp += (s, e) => _owner.TitleBarMouseUp();
            _tabStrip.MouseMove += (s, e) => _owner.TitleBarMouseMove(_tabStrip, e);
            _tabStrip.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) _owner.ToggleMaximize(); };
            _tabStrip.MouseWheel += (s, e) =>
            {
                if (_tabs.Count == 0) return;
                int dir = e.Delta > 0 ? -1 : 1;
                int next = (_activeTabIndex + dir + _tabs.Count) % _tabs.Count;
                SwitchToTab(next);
            };

            _tabStrip.Resize += (s, e) => UpdateTabStripLayout();
            _titleBar.Resize += (s, e) => UpdateTabStripLayout();
        }

        public void InitializeTabs(string? initialPath)
        {
            if (_tabStrip == null) return;

            _tabs.Clear();
            _tabStrip.Controls.Clear();

            var startPath = initialPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var initialSort = ResolveInitialSortForPath(startPath);
            var tab = new TabState
            {
                CurrentPath = startPath,
                CurrentDisplayPath = startPath,
                Title = GetTabTitleForPath(startPath, false),
                SortColumn = initialSort.Column,
                SortDirection = initialSort.Direction,
                TaggedFilesOnTop = initialSort.TaggedOnTop
            };
            _tabs.Add(tab);
            _activeTabIndex = 0;

            _addTabButton = new Button
            {
                Text = "+",
                Size = new Size(_owner.Scale(30), _owner.Scale(38)),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = _owner.ForeColor_Dark,
                Font = new Font("Segoe UI Semibold", 12),
                Cursor = Cursors.Hand,
                Margin = new Padding(_owner.Scale(1), _owner.Scale(1), 0, 0)
            };
            _addTabButton.FlatAppearance.BorderSize = 0;
            _addTabButton.FlatAppearance.MouseOverBackColor = _owner.ControlHoverColor;
            ApplyButtonTextOffset(_addTabButton, "+", -_owner.Scale(5));
            _addTabButton.Click += (s, e) => AddNewTab();

            _tabOverflowButton = new Button
            {
                Text = "⋯",
                Size = new Size(_owner.Scale(30), _owner.Scale(38)),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = _owner.ForeColor_Dark,
                Font = new Font("Segoe UI Semibold", 12),
                Cursor = Cursors.Hand,
                Margin = new Padding(_owner.Scale(1), _owner.Scale(1), 0, 0),
                Visible = false
            };
            _tabOverflowButton.FlatAppearance.BorderSize = 0;
            _tabOverflowButton.FlatAppearance.MouseOverBackColor = _owner.ControlHoverColor;
            _tabOverflowButton.Click += (s, e) => ShowTabOverflowMenu();

            RebuildTabStrip();
        }

        public void HandleExternalPath(string? rawPath, bool openImageViewer = true)
        {
            if (Program.IsExplorerShellArgument(rawPath))
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", rawPath!) { UseShellExecute = true }); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
                return;
            }
            if (string.IsNullOrWhiteSpace(rawPath)) return;

            if ((rawPath.IndexOf("/select", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 rawPath.IndexOf("-select", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var parsed = Program.ExtractStartPathFromSingleArg(rawPath);
                if (!string.IsNullOrWhiteSpace(parsed))
                    rawPath = parsed;
            }

            var imagePathForViewer = openImageViewer ? _owner.ResolveImagePathForBuiltInViewer(rawPath) : null;

            var normalized = _owner.NormalizeStartupPath(rawPath, out var selectPaths, inferRecentSelectionForDirectory: true);
            if (_owner._listView != null && !_owner._listView.IsDisposed)
                _owner._listView.Visible = true;

            int existingTabIndex = _tabs.FindIndex(t =>
                !string.IsNullOrWhiteSpace(t.CurrentPath) &&
                string.Equals(t.CurrentPath, normalized, StringComparison.OrdinalIgnoreCase));
            if (existingTabIndex >= 0)
            {
                bool hasExplicitSelection = selectPaths != null && selectPaths.Count > 0;
                if (_activeTabIndex != existingTabIndex)
                    SwitchToTab(existingTabIndex);

                bool activeAlreadyOnPath = string.Equals(_owner._currentPath, normalized, StringComparison.OrdinalIgnoreCase);
                if (activeAlreadyOnPath)
                {
                    if (hasExplicitSelection && !_owner.IsSearchMode)
                    {
                        _owner.SelectItems(selectPaths!);
                        try
                        {
                            var listView = _owner._listView;
                            if (listView != null && !listView.IsDisposed && listView.Visible && listView.CanFocus)
                                listView.Focus();
                        }
                        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
                    }
                    if (!string.IsNullOrWhiteSpace(imagePathForViewer))
                    {
                        _owner.TryOpenImageViewerForImagePath(imagePathForViewer, _owner._items.Select(static x => x.FullPath));
                    }
                    return;
                }

                _owner.ObserveTask(
                    _owner.NavigateToAndMaybeOpenImageViewerAsync(normalized, selectPaths, imagePathForViewer),
                    "TabsController.HandleExternalPath/existing-tab");
                return;
            }

            bool useNewTab = _tabs.Count > 0;
            if (useNewTab)
            {
                SaveCurrentTabState();
                var initialSort = ResolveInitialSortForPath(normalized);
                var tab = new TabState
                {
                    CurrentPath = normalized,
                    CurrentDisplayPath = normalized,
                    Title = GetTabTitleForPath(normalized, false),
                    SortColumn = initialSort.Column,
                    SortDirection = initialSort.Direction,
                    TaggedFilesOnTop = initialSort.TaggedOnTop
                };
                _tabs.Add(tab);
                int newIndex = _tabs.Count - 1;
                _activeTabIndex = newIndex;
                RebuildTabStrip();
                _tabStrip?.Refresh();
                _titleBar?.Refresh();
                SwitchToTab(newIndex, force: true, saveCurrent: false, skipNavigation: true);
                _owner.ObserveTask(
                    _owner.NavigateToAndMaybeOpenImageViewerAsync(normalized, selectPaths, imagePathForViewer),
                    "TabsController.HandleExternalPath/new-tab");
            }
            else
            {
                _owner.ObserveTask(
                    _owner.NavigateToAndMaybeOpenImageViewerAsync(normalized, selectPaths, imagePathForViewer),
                    "TabsController.HandleExternalPath/first-tab");
            }
        }

        public void AddNewTab()
        {
            SaveCurrentTabState();
            var startPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var initialSort = ResolveInitialSortForPath(startPath);
            var tab = new TabState
            {
                CurrentPath = startPath,
                CurrentDisplayPath = startPath,
                Title = GetTabTitleForPath(startPath, false),
                SortColumn = initialSort.Column,
                SortDirection = initialSort.Direction,
                TaggedFilesOnTop = initialSort.TaggedOnTop
            };
            _tabs.Add(tab);
            int newIndex = _tabs.Count - 1;
            _activeTabIndex = newIndex;
            RebuildTabStrip();
            _tabStrip?.Refresh();
            _titleBar?.Refresh();
            SwitchToTab(newIndex, force: true, saveCurrent: false);
        }

        public void OpenPathInNewTab(
            string path,
            bool activate = true,
            Stack<string>? inheritedBackHistory = null,
            Stack<string>? inheritedForwardHistory = null)
        {
            SaveCurrentTabState();
            var initialSort = ResolveInitialSortForPath(path);
            var tab = new TabState
            {
                CurrentPath = path,
                CurrentDisplayPath = path,
                Title = GetTabTitleForPath(path, false),
                SortColumn = initialSort.Column,
                SortDirection = initialSort.Direction,
                TaggedFilesOnTop = initialSort.TaggedOnTop,
                BackHistory = inheritedBackHistory != null ? CloneStack(inheritedBackHistory) : new Stack<string>(),
                ForwardHistory = inheritedForwardHistory != null ? CloneStack(inheritedForwardHistory) : new Stack<string>()
            };
            _tabs.Add(tab);
            int newIndex = _tabs.Count - 1;

            if (activate)
            {
                _activeTabIndex = newIndex;
                RebuildTabStrip();
                _tabStrip?.Refresh();
                _titleBar?.Refresh();
                SwitchToTab(newIndex, force: true, saveCurrent: false);
            }
            else
            {
                RebuildTabStrip();
                UpdateTabStripVisuals();
                StartBackgroundTabPreload(tab);
            }
        }

        public void CloseTab(int index)
        {
            if (index < 0 || index >= _tabs.Count) return;
            if (_tabs.Count == 1)
            {
                _owner.Close();
                return;
            }

            _tabs.RemoveAt(index);
            if (_activeTabIndex >= _tabs.Count) _activeTabIndex = _tabs.Count - 1;
            RebuildTabStrip();
            _tabStrip?.Refresh();
            _titleBar?.Refresh();
            SwitchToTab(_activeTabIndex, force: true, saveCurrent: false);
        }

        public void SwitchToTab(int index, bool force = false, bool saveCurrent = true, bool skipNavigation = false)
        {
            if (index < 0 || index >= _tabs.Count) return;
            if (index == _activeTabIndex && !force) return;
            _owner.LogListViewState("TAB", $"switch-begin target={index} current={_activeTabIndex}");

            if (saveCurrent) SaveCurrentTabState();
            _activeTabIndex = index;

            if (ShouldShowTabOverflow())
                RebuildTabStrip();
            else
                UpdateTabStripVisuals();

            if (skipNavigation)
            {
                UpdateActiveTabTitle();
            }
            else
            {
                LoadTabState(_tabs[index]);
            }

            _owner.LogListViewState("TAB", $"switch-end active={_activeTabIndex}");
        }

        public void SaveCurrentTabState()
        {
            if (_activeTabIndex < 0 || _activeTabIndex >= _tabs.Count) return;
            var tab = _tabs[_activeTabIndex];
            tab.CurrentPath = _owner._currentPath;
            tab.CurrentDisplayPath = _owner._currentDisplayPath;
            tab.IsSearchMode = _owner.IsSearchMode;
            tab.SearchText = _owner._searchBox.Text;
            tab.SortColumn = _owner._sortColumn;
            tab.SortDirection = _owner._sortDirection;
            tab.TaggedFilesOnTop = _owner._taggedFilesOnTop;
            tab.LastSelection = new Dictionary<string, string>(_owner._nav.LastSelection);
            tab.FolderSortSettings = new Dictionary<string, (SortColumn Column, SortDirection Direction)>(_owner._nav.FolderSortSettings);
            tab.BackHistory = CloneStack(_owner._nav.BackHistory);
            tab.ForwardHistory = CloneStack(_owner._nav.ForwardHistory);
            tab.IsShellMode = _owner._isShellMode;
            tab.CurrentShellId = ShellNavigationController.IsShellIdPath(_owner._currentPath) ? _owner._currentPath : "";
            tab.Title = GetTabTitleForPath(_owner._currentPath, tab.IsSearchMode);
            tab.CachedPath = _owner._currentPath;
            tab.CachedItems = _owner._items;
            tab.CachedAllItems = _owner._allItems;
            tab.HasCachedSnapshot = true;
            CaptureListState(tab);
        }

        public void LoadTabState(TabState tab)
        {
            int requestId = ++_tabLoadRequestId;
            _ = LoadTabStateAsync(tab, requestId);
        }

        private async Task LoadTabStateAsync(TabState tab, int requestId)
        {
            _owner.LogListViewState("TAB", $"load-begin req={requestId} path=\"{TraceText(tab.CurrentPath)}\" search={tab.IsSearchMode}");
            bool restoreSearchMode = tab.IsSearchMode;
            string restoreSearchText = tab.SearchText;
            _pendingSearchRestoreTabId = restoreSearchMode ? tab.Id : null;
            _owner._nav.LastSelection = new Dictionary<string, string>(tab.LastSelection);
            _owner._nav.BackHistory = CloneStack(tab.BackHistory);
            _owner._nav.ForwardHistory = CloneStack(tab.ForwardHistory);
            List<string>? restoreSelection = null;
            if (!restoreSearchMode && tab.SelectedPaths.Count > 0)
            {
                restoreSelection = new List<string>(tab.SelectedPaths);
            }

            bool useCachedSnapshot =
                !IsShellPath(tab.CurrentPath) &&
                tab.HasCachedSnapshot &&
                !string.IsNullOrWhiteSpace(tab.CachedPath) &&
                string.Equals(tab.CachedPath, tab.CurrentPath, StringComparison.OrdinalIgnoreCase) &&
                tab.CachedItems != null &&
                tab.CachedAllItems != null;

            if (useCachedSnapshot)
            {
                _owner._pendingTabCachePath = tab.CurrentPath;
                _owner._pendingTabCacheItems = tab.CachedItems;
                _owner._pendingTabCacheAllItems = tab.CachedAllItems;
                _owner._pendingTabCacheIsSearchMode = restoreSearchMode;
            }
            else
            {
                _owner._pendingTabCachePath = null;
                _owner._pendingTabCacheItems = null;
                _owner._pendingTabCacheAllItems = null;
                _owner._pendingTabCacheIsSearchMode = false;
            }

            if (!restoreSearchMode && tab.TopItemIndex >= 0)
            {
                _owner._pendingTabTopRestorePath = tab.CurrentPath;
                _owner._pendingTabTopRestoreIndex = tab.TopItemIndex;
            }
            else
            {
                _owner._pendingTabTopRestorePath = null;
                _owner._pendingTabTopRestoreIndex = -1;
            }

            if (useCachedSnapshot)
            {
                RestoreCachedTabState(tab, restoreSelection, restoreSearchMode, restoreSearchText);
                _owner._pendingTabTopRestorePath = null;
                _owner._pendingTabTopRestoreIndex = -1;
                _owner._pendingTabCachePath = null;
                _owner._pendingTabCacheItems = null;
                _owner._pendingTabCacheAllItems = null;
                _owner._pendingTabCacheIsSearchMode = false;
                _pendingSearchRestoreTabId = null;
                return;
            }

            _owner._suppressHistoryUpdate = true;
            try
            {
                await _owner.NavigateTo(
                    tab.CurrentPath,
                    restoreSelection,
                    tab.SortColumn,
                    tab.SortDirection,
                    tab.TaggedFilesOnTop);
            }
            finally
            {
                _owner._suppressHistoryUpdate = false;
                _owner._pendingTabTopRestorePath = null;
                _owner._pendingTabTopRestoreIndex = -1;
                _owner._pendingTabCachePath = null;
                _owner._pendingTabCacheItems = null;
                _owner._pendingTabCacheAllItems = null;
                _owner._pendingTabCacheIsSearchMode = false;
            }

            if (requestId != _tabLoadRequestId)
                return;
            if (_activeTabIndex < 0 || _activeTabIndex >= _tabs.Count)
                return;
            if (!ReferenceEquals(_tabs[_activeTabIndex], tab))
                return;
            _owner.LogListViewState("TAB", $"load-after-nav req={requestId}");

            if (restoreSearchMode && !IsShellPath(tab.CurrentPath) &&
                !string.IsNullOrWhiteSpace(restoreSearchText) &&
                restoreSearchText != Localization.T("search_placeholder"))
            {
                _owner._suppressSearchTextChanged = true;
                try
                {
                    _owner._searchBox.Text = restoreSearchText;
                    _owner._searchBox.ForeColor = _owner.ForeColor_Dark;
                }
                finally
                {
                    _owner._suppressSearchTextChanged = false;
                }

                if (useCachedSnapshot)
                {
                    _owner._searchController.RestoreCachedSearchState(restoreSearchText);
                    _owner.LogListViewState("TAB", $"load-after-search-cache req={requestId}");
                }
                else
                {
                    _owner._searchController.StartSearch(restoreSearchText);
                    _owner.LogListViewState("TAB", $"load-after-search-start req={requestId}");
                }

                if (string.Equals(_pendingSearchRestoreTabId, tab.Id, StringComparison.Ordinal))
                    _pendingSearchRestoreTabId = null;
            }
            else if (string.Equals(_pendingSearchRestoreTabId, tab.Id, StringComparison.Ordinal))
            {
                _pendingSearchRestoreTabId = null;
            }

            // Force a clean repaint to avoid stale virtual list viewport artifacts.
            _owner.BeginInvoke((Action)(() =>
            {
                if (_owner._listView == null || _owner._listView.IsDisposed || !_owner._listView.IsHandleCreated)
                    return;
                try
                {
                    _owner._listView.Invalidate();
                    _owner._listView.Update();
                }
                catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
                _owner.LogListViewState("TAB", $"load-end req={requestId}");
            }));
        }

        private void RestoreCachedTabState(
            TabState tab,
            List<string>? restoreSelection,
            bool restoreSearchMode,
            string restoreSearchText)
        {
            string path = tab.CurrentPath;
            var redrawScope = new Control?[]
            {
                _owner._sidebar,
                _owner._listView,
                _owner._addressBar,
                _tabStrip,
                _titleBar,
                _owner._statusBar
            };

            SetRedraw(redrawScope, enabled: false);
            try
            {
                _owner._searchController.TryCancelActiveSearch();
                _owner._isShellMode = tab.IsShellMode;
                _owner._sortColumn = tab.SortColumn;
                _owner._sortDirection = tab.SortDirection;
                _owner._taggedFilesOnTop = tab.TaggedFilesOnTop;
                _owner._currentPath = path;
                _owner._currentDisplayPath = string.IsNullOrWhiteSpace(tab.CurrentDisplayPath) ? path : tab.CurrentDisplayPath;

                UpdateWindowTitle(path, _owner._currentDisplayPath);
                SyncSidebarSelection(path);
                _owner.UpdateWatcher(path);
                _owner.UpdateBreadcrumbs(path);
                _owner._addressTextBox.Text = path;

                _owner._suppressSearchTextChanged = true;
                try
                {
                    _owner._searchBox.Enabled = true;
                    if (restoreSearchMode &&
                        !string.IsNullOrWhiteSpace(restoreSearchText) &&
                        restoreSearchText != Localization.T("search_placeholder"))
                    {
                        _owner._searchBox.Text = restoreSearchText;
                        _owner._searchBox.ForeColor = _owner.ForeColor_Dark;
                    }
                    else
                    {
                        _owner._searchBox.Text = Localization.T("search_placeholder");
                        _owner._searchBox.ForeColor = Color.Gray;
                    }
                }
                finally
                {
                    _owner._suppressSearchTextChanged = false;
                }

                _owner._allItems = tab.CachedAllItems ?? new List<FileItem>();
                _owner._items = tab.CachedItems ?? new List<FileItem>();

                if (path == ThisPcPath && !restoreSearchMode)
                    _owner.SetupDriveColumns(_owner._listView);
                else
                    _owner.SetupFileColumns(_owner._listView);

                RestoreListViewSnapshot(path, restoreSelection, tab.TopItemIndex);

                if (restoreSearchMode &&
                    !string.IsNullOrWhiteSpace(restoreSearchText) &&
                    restoreSearchText != Localization.T("search_placeholder"))
                {
                    _owner._searchController.RestoreCachedSearchState(restoreSearchText);
                }
                else
                {
                    _owner._searchController.ExitSearchModeOnNavigate();
                    _owner._statusLabel.Text = string.Format(Localization.T("status_ready_items"), _owner._items.Count);
                }

                SyncActiveTabPath(_owner._currentPath, _owner._currentDisplayPath);
                _owner.RefreshSearchOverlayVisibility();
            }
            finally
            {
                SetRedraw(redrawScope, enabled: true);
            }

            try
            {
                _owner._listView.Refresh();
                _owner._addressBar?.Refresh();
                _owner._statusBar?.Refresh();
                _tabStrip?.Refresh();
                _titleBar?.Refresh();
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }

            _owner.LogListViewState("TAB", $"load-fast-cache path=\"{TraceText(path)}\" count={_owner._items.Count}");
        }

        private static void SetRedraw(IEnumerable<Control?> controls, bool enabled)
        {
            const uint RDW_INVALIDATE = 0x0001;
            const uint RDW_ALLCHILDREN = 0x0080;
            const uint RDW_UPDATENOW = 0x0100;

            foreach (var control in controls)
            {
                if (control == null || control.IsDisposed || !control.IsHandleCreated)
                    continue;

                try
                {
                    SendMessage(control.Handle, WM_SETREDRAW, enabled ? 1 : 0, 0);
                    if (enabled)
                        RedrawWindow(control.Handle, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_ALLCHILDREN | RDW_UPDATENOW);
                }
                catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
            }
        }

        private void RestoreListViewSnapshot(string path, List<string>? restoreSelection, int topItemIndex)
        {
            _owner._listView.BeginUpdate();
            try
            {
                _owner._listView.SelectedIndices.Clear();
                _owner.SetListSelectionAnchor(-1);

                if (_owner.IsTileView)
                {
                    _owner.PopulateTileItems();
                }
                else
                {
                    _owner._listView.VirtualListSize = 0;
                    _owner._listView.VirtualListSize = _owner._items.Count;
                }

                if (_owner._items.Count > 0)
                {
                    bool restored = false;

                    if (restoreSelection != null && restoreSelection.Count > 0)
                    {
                        foreach (var p in restoreSelection)
                        {
                            int index = _owner._items.FindIndex(x =>
                                x.FullPath.Equals(p, StringComparison.OrdinalIgnoreCase) ||
                                x.Name.Equals(Path.GetFileName(p), StringComparison.OrdinalIgnoreCase));
                            if (index < 0)
                                continue;
                            if (_owner.IsTileView && index >= _owner._listView.Items.Count)
                                continue;

                            _owner._listView.SelectedIndices.Add(index);
                            if (!restored)
                            {
                                _owner.FocusAndAnchorListIndex(index, ensureVisible: false);
                                restored = true;
                            }
                        }
                    }

                    if (!restored && _owner._nav.LastSelection.TryGetValue(path, out var lastSelectedName))
                    {
                        int index = _owner._items.FindIndex(x => x.Name == lastSelectedName);
                        if (index >= 0 && (!_owner.IsTileView || index < _owner._listView.Items.Count))
                        {
                            _owner._listView.SelectedIndices.Add(index);
                            _owner.FocusAndAnchorListIndex(index, ensureVisible: false);
                            restored = true;
                        }
                    }

                    if (!restored)
                    {
                        _owner._listView.SelectedIndices.Add(0);
                        _owner.FocusAndAnchorListIndex(0, ensureVisible: false);
                    }

                    if (topItemIndex >= 0 && topItemIndex < _owner._items.Count)
                    {
                        try
                        {
                            if (!_owner.IsTileView && topItemIndex < _owner._listView.Items.Count)
                                _owner._listView.TopItem = _owner._listView.Items[topItemIndex];
                            else
                                _owner._listView.EnsureVisible(topItemIndex);
                        }
                        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
                    }
                }
            }
            finally
            {
                _owner._listView.EndUpdate();
            }

            bool preserveAiPanelFocus = _owner._llmChatPanel != null && _owner._llmChatPanel.IsExpanded;
            bool isRenaming = _owner._renameTextBox != null && !_owner._renameTextBox.IsDisposed;
            if (!preserveAiPanelFocus && !isRenaming)
                _owner._listView.Focus();
        }

        private void UpdateWindowTitle(string path, string displayPath)
        {
            if (path == ThisPcPath)
            {
                _owner.Text = Localization.T("this_pc");
                _owner._pathLabel.Text = Localization.T("this_pc");
                return;
            }

            if (IsShellPath(path))
            {
                _owner.Text = displayPath;
                _owner._pathLabel.Text = displayPath;
                return;
            }

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

            _owner.Text = folderName;
            _owner._pathLabel.Text = path;
        }

        private void SyncSidebarSelection(string path)
        {
            TreeNode? exactNode = null;
            TreeNode? driveNode = null;

            if (_owner._sidebar.SelectedNode?.Tag is string selectedPath &&
                string.Equals(selectedPath, path, StringComparison.OrdinalIgnoreCase))
            {
                exactNode = _owner._sidebar.SelectedNode;
            }

            foreach (TreeNode node in _owner._sidebar.Nodes)
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
            if (sidebarTarget != null && _owner._sidebar.SelectedNode != sidebarTarget)
                _owner._sidebar.SelectedNode = sidebarTarget;
        }

        private void StartBackgroundTabPreload(TabState tab)
        {
            if (tab == null)
                return;
            if (tab.IsPreloading)
                return;
            if (tab.IsSearchMode || tab.IsShellMode)
                return;
            if (string.IsNullOrWhiteSpace(tab.CurrentPath) || tab.CurrentPath == ThisPcPath)
                return;
            if (tab.HasCachedSnapshot &&
                string.Equals(tab.CachedPath, tab.CurrentPath, StringComparison.OrdinalIgnoreCase) &&
                tab.CachedItems != null &&
                tab.CachedAllItems != null)
                return;
            if (!Directory.Exists(tab.CurrentPath))
                return;

            tab.IsPreloading = true;
            int preloadVersion = unchecked(tab.PreloadVersion + 1);
            tab.PreloadVersion = preloadVersion;
            _owner.ObserveTask(PreloadTabSnapshotAsync(tab, preloadVersion), $"TabsController.Preload/{tab.CurrentPath}");
        }

        private async Task PreloadTabSnapshotAsync(TabState tab, int preloadVersion)
        {
            string path = tab.CurrentPath;
            try
            {
                var allItems = await FileSystemService.GetFilesAsync(path, CancellationToken.None);
                await Task.Run(() => FileSystemService.SortItems(allItems, tab.SortColumn, tab.SortDirection, tab.TaggedFilesOnTop));
                var items = new List<FileItem>(allItems);

                if (_owner.IsDisposed || _owner.Disposing)
                    return;

                try
                {
                    _owner.BeginInvoke((Action)(() =>
                    {
                        if (_owner.IsDisposed || _owner.Disposing)
                            return;
                        if (!_tabs.Contains(tab))
                            return;
                        if (tab.PreloadVersion != preloadVersion)
                            return;
                        if (!string.Equals(tab.CurrentPath, path, StringComparison.OrdinalIgnoreCase))
                            return;

                        tab.CachedPath = path;
                        tab.CachedAllItems = allItems;
                        tab.CachedItems = items;
                        tab.HasCachedSnapshot = true;
                        tab.IsPreloading = false;
                    }));
                }
                catch (InvalidOperationException)
                {
                    if (_tabs.Contains(tab) &&
                        tab.PreloadVersion == preloadVersion &&
                        string.Equals(tab.CurrentPath, path, StringComparison.OrdinalIgnoreCase))
                    {
                        tab.CachedPath = path;
                        tab.CachedAllItems = allItems;
                        tab.CachedItems = items;
                        tab.HasCachedSnapshot = true;
                        tab.IsPreloading = false;
                    }
                }
            }
            catch (Exception __ex)
            {
                System.Diagnostics.Debug.WriteLine(__ex);
                if (_tabs.Contains(tab) &&
                    tab.PreloadVersion == preloadVersion)
                {
                    tab.IsPreloading = false;
                }
            }
        }

        public void UpdateActiveTabTitle()
        {
            if (_activeTabIndex < 0 || _activeTabIndex >= _tabs.Count) return;
            var tab = _tabs[_activeTabIndex];
            bool keepSearchTitle = string.Equals(_pendingSearchRestoreTabId, tab.Id, StringComparison.Ordinal);
            tab.IsSearchMode = _owner.IsSearchMode || keepSearchTitle;
            tab.SearchText = tab.IsSearchMode ? _owner._searchBox.Text : "";
            tab.Title = GetTabTitleForPath(_owner._currentPath, tab.IsSearchMode);
            UpdateTabStripVisuals();
        }

        public void SyncActiveTabPath(string currentPath, string currentDisplayPath)
        {
            if (_activeTabIndex < 0 || _activeTabIndex >= _tabs.Count)
                return;

            var tab = _tabs[_activeTabIndex];
            tab.CurrentPath = currentPath;
            tab.CurrentDisplayPath = currentDisplayPath;
            bool keepSearchTitle = string.Equals(_pendingSearchRestoreTabId, tab.Id, StringComparison.Ordinal);
            tab.IsSearchMode = _owner.IsSearchMode || keepSearchTitle;
            if (!tab.IsSearchMode)
                tab.SearchText = "";
            tab.Title = GetTabTitleForPath(currentPath, tab.IsSearchMode);
            UpdateTabStripVisuals();
        }

        public string GetTabTitleForPath(string path)
            => GetTabTitleForPath(path, false);

        public string GetTabTitleForPath(string path, bool isSearchMode)
        {
            string title;
            if (string.IsNullOrEmpty(path))
            {
                title = "Tab";
            }
            else if (path == ThisPcPath)
            {
                title = Localization.T("this_pc");
            }
            else if (IsShellPath(path))
            {
                title = _owner.GetShellDisplayName(path);
            }
            else
            {
                try
                {
                    title = new DirectoryInfo(path).Name;
                }
                catch
                {
                    title = path;
                }
            }

            if (!isSearchMode)
                return title;

            return string.Format(Localization.T("tab_title_search"), title);
        }

        private Stack<string> CloneStack(Stack<string> source) => new Stack<string>(source.Reverse());

        private (SortColumn Column, SortDirection Direction, bool TaggedOnTop) ResolveInitialSortForPath(string path)
        {
            if (string.Equals(path, ThisPcPath, StringComparison.OrdinalIgnoreCase))
                return (SortColumn.DriveNumber, SortDirection.Ascending, false);

            if (!IsShellPath(path) &&
                _owner._nav.FolderSortSettings.TryGetValue(path, out var saved))
            {
                return (saved.Column, saved.Direction, saved.Column == SortColumn.Tags);
            }

            return (SortColumn.Name, SortDirection.Ascending, false);
        }

        private void CaptureListState(TabState tab)
        {
            tab.SelectedPaths.Clear();
            tab.TopItemIndex = -1;

            if (_owner._listView == null || _owner._listView.IsDisposed)
                return;
            if (_owner._items.Count == 0)
                return;

            try
            {
                foreach (int idx in _owner._listView.SelectedIndices)
                {
                    if (idx >= 0 && idx < _owner._items.Count)
                    {
                        var p = _owner._items[idx].FullPath;
                        if (!string.IsNullOrWhiteSpace(p))
                            tab.SelectedPaths.Add(p);
                    }
                }
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }

            try
            {
                if (_owner._listView.TopItem != null)
                    tab.TopItemIndex = _owner._listView.TopItem.Index;
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }

            if (tab.TopItemIndex < 0 && _owner._listView.SelectedIndices.Count > 0)
            {
                try { tab.TopItemIndex = _owner._listView.SelectedIndices[0]; } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
            }
        }

        public void SyncPathSnapshot(string path, List<FileItem> items, List<FileItem> allItems)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            if (items == null || allItems == null)
                return;

            for (int i = 0; i < _tabs.Count; i++)
            {
                var tab = _tabs[i];
                if (tab.IsSearchMode)
                    continue;
                if (!string.Equals(tab.CurrentPath, path, StringComparison.OrdinalIgnoreCase))
                    continue;

                tab.CachedPath = path;
                tab.CachedAllItems = new List<FileItem>(allItems);
                tab.HasCachedSnapshot = true;

                if (tab.SortColumn == _owner._sortColumn &&
                    tab.SortDirection == _owner._sortDirection &&
                    tab.TaggedFilesOnTop == _owner._taggedFilesOnTop)
                {
                    tab.CachedItems = new List<FileItem>(items);
                    continue;
                }

                var sorted = new List<FileItem>(tab.CachedAllItems);
                FileSystemService.SortItems(sorted, tab.SortColumn, tab.SortDirection, tab.TaggedFilesOnTop);
                tab.CachedItems = sorted;
            }
        }

        private void RebuildTabStrip()
        {
            if (_tabStrip == null) return;
            _tabStrip.SuspendLayout();
            _tabStrip.Controls.Clear();
            _tabPanelsById.Clear();

            int tabWidth = GetTabPanelWidth();
            GetVisibleTabRange(out int startIndex, out int visibleCount);
            int endIndex = Math.Min(_tabs.Count, startIndex + visibleCount);

            for (int i = startIndex; i < endIndex; i++)
            {
                var tab = _tabs[i];
                var isActive = i == _activeTabIndex;

                var tabPanel = new Panel
                {
                    Height = _owner.Scale(38),
                    Width = tabWidth,
                    Margin = new Padding(i == startIndex ? 0 : _owner.Scale(1), _owner.Scale(1), 0, 0),
                    BackColor = isActive ? _owner.ActiveTabBackColor : _owner.InactiveTabBackColor
                };
                tabPanel.Tag = tab.Id;
                EnableDoubleBuffering(tabPanel);

                var title = new Label
                {
                    Text = tab.Title,
                    ForeColor = _owner.ForeColor_Dark,
                    AutoEllipsis = true,
                    Font = new Font("Segoe UI Semibold", 10),
                    Location = new Point(_owner.Scale(10), _owner.Scale(5)),
                    Size = new Size(Math.Max(_owner.Scale(40), tabPanel.Width - _owner.Scale(40)), _owner.Scale(20)),
                    Cursor = Cursors.Hand
                };
                title.Tag = "title";

                var close = new Label
                {
                    Text = "×",
                    ForeColor = _owner.SecondaryForeColor,
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI Symbol", 11, FontStyle.Bold),
                    Size = new Size(_owner.Scale(20), _owner.Scale(20)),
                    Location = new Point(tabPanel.Width - _owner.Scale(25), _owner.Scale(5)),
                    Cursor = Cursors.Hand
                };

                int tabIndex = i;
                title.Click += (s, e) => SwitchToTab(tabIndex);
                tabPanel.Click += (s, e) => SwitchToTab(tabIndex);
                close.Click += (s, e) => CloseTab(tabIndex);

                tabPanel.MouseDown += (s, e) => Tab_MouseDown(s, e, tab.Id);
                title.MouseDown += (s, e) => Tab_MouseDown(tabPanel, e, tab.Id);
                close.MouseDown += (s, e) => Tab_MouseDown(tabPanel, e, tab.Id);
                tabPanel.MouseMove += Tab_MouseMove;
                title.MouseMove += Tab_MouseMove;
                close.MouseMove += Tab_MouseMove;
                tabPanel.MouseUp += Tab_MouseUp;
                title.MouseUp += Tab_MouseUp;
                close.MouseUp += Tab_MouseUp;

                tabPanel.Controls.Add(title);
                tabPanel.Controls.Add(close);
                _tabStrip.Controls.Add(tabPanel);
                _tabPanelsById[tab.Id] = tabPanel;
            }

            if (_addTabButton != null)
                _tabStrip.Controls.Add(_addTabButton);
            if (_tabOverflowButton != null)
                _tabStrip.Controls.Add(_tabOverflowButton);

            _tabStrip.ResumeLayout();
            UpdateTabStripLayout();
        }

        internal void UpdateTabStripVisuals()
        {
            if (_tabStrip == null) return;
            _tabStrip.SuspendLayout();
            for (int i = 0; i < _tabs.Count; i++)
            {
                var tab = _tabs[i];
                if (!_tabPanelsById.TryGetValue(tab.Id, out var panel)) continue;
                bool isActive = i == _activeTabIndex;
                panel.BackColor = isActive ? _owner.ActiveTabBackColor : _owner.InactiveTabBackColor;
                foreach (Control c in panel.Controls)
                {
                    if (c is Label lbl && (lbl.Tag as string) == "title")
                    {
                        lbl.Text = tab.Title;
                        lbl.ForeColor = _owner.ForeColor_Dark;
                    }
                    else if (c is Label close && close.Text == "×")
                    {
                        close.ForeColor = _owner.SecondaryForeColor;
                    }
                }
                panel.Invalidate();
            }

            if (_addTabButton != null)
            {
                _addTabButton.BackColor = _owner.TitleBarColor;
                _addTabButton.ForeColor = _owner.ForeColor_Dark;
                _addTabButton.FlatAppearance.MouseOverBackColor = _owner.ControlHoverColor;
                _addTabButton.Invalidate();
            }

            if (_tabOverflowButton != null)
            {
                _tabOverflowButton.BackColor = _owner.TitleBarColor;
                _tabOverflowButton.ForeColor = _owner.ForeColor_Dark;
                _tabOverflowButton.FlatAppearance.MouseOverBackColor = _owner.ControlHoverColor;
                _tabOverflowButton.Invalidate();
            }

            _tabStrip.Invalidate();
            _titleBar?.Invalidate();
            _tabStrip.ResumeLayout();
        }

        private void UpdateTabStripLayout()
        {
            if (_tabStrip == null) return;
            if (_windowButtonsPanel != null && _titleBar != null)
            {
                _tabStrip.Width = Math.Max(_owner.Scale(100), _titleBar.ClientSize.Width - _windowButtonsPanel.Width);
            }

            int tabWidth = GetTabPanelWidth();
            bool showOverflow = ShouldShowTabOverflow();
            if (_tabOverflowButton != null) _tabOverflowButton.Visible = showOverflow;

            foreach (var kvp in _tabPanelsById)
            {
                var panel = kvp.Value;
                panel.Width = tabWidth;
                foreach (Control c in panel.Controls)
                {
                    if (c is Label lbl && (lbl.Tag as string) == "title")
                    {
                        lbl.Location = new Point(_owner.Scale(10), _owner.Scale(5));
                        lbl.Size = new Size(Math.Max(_owner.Scale(40), panel.Width - _owner.Scale(40)), _owner.Scale(20));
                    }
                    else if (c is Label close && close.Text == "×")
                    {
                        close.Location = new Point(panel.Width - _owner.Scale(25), _owner.Scale(5));
                    }
                }
            }
            _tabStrip.PerformLayout();
        }

        private bool ShouldShowTabOverflow()
        {
            if (_tabStrip == null) return false;
            int count = _tabs.Count;
            if (count <= 0) return false;
            GetVisibleTabRange(out _, out int visibleCount);
            return visibleCount < count;
        }

        private int GetTabPanelWidth()
        {
            if (_tabStrip == null) return _owner.Scale(240);
            int count = _tabs.Count;
            if (count <= 0) return _owner.Scale(240);

            int maxWidth = _owner.Scale(240);
            int minWidth = _owner.Scale(105);
            int available = Math.Max(0, _tabStrip.ClientSize.Width - GetTabActionsWidth() - _owner.Scale(1));
            int perTab = available / count;
            return Math.Max(minWidth, Math.Min(maxWidth, perTab));
        }

        private void GetVisibleTabRange(out int startIndex, out int visibleCount)
        {
            startIndex = 0;
            visibleCount = _tabs.Count;
            if (_tabStrip == null || _tabs.Count == 0) return;

            int tabWidth = GetTabPanelWidth();
            int tabSlot = tabWidth + _owner.Scale(1);
            int available = Math.Max(0, _tabStrip.ClientSize.Width - GetTabActionsWidth() - _owner.Scale(1));
            int maxVisible = Math.Max(1, available / tabSlot);

            if (maxVisible >= _tabs.Count)
            {
                visibleCount = _tabs.Count;
                startIndex = 0;
                return;
            }

            visibleCount = maxVisible;
            int half = maxVisible / 2;
            startIndex = _activeTabIndex - half;
            if (startIndex < 0) startIndex = 0;
            if (startIndex + visibleCount > _tabs.Count)
                startIndex = _tabs.Count - visibleCount;
        }

        private int GetTabActionsWidth()
        {
            int width = 0;
            if (_addTabButton != null) width += _addTabButton.Width + _addTabButton.Margin.Horizontal;
            if (_tabOverflowButton != null) width += _tabOverflowButton.Width + _tabOverflowButton.Margin.Horizontal;
            return width;
        }

        private void ShowTabOverflowMenu()
        {
            if (_tabOverflowButton == null || _tabStrip == null) return;

            var menu = new ContextMenuStrip
            {
                Renderer = _owner._themeController.IsDarkTheme ? new DarkToolStripRenderer() : new LightToolStripRenderer(),
                ShowImageMargin = false,
                BackColor = _owner.BackColor_Dark
            };

            for (int i = 0; i < _tabs.Count; i++)
            {
                int idx = i;
                string title = _tabs[i].Title;
                if (string.IsNullOrWhiteSpace(title)) title = "Tab";
                var item = new ToolStripMenuItem(title);
                if (idx == _activeTabIndex)
                    item.Font = new Font(item.Font, FontStyle.Bold);
                item.Click += (s, e) => SwitchToTab(idx);
                menu.Items.Add(item);
            }

            menu.Show(_tabOverflowButton, new Point(0, _tabOverflowButton.Height));
        }

        private static void EnableDoubleBuffering(Control control)
        {
            typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(control, true, null);
        }

        private static void ApplyButtonTextOffset(Button btn, string text, int offsetY)
        {
            btn.Text = "";
            btn.Tag = text;
            btn.Paint += (s, e) =>
            {
                var b = (Button)s!;
                if (b.Tag is not string t)
                    return;
                var rect = b.ClientRectangle;
                rect.Offset(0, offsetY);
                var color = b.Enabled ? b.ForeColor : SystemColors.GrayText;
                TextRenderer.DrawText(
                    e.Graphics,
                    t,
                    b.Font,
                    rect,
                    color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            };
        }

        private void Tab_MouseDown(object? sender, MouseEventArgs e, string tabId)
        {
            if (e.Button == MouseButtons.Middle)
            {
                int idx = _tabs.FindIndex(t => t.Id == tabId);
                CloseTab(idx);
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                _tabDragId = tabId;
                _tabDragStart = Control.MousePosition;
            }
        }

        private void Tab_MouseMove(object? sender, MouseEventArgs e)
        {
            if (string.IsNullOrEmpty(_tabDragId)) return;
            if (Control.MouseButtons != MouseButtons.Left) return;

            var current = Control.MousePosition;
            if (Math.Abs(current.X - _tabDragStart.X) < 6 && Math.Abs(current.Y - _tabDragStart.Y) < 6) return;

            _tabStrip?.DoDragDrop(_tabDragId, DragDropEffects.Move);
            _tabDragId = "";
        }

        private void Tab_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                _tabDragId = "";
        }

        private void TabStrip_DragOver(object? sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(typeof(string)))
                e.Effect = DragDropEffects.Move;
        }

        private void TabStrip_DragDrop(object? sender, DragEventArgs e)
        {
            if (_tabStrip == null) return;
            if (e.Data == null || !e.Data.GetDataPresent(typeof(string))) return;
            var rawTabId = e.Data.GetData(typeof(string));
            if (rawTabId is not string tabId || string.IsNullOrEmpty(tabId)) return;

            int fromIndex = _tabs.FindIndex(t => t.Id == tabId);
            if (fromIndex < 0) return;

            var point = _tabStrip.PointToClient(new Point(e.X, e.Y));
            int toIndex = _tabs.Count - 1;
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (_tabPanelsById.TryGetValue(_tabs[i].Id, out var panel))
                {
                    if (point.X < panel.Right)
                    {
                        toIndex = i;
                        break;
                    }
                }
            }

            if (toIndex == fromIndex) return;

            var tab = _tabs[fromIndex];
            _tabs.RemoveAt(fromIndex);
            _tabs.Insert(toIndex, tab);

            if (_activeTabIndex == fromIndex) _activeTabIndex = toIndex;
            else if (fromIndex < _activeTabIndex && toIndex >= _activeTabIndex) _activeTabIndex--;
            else if (fromIndex > _activeTabIndex && toIndex <= _activeTabIndex) _activeTabIndex++;

            RebuildTabStrip();
            UpdateTabStripVisuals();
        }
    }
}
