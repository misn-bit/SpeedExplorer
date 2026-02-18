using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
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
            var tab = new TabState
            {
                CurrentPath = startPath,
                CurrentDisplayPath = startPath,
                Title = GetTabTitleForPath(startPath)
            };
            _tabs.Add(tab);
            _activeTabIndex = 0;

            _addTabButton = new Button
            {
                Text = "+",
                Size = new Size(_owner.Scale(28), _owner.Scale(25)),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = ForeColor_Dark,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(_owner.Scale(6), _owner.Scale(8), 0, 0)
            };
            _addTabButton.FlatAppearance.BorderSize = 0;
            _addTabButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 60);
            ApplyButtonTextOffset(_addTabButton, "+", -_owner.Scale(1));
            _addTabButton.Click += (s, e) => AddNewTab();

            _tabOverflowButton = new Button
            {
                Text = "⋯",
                Size = new Size(_owner.Scale(28), _owner.Scale(24)),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = ForeColor_Dark,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(_owner.Scale(4), _owner.Scale(8), 0, 0),
                Visible = false
            };
            _tabOverflowButton.FlatAppearance.BorderSize = 0;
            _tabOverflowButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 60);
            _tabOverflowButton.Click += (s, e) => ShowTabOverflowMenu();

            RebuildTabStrip();
        }

        public void HandleExternalPath(string? rawPath)
        {
            if (Program.IsExplorerShellArgument(rawPath))
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", rawPath!) { UseShellExecute = true }); } catch { }
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

            var normalized = _owner.NormalizeStartupPath(rawPath, out var selectPaths);
            if (_owner._listView != null && !_owner._listView.IsDisposed)
                _owner._listView.Visible = true;

            bool useNewTab = _tabs.Count > 0;
            if (useNewTab)
            {
                SaveCurrentTabState();
                var tab = new TabState
                {
                    CurrentPath = normalized,
                    CurrentDisplayPath = normalized,
                    Title = GetTabTitleForPath(normalized)
                };
                _tabs.Add(tab);
                int newIndex = _tabs.Count - 1;
                _activeTabIndex = newIndex;
                RebuildTabStrip();
                SwitchToTab(newIndex, force: true, saveCurrent: false, skipNavigation: true);
                _owner.ObserveTask(_owner.NavigateTo(normalized, selectPaths), "TabsController.HandleExternalPath/new-tab");
            }
            else
            {
                _owner.ObserveTask(_owner.NavigateTo(normalized, selectPaths), "TabsController.HandleExternalPath/first-tab");
            }
        }

        public void AddNewTab()
        {
            SaveCurrentTabState();
            var startPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var tab = new TabState
            {
                CurrentPath = startPath,
                CurrentDisplayPath = startPath,
                Title = GetTabTitleForPath(startPath)
            };
            _tabs.Add(tab);
            int newIndex = _tabs.Count - 1;
            _activeTabIndex = newIndex;
            RebuildTabStrip();
            SwitchToTab(newIndex, force: true, saveCurrent: false);
        }

        public void OpenPathInNewTab(string path, bool activate = true)
        {
            SaveCurrentTabState();
            var tab = new TabState
            {
                CurrentPath = path,
                CurrentDisplayPath = path,
                Title = GetTabTitleForPath(path)
            };
            _tabs.Add(tab);
            int newIndex = _tabs.Count - 1;

            if (activate)
            {
                _activeTabIndex = newIndex;
                RebuildTabStrip();
                SwitchToTab(newIndex, force: true, saveCurrent: false);
            }
            else
            {
                RebuildTabStrip();
                UpdateTabStripVisuals();
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

            SaveCurrentTabState();
            _tabs.RemoveAt(index);
            if (_activeTabIndex >= _tabs.Count) _activeTabIndex = _tabs.Count - 1;
            RebuildTabStrip();
            SwitchToTab(_activeTabIndex, force: true, saveCurrent: false);
        }

        public void SwitchToTab(int index, bool force = false, bool saveCurrent = true, bool skipNavigation = false)
        {
            if (index < 0 || index >= _tabs.Count) return;
            if (index == _activeTabIndex && !force) return;
            _owner.LogListViewState("TAB", $"switch-begin target={index} current={_activeTabIndex}");

            if (saveCurrent) SaveCurrentTabState();
            _activeTabIndex = index;

            if (skipNavigation)
            {
                UpdateActiveTabTitle();
            }
            else
            {
                LoadTabState(_tabs[index]);
            }

            if (ShouldShowTabOverflow())
                RebuildTabStrip();
            else
                UpdateTabStripVisuals();

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
            tab.LastSelection = new Dictionary<string, string>(_owner._nav.LastSelection);
            tab.FolderSortSettings = new Dictionary<string, (SortColumn Column, SortDirection Direction)>(_owner._nav.FolderSortSettings);
            tab.BackHistory = CloneStack(_owner._nav.BackHistory);
            tab.ForwardHistory = CloneStack(_owner._nav.ForwardHistory);
            tab.IsShellMode = _owner._isShellMode;
            tab.CurrentShellId = ShellNavigationController.IsShellIdPath(_owner._currentPath) ? _owner._currentPath : "";
            tab.Title = GetTabTitleForPath(_owner._currentPath);
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
            _owner._nav.LastSelection = new Dictionary<string, string>(tab.LastSelection);
            _owner._nav.FolderSortSettings = new Dictionary<string, (SortColumn Column, SortDirection Direction)>(tab.FolderSortSettings);
            _owner._nav.BackHistory = CloneStack(tab.BackHistory);
            _owner._nav.ForwardHistory = CloneStack(tab.ForwardHistory);
            _owner._sortColumn = tab.SortColumn;
            _owner._sortDirection = tab.SortDirection;
            List<string>? restoreSelection = null;
            if (!tab.IsSearchMode && tab.SelectedPaths.Count > 0)
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
                _owner._pendingTabCacheIsSearchMode = tab.IsSearchMode;
            }
            else
            {
                _owner._pendingTabCachePath = null;
                _owner._pendingTabCacheItems = null;
                _owner._pendingTabCacheAllItems = null;
                _owner._pendingTabCacheIsSearchMode = false;
            }

            if (!tab.IsSearchMode && tab.TopItemIndex >= 0)
            {
                _owner._pendingTabTopRestorePath = tab.CurrentPath;
                _owner._pendingTabTopRestoreIndex = tab.TopItemIndex;
            }
            else
            {
                _owner._pendingTabTopRestorePath = null;
                _owner._pendingTabTopRestoreIndex = -1;
            }

            _owner._suppressHistoryUpdate = true;
            try
            {
                await _owner.NavigateTo(tab.CurrentPath, restoreSelection);
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

            if (tab.IsSearchMode && !IsShellPath(tab.CurrentPath) &&
                !string.IsNullOrWhiteSpace(tab.SearchText) &&
                tab.SearchText != Localization.T("search_placeholder"))
            {
                _owner._suppressSearchTextChanged = true;
                try
                {
                    _owner._searchBox.Text = tab.SearchText;
                    _owner._searchBox.ForeColor = ForeColor_Dark;
                }
                finally
                {
                    _owner._suppressSearchTextChanged = false;
                }

                if (useCachedSnapshot)
                {
                    _owner._searchController.RestoreCachedSearchState(tab.SearchText);
                    _owner.LogListViewState("TAB", $"load-after-search-cache req={requestId}");
                }
                else
                {
                    _owner._searchController.StartSearch(tab.SearchText);
                    _owner.LogListViewState("TAB", $"load-after-search-start req={requestId}");
                }
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
                catch { }
                _owner.LogListViewState("TAB", $"load-end req={requestId}");
            }));
        }

        public void UpdateActiveTabTitle()
        {
            if (_activeTabIndex < 0 || _activeTabIndex >= _tabs.Count) return;
            _tabs[_activeTabIndex].Title = GetTabTitleForPath(_owner._currentPath);
            UpdateTabStripVisuals();
        }

        public string GetTabTitleForPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "Tab";
            if (path == ThisPcPath) return Localization.T("this_pc");
            if (IsShellPath(path)) return _owner.GetShellDisplayName(path);
            try
            {
                return new DirectoryInfo(path).Name;
            }
            catch
            {
                return path;
            }
        }

        private Stack<string> CloneStack(Stack<string> source) => new Stack<string>(source.Reverse());

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
            catch { }

            try
            {
                if (_owner._listView.TopItem != null)
                    tab.TopItemIndex = _owner._listView.TopItem.Index;
            }
            catch { }

            if (tab.TopItemIndex < 0 && _owner._listView.SelectedIndices.Count > 0)
            {
                try { tab.TopItemIndex = _owner._listView.SelectedIndices[0]; } catch { }
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
                tab.CachedAllItems = allItems;
                tab.HasCachedSnapshot = true;

                if (tab.SortColumn == _owner._sortColumn && tab.SortDirection == _owner._sortDirection)
                {
                    tab.CachedItems = items;
                    continue;
                }

                var sorted = new List<FileItem>(allItems);
                FileSystemService.SortItems(sorted, tab.SortColumn, tab.SortDirection);
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
                    Height = _owner.Scale(25),
                    Width = tabWidth,
                    Margin = new Padding(_owner.Scale(4), _owner.Scale(8), 0, 0),
                    BackColor = isActive ? Color.FromArgb(55, 55, 55) : Color.FromArgb(40, 40, 40)
                };
                tabPanel.Tag = tab.Id;
                EnableDoubleBuffering(tabPanel);

                var title = new Label
                {
                    Text = tab.Title,
                    ForeColor = ForeColor_Dark,
                    AutoEllipsis = true,
                    Location = new Point(_owner.Scale(8), _owner.Scale(4)),
                    Size = new Size(Math.Max(_owner.Scale(40), tabPanel.Width - _owner.Scale(34)), _owner.Scale(18)),
                    Cursor = Cursors.Hand
                };
                title.Tag = "title";

                var close = new Label
                {
                    Text = "×",
                    ForeColor = Color.FromArgb(200, 200, 200),
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Size = new Size(_owner.Scale(18), _owner.Scale(18)),
                    Location = new Point(tabPanel.Width - _owner.Scale(22), _owner.Scale(2)),
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

        private void UpdateTabStripVisuals()
        {
            if (_tabStrip == null) return;
            _tabStrip.SuspendLayout();
            for (int i = 0; i < _tabs.Count; i++)
            {
                var tab = _tabs[i];
                if (!_tabPanelsById.TryGetValue(tab.Id, out var panel)) continue;
                bool isActive = i == _activeTabIndex;
                panel.BackColor = isActive ? Color.FromArgb(55, 55, 55) : Color.FromArgb(40, 40, 40);
                foreach (Control c in panel.Controls)
                {
                    if (c is Label lbl && (lbl.Tag as string) == "title")
                    {
                        lbl.Text = tab.Title;
                        break;
                    }
                }
            }
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
                        lbl.Location = new Point(_owner.Scale(8), _owner.Scale(4));
                        lbl.Size = new Size(Math.Max(_owner.Scale(40), panel.Width - _owner.Scale(34)), _owner.Scale(18));
                    }
                    else if (c is Label close && close.Text == "×")
                    {
                        close.Location = new Point(panel.Width - _owner.Scale(22), _owner.Scale(2));
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
            if (_tabStrip == null) return _owner.Scale(160);
            int count = _tabs.Count;
            if (count <= 0) return _owner.Scale(160);

            int maxWidth = _owner.Scale(160);
            int minWidth = _owner.Scale(70);
            int available = Math.Max(0, _tabStrip.ClientSize.Width - GetTabActionsWidth() - _owner.Scale(8));
            int perTab = available / count;
            return Math.Max(minWidth, Math.Min(maxWidth, perTab));
        }

        private void GetVisibleTabRange(out int startIndex, out int visibleCount)
        {
            startIndex = 0;
            visibleCount = _tabs.Count;
            if (_tabStrip == null || _tabs.Count == 0) return;

            int tabWidth = GetTabPanelWidth();
            int tabSlot = tabWidth + _owner.Scale(4);
            int available = Math.Max(0, _tabStrip.ClientSize.Width - GetTabActionsWidth() - _owner.Scale(8));
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
                Renderer = new DarkToolStripRenderer(),
                ShowImageMargin = false,
                BackColor = Color.FromArgb(30, 30, 30)
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
