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

public partial class MainForm : Form
{
    private void RefreshFrame()
        => _windowChromeController.RefreshFrame();

    private void ApplyDarkMode()
        => _windowChromeController.ApplyDarkMode();

    // Virtual Path Constants
    private const string ThisPcPath = "::ThisPC";

    // NavigationController bridge.
    // Keep internal surface tiny: it only exists to let NavigationController manage state without
    // exposing MainForm internals publicly.
    internal const string ThisPcPathConst = ThisPcPath;
    internal string CurrentPathForNav => _currentPath;
    internal void ClearCurrentPathForHistory() => _currentPath = "";
    internal static bool IsShellPathStatic(string path) => ShellNavigationController.IsShellPath(path);
    private IntPtr _headerHandle;

    private ListView _listView;
    private Control _addressBar;
    private TextBox _addressTextBox = null!;
    private FlowLayoutPanel _breadcrumbPanel = null!;
    private TextBox _searchBox = null!;
    private Label _searchingOverlay = null!;
    private TreeView _sidebar;
    private SidebarController _sidebarController;
    private readonly Panel _titleBar;
    private readonly StatusStrip _statusBar;
    private ToolStripStatusLabel _pathLabel;
    private ToolStripStatusLabel _statusLabel;
    private ToolStripStatusLabel _viewToggleLabel = null!;
    private readonly ImageList _smallIcons;
    private readonly ImageList _largeIcons;
    private SplitContainer _splitContainer = null!;
    private Panel _navPanel = null!;
    private Control _searchControl = null!;
    private Panel _navButtonsPanel = null!;
    private Button _backBtn = null!;
    private Button _fwdBtn = null!;
    private Button _upBtn = null!;
    private Button _refreshBtn = null!;
    private Button _settingsBtn = null!;
    private FlowLayoutPanel _tabStrip = null!;
    private Panel _windowButtonsPanel = null!;
    private Button _windowMinButton = null!;
    private Button _windowMaxButton = null!;
    private Button _windowCloseButton = null!;
    private readonly TabsController _tabsController;
    private readonly TileViewController _tileViewController;
    private readonly IconZoomController _iconZoomController;
    private readonly ArchiveController _archiveController;
    private readonly HotkeyController _hotkeyController;
    private readonly ListViewController _listViewController;
    private readonly ListViewRenderController _listViewRenderController;
    private readonly SelectionOpenController _selectionOpenController;
    private readonly OpenTargetController _openTargetController;
    private readonly FileOperationsController _fileOperationsController;
    private readonly ShellActionsController _shellActionsController;
    private readonly HeaderTailController _headerTailController;
    private readonly SettingsLauncherController _settingsLauncherController;
    private readonly ThemeController _themeController;
    private readonly WindowChromeController _windowChromeController;
    private readonly ShellNavigationController _shellNav;

    private List<FileItem> _items = new();
    private List<FileItem> _allItems = new(); // Unfiltered list for search
    private string _currentPath = "";
    private string _currentDisplayPath = "";
    private bool _isShellMode = false;
    private bool _suppressSearchTextChanged = false;
    private bool _suppressHistoryUpdate = false;
    private readonly NavigationController _nav;
    private CancellationTokenSource? _loadCts;
    private readonly SearchController _searchController;
    private readonly AddressBarController _addressBarController;
    private readonly WatcherController _watcherController;
    private readonly DragDropController _dragDropController;
    private readonly QuickLookController _quickLookController;
    private readonly LayoutController _layoutController;
    private readonly StartupNavigationController _startupNavigationController;
    private readonly StartupIconController _startupIconController;
    private readonly UiSettingsController _uiSettingsController;
    private readonly ListViewInteractionController _listViewInteractionController;
    
    private ContextMenuStrip _contextMenu = null!;
    private ContextMenuController _contextMenuController = null!;
    private IconLoadService? _iconLoadService;

    private System.Windows.Forms.Timer _repaintTimer = null!;
    private System.Windows.Forms.Timer _statusTimer = null!;
    private System.Windows.Forms.Timer _retryLoadTimer = null!;
    private string? _retryLoadPath;
    private bool _retryLoadPending = false;
    private bool _needsRepaint = false;
    private HashSet<string> _cutPaths = new();
    private SortColumn _sortColumn = SortColumn.Name;
    private SortDirection _sortDirection = SortDirection.Ascending;
    private bool _taggedFilesOnTop = false; // "Sticky" tag grouping
    private bool IsSearchMode => _searchController.IsSearchMode;
    private bool IsTileView => _tileViewController.IsTileView;
    private char _lastSearchChar = '\0';
    private int _lastSearchIndex = -1;
    private LlmChatPanel? _llmChatPanel;
    private int _hoveredIndex = -1;
    private const string SidebarSeparatorTag = "SEPARATOR";
    private const string SidebarPinnedNodeName = "PINNED";
    private bool _pendingClearFromThisPc = false;
    private bool _loadCompleted = false;
    private bool _startupListStabilized = false;
    private List<string>? _startupSelectPaths;
    private bool _fastStartup = false;
    private long _navigationTraceSeq = 0;
    private PictureBox? _navigationFreezeOverlay;
    private bool _navigationFreezeActive = false;
    private static readonly object SearchProgressRowTag = new object();
    private string? _pendingTabTopRestorePath;
    private int _pendingTabTopRestoreIndex = -1;
    private string? _pendingTabCachePath;
    private List<FileItem>? _pendingTabCacheItems;
    private List<FileItem>? _pendingTabCacheAllItems;
    private bool _pendingTabCacheIsSearchMode;
    
    private bool _suppressColumnMetaUpdate = false;

    private sealed class ColumnMeta
    {
        public string Key { get; set; } = "";
        // Width in 96-DPI pixels (so it can be scaled on DPI changes).
        public int BaseWidth { get; set; }
    }
    private void TitleBarMouseDown(Point location) => _windowChromeController.TitleBarMouseDown(location);

    private void TitleBarMouseUp() => _windowChromeController.TitleBarMouseUp();

    private void TitleBarMouseMove(Control surface, MouseEventArgs e) => _windowChromeController.TitleBarMouseMove(surface, e);

    private int Scale(int pixels) => (int)(pixels * (this.DeviceDpi / 96.0));
    private int Unscale(int pixels) => (int)Math.Round(pixels * (96.0 / this.DeviceDpi));
    private void BeginNavigationFreezeVisual()
    {
        // Avoid freeze-overlay on initial window load/open. It can leave stale first-frame paints
        // with owner-drawn virtual list rows near the bottom edge.
        if (!_loadCompleted) return;
        if (_navigationFreezeActive) return;
        if (_listView == null || _listView.IsDisposed || !_listView.Visible) return;
        if (_splitContainer == null || _splitContainer.IsDisposed) return;
        if (_listView.Width <= 1 || _listView.Height <= 1) return;

        try
        {
            _navigationFreezeOverlay ??= new PictureBox
            {
                Visible = false,
                BackColor = ListBackColor,
                SizeMode = PictureBoxSizeMode.Normal
            };

            if (_navigationFreezeOverlay.Parent == null)
            {
                _splitContainer.Panel2.Controls.Add(_navigationFreezeOverlay);
            }

            var bmp = new Bitmap(_listView.Width, _listView.Height);
            try
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    var screenPoint = _listView.PointToScreen(Point.Empty);
                    g.CopyFromScreen(screenPoint, Point.Empty, _listView.Size);
                }

                var old = _navigationFreezeOverlay.Image;
                _navigationFreezeOverlay.Image = bmp;
                old?.Dispose();
            }
            catch
            {
                bmp.Dispose();
                throw;
            }

            _navigationFreezeOverlay.Bounds = _listView.Bounds;
            _navigationFreezeOverlay.Visible = true;
            _navigationFreezeOverlay.BringToFront();
            _listView.Visible = false;
            _navigationFreezeActive = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"BeginNavigationFreezeVisual failed: {ex.Message}");
        }
    }

    private void EndNavigationFreezeVisual()
    {
        if (!_navigationFreezeActive) return;
        _navigationFreezeActive = false;

        try
        {
            if (_listView != null && !_listView.IsDisposed)
                _listView.Visible = true;
        }
        catch (Exception ex) { Debug.WriteLine($"EndNavigationFreezeVisual listView restore failed: {ex.Message}"); }

        try
        {
            if (_navigationFreezeOverlay != null && !_navigationFreezeOverlay.IsDisposed)
                _navigationFreezeOverlay.Visible = false;
        }
        catch (Exception ex) { Debug.WriteLine($"EndNavigationFreezeVisual overlay hide failed: {ex.Message}"); }
    }

    private void ForceListViewportTopAndRedraw(int preferredIndex, string reason, int pass)
    {
        if (_listView == null || _listView.IsDisposed || !_listView.IsHandleCreated)
            return;

        LogListViewState(reason, $"before-pass{pass}");

        int index = 0;
        if (_items.Count > 0)
            index = Math.Max(0, Math.Min(preferredIndex, _items.Count - 1));

        try
        {
            const int LVM_ENSUREVISIBLE = 0x1013;
            SendMessage(_listView.Handle, LVM_ENSUREVISIBLE, index, 0);
        }
        catch (Exception ex) { Debug.WriteLine($"ForceListViewportTopAndRedraw LVM_ENSUREVISIBLE failed: {ex.Message}"); }

        try
        {
            if (_items.Count > 0)
                _listView.EnsureVisible(index);
        }
        catch (Exception ex) { Debug.WriteLine($"ForceListViewportTopAndRedraw EnsureVisible failed: {ex.Message}"); }

        try
        {
            const uint RDW_INVALIDATE = 0x0001;
            const uint RDW_ALLCHILDREN = 0x0080;
            const uint RDW_UPDATENOW = 0x0100;
            RedrawWindow(_listView.Handle, IntPtr.Zero, IntPtr.Zero,
                RDW_INVALIDATE | RDW_ALLCHILDREN | RDW_UPDATENOW);
        }
        catch (Exception ex) { Debug.WriteLine($"ForceListViewportTopAndRedraw RedrawWindow failed: {ex.Message}"); }

        try
        {
            _listView.Invalidate();
            _listView.Update();
        }
        catch (Exception ex) { Debug.WriteLine($"ForceListViewportTopAndRedraw Invalidate/Update failed: {ex.Message}"); }

        LogListViewState(reason, $"after-pass{pass}");
    }

    private void ResetListViewportTopAsync(int preferredIndex = 0, string reason = "reset")
    {
        if (_listView == null || _listView.IsDisposed)
            return;

        BeginInvoke((Action)(() =>
        {
            if (_listView == null || _listView.IsDisposed || !_listView.IsHandleCreated)
                return;
            ForceListViewportTopAndRedraw(preferredIndex, reason, 1);
            BeginInvoke((Action)(() =>
            {
                if (_listView == null || _listView.IsDisposed || !_listView.IsHandleCreated)
                    return;
                ForceListViewportTopAndRedraw(preferredIndex, reason, 2);
            }));
        }));
    }
    private void ApplySidebarSplit()
    {
        if (_splitContainer == null || _splitContainer.Width <= 0) return;
        if (AppSettings.Current.SidebarSplitAtMinimum)
        {
            _splitContainer.SplitterDistance = _splitContainer.Panel1MinSize;
            return;
        }
        var ratio = AppSettings.Current.SidebarSplitRatio;
        int distance;
        if (ratio > 0.0 && ratio < 0.9)
        {
            distance = (int)Math.Round(_splitContainer.Width * ratio);
        }
        else
        {
            distance = Scale(AppSettings.Current.SidebarSplitDistance);
        }
        distance = Math.Max(_splitContainer.Panel1MinSize, distance);
        _splitContainer.SplitterDistance = distance;
    }
    private Padding Scale(Padding p) => new Padding(Scale(p.Left), Scale(p.Top), Scale(p.Right), Scale(p.Bottom));


    private void SetupFileColumns(ListView lv)
        => _listViewController.SetupFileColumns(lv);

    private void SetupDriveColumns(ListView lv)
        => _listViewController.SetupDriveColumns(lv);

    private void RescaleListViewColumns()
        => _listViewController.RescaleListViewColumns();


    private void UpdateScale()
        => _uiSettingsController.UpdateScale();

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        UpdateScale();
    }

    private void LoadDrives()
        => _startupNavigationController.LoadDrives();

    private void DrawTags(Graphics g, Rectangle bounds, string? tagText, Color rowBackColor, bool isSelected)
        => _listViewRenderController.DrawTags(g, bounds, tagText, rowBackColor, isSelected);

    private void LoadFolderSettings()
        => _startupNavigationController.LoadFolderSettings();

    private void SaveFolderSettings()
        => _startupNavigationController.SaveFolderSettings();

    private void OpenWithDialog()
        => _shellActionsController.OpenWithDialog();

    private void ShowInExplorer()
        => _shellActionsController.ShowInExplorer();

    private void CopyPathToClipboard()
        => _shellActionsController.CopyPathToClipboard();

    private void ShowProperties()
        => _shellActionsController.ShowProperties();

    private void ShowSingleFileProperties(string path)
        => _shellActionsController.ShowSingleFileProperties(path);
    private TextBox? _renameTextBox;
    private const int ColumnIndex_Tags = 6; // Index for Tags
    private const int ColumnIndex_DriveCapacity = 5; // Drive view Capacity bar column


    // Dark theme colors
    private static readonly Color BackColor_Dark = Color.FromArgb(30, 30, 30);
    private static readonly Color ForeColor_Dark = Color.FromArgb(240, 240, 240);
    private static readonly Color AccentColor = Color.FromArgb(0, 120, 212);
    private static readonly Color SidebarColor = Color.FromArgb(37, 37, 38);
    private static readonly Color ListBackColor = Color.FromArgb(25, 25, 25);
    private static readonly Color TitleBarColor = Color.FromArgb(32, 32, 32);
    private static readonly Color TagColor = Color.FromArgb(60, 60, 60);
    private static readonly Color TagForeColor = Color.FromArgb(200, 200, 200);

    public MainForm(string? initialPath = null)
    {
        _themeController = new ThemeController(this);
        _nav = new NavigationController(this);
        _searchController = new SearchController(this);
        _addressBarController = new AddressBarController(this);
        _tabsController = new TabsController(this);
        _watcherController = new WatcherController(this);
        _dragDropController = new DragDropController(this);
        _quickLookController = new QuickLookController(this);
        _layoutController = new LayoutController(this);
        _startupNavigationController = new StartupNavigationController(this);
        _startupIconController = new StartupIconController(this);
        _uiSettingsController = new UiSettingsController(this);
        _listViewInteractionController = new ListViewInteractionController(this);
        _tileViewController = new TileViewController(this);
        _iconZoomController = new IconZoomController(this);
        _archiveController = new ArchiveController(this);
        _hotkeyController = new HotkeyController(this);
        _listViewController = new ListViewController(this);
        _listViewRenderController = new ListViewRenderController(this);
        _selectionOpenController = new SelectionOpenController(this);
        _openTargetController = new OpenTargetController(this);
        _fileOperationsController = new FileOperationsController(this);
        _shellActionsController = new ShellActionsController(this);
        _headerTailController = new HeaderTailController(this);
        _settingsLauncherController = new SettingsLauncherController(this);
        _windowChromeController = new WindowChromeController(this);
        _shellNav = new ShellNavigationController(ThisPcPath);
        // Borderless form setup
        Text = "Speed Explorer";
        FormBorderStyle = FormBorderStyle.None;
        var s = AppSettings.Current;
        Size = new Size(s.MainWindowWidth, s.MainWindowHeight);
        MinimumSize = new Size(Scale(400), Scale(300));
        StartPosition = FormStartPosition.CenterScreen; 
        KeyPreview = true;
        
        // Hide window and disable redrawing to prevent ANY "Skeleton" frames
        Opacity = 0;
        SendMessage(Handle, WM_SETREDRAW, 0, 0);

        this.Shown += (s, e) =>
        {
            // Safety: if load never completes, force visibility after a delay.
            var t = new System.Windows.Forms.Timer { Interval = 1500 };
            t.Tick += (s2, e2) =>
            {
                t.Stop();
                t.Dispose();
                if (!_loadCompleted)
                {
                    SendMessage(Handle, WM_SETREDRAW, 1, 0);
                    Opacity = 1;
                    if (_listView != null && !_listView.IsDisposed)
                        _listView.Visible = true;
                    base.Refresh();
                }
            };
            t.Start();
        };

        this.ResizeEnd += (s, e) => {
            if (this.WindowState == FormWindowState.Normal) {
                AppSettings.Current.MainWindowWidth = this.Width;
                AppSettings.Current.MainWindowHeight = this.Height;
                AppSettings.Current.Save();
            }
        };
        _themeController.ApplyMainWindowTheme();

        // Create icon lists first
        _smallIcons = CreateIconList(Scale(16));
        _largeIcons = CreateIconList(Scale(32));

        InitializeContextMenu(); // Initialize menu first!

        // Create controls
        _titleBar = CreateTitleBar();
        _addressBar = CreateAddressBar();

        _sidebarController = new SidebarController(this);
        _sidebar = _sidebarController.CreateSidebar(_contextMenu!);
        // Set initial scaled item height for sidebar
        _sidebar.ItemHeight = Scale(24);
        
        _listView = CreateListView();
        InitializeSearchOverlay();
        _listView.Visible = false;
        _statusBar = CreateStatusBar();
        _pathLabel = (ToolStripStatusLabel)_statusBar.Items[0];
        _statusLabel = (ToolStripStatusLabel)_statusBar.Items[1];

        _hotkeyController.Reload();
        ApplySettings();   
        this.KeyUp += (s, e) => { if (_hotkeyController.IsActionKeyCode("QuickLook", e.KeyCode)) HideQuickLook(); };

        bool inferStartupSelection = !string.IsNullOrWhiteSpace(initialPath);
        var normalizedStartup = NormalizeStartupPath(initialPath, out _startupSelectPaths, inferStartupSelection);
        _fastStartup = _startupSelectPaths != null && _startupSelectPaths.Count > 0;
        InitializeTabs(normalizedStartup);

        UpdateViewToggleLabel();
        _layoutController.InitializeLayoutAndLifecycle(normalizedStartup);
    }

    private void InitializeTabs(string? initialPath) => _tabsController.InitializeTabs(initialPath);

    private string NormalizeStartupPath(string? input, out List<string>? selectPaths, bool inferRecentSelectionForDirectory = false)
        => _startupIconController.NormalizeStartupPath(input, out selectPaths, inferRecentSelectionForDirectory);

    public void HandleExternalPath(string? rawPath) => _tabsController.HandleExternalPath(rawPath);

    private void AddNewTab() => _tabsController.AddNewTab();

    private void CloseTab(int index) => _tabsController.CloseTab(index);

    private void SwitchToTab(int index, bool force = false, bool saveCurrent = true, bool skipNavigation = false)
        => _tabsController.SwitchToTab(index, force, saveCurrent, skipNavigation);

    private void SaveCurrentTabState() => _tabsController.SaveCurrentTabState();

    private void LoadTabState(TabState tab) => _tabsController.LoadTabState(tab);

    private string GetTabTitleForPath(string path) => _tabsController.GetTabTitleForPath(path);

    private void UpdateActiveTabTitle() => _tabsController.UpdateActiveTabTitle();

    private void ToggleMaximize()
        => _windowChromeController.ToggleMaximize();

    private void ToggleFullscreen()
        => _windowChromeController.ToggleFullscreen();

    private ImageList CreateIconList(int size)
        => _startupIconController.CreateIconList(size);

    private void ApplySettings()
        => _uiSettingsController.ApplySettings();

    private bool IsDriveItemsOnly()
        => _startupNavigationController.IsDriveItemsOnly();

    private Bitmap CreateFolderIcon(int size)
        => _startupIconController.CreateFolderIcon(size);

    private Bitmap CreateFileIcon(int size)
        => _startupIconController.CreateFileIcon(size);

    private Bitmap CreateImageIcon(int size)
        => _startupIconController.CreateImageIcon(size);

    private Bitmap CreateDriveIcon(int size)
        => _startupIconController.CreateDriveIcon(size);

    private Bitmap CreateComputerIcon(int size)
        => _startupIconController.CreateComputerIcon(size);

    private Bitmap CreateUsbIcon(int size)
        => _startupIconController.CreateUsbIcon(size);

    private Control CreateAddressBar() => _addressBarController.CreateAddressBar();

    private void EnableAddressEdit() => _addressBarController.EnableAddressEdit();

    private void UpdateBreadcrumbs(string path) => _addressBarController.UpdateBreadcrumbs(path);

    private void AddBreadcrumb(string text, string targetPath) => _addressBarController.AddBreadcrumb(text, targetPath);

    private void AddSeparator() => _addressBarController.AddSeparator();

    private void PerformSearch(string query) => _searchController.StartSearch(query);

    private void ClearSearch() => _searchController.ClearSearch();

    private void StretchTagsColumn()
        => _listViewController.StretchTagsColumn();

    private void EnsureHeaderTail()
        => _listViewController.EnsureHeaderTail();


    private static string GetTypeIndicator(FileItem item)
    {
        if (item.IsDirectory) return "📁 ";
        
        return item.Extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".ico" or ".tiff" or ".tif" => "🖼️ ",
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".webm" => "🎬 ",
            ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" or ".aac" => "🎵 ",
            ".txt" or ".md" or ".log" => "📝 ",
            ".pdf" => "📕 ",
            ".doc" or ".docx" => "📘 ",
            ".xls" or ".xlsx" => "📗 ",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "📦 ",
            ".exe" or ".msi" => "⚙️ ",
            ".dll" => "🔧 ",
            ".cs" or ".py" or ".js" or ".ts" or ".cpp" or ".c" or ".h" or ".java" => "💻 ",
            ".html" or ".htm" or ".css" => "🌐 ",
            ".json" or ".xml" or ".yaml" or ".yml" => "📋 ",
            _ => "📄 "
        };
    }

    private static bool IsShellPath(string? path) => ShellNavigationController.IsShellPath(path);
    private static bool IsShellIdPath(string? path) => ShellNavigationController.IsShellIdPath(path);
    private string GetShellDisplayName(string shellPath) => _shellNav.GetShellDisplayName(shellPath);
    internal string? GetShellParentPath(string shellPath) => _shellNav.GetShellParentPath(shellPath);
    private Task<List<FileItem>> GetShellItemsAsync(string shellPath, CancellationToken ct)
        => _shellNav.GetShellItemsAsync(shellPath, ct, _currentDisplayPath);
    private void OpenShellPath(string shellPath) => _shellNav.OpenShellPath(shellPath);
    internal string RegisterShellItem(object item, string? parentShellId) => _shellNav.RegisterShellItem(item, parentShellId);

    private void ListView_RetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
        => _listViewInteractionController.RetrieveVirtualItem(sender, e);


    private void ListView_ColumnClick(object? sender, ColumnClickEventArgs e)
        => _listViewInteractionController.ColumnClick(sender, e);


    private void ListView_MouseDoubleClick(object? sender, MouseEventArgs e)
        => _listViewInteractionController.MouseDoubleClick(sender, e);

    private void ListView_MouseDown(object? sender, MouseEventArgs e)
        => _listViewInteractionController.MouseDown(sender, e);

    private ListViewItem BuildListViewItem(FileItem item, bool includeSubItems)
        => _listViewInteractionController.BuildListViewItem(item, includeSubItems);

    private void PopulateTileItems() => _tileViewController.PopulateTileItems();

    private void ListView_MouseMove(object? sender, MouseEventArgs e)
        => _listViewInteractionController.MouseMove(sender, e);

    private void InvalidateListItem(int index)
        => _listViewInteractionController.InvalidateListItem(index);

    private void ListView_KeyDown(object? sender, KeyEventArgs e)
        => _listViewInteractionController.KeyDown(sender, e);

    private void ListView_KeyUp(object? sender, KeyEventArgs e)
        => _listViewInteractionController.KeyUp(sender, e);

    private void ShowQuickLook() => _quickLookController.Show();

    private void HideQuickLook() => _quickLookController.Hide();
    private void ListView_DrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
        => _listViewRenderController.DrawColumnHeader(sender, e);

    private void ListView_DrawItem(object? sender, DrawListViewItemEventArgs e)
        => _listViewRenderController.DrawItem(sender, e);
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_hotkeyController.HandleProcessCmdKey(ref msg, keyData))
            return true;

        return base.ProcessCmdKey(ref msg, keyData);
    }

    // Reset is deferred during navigation to avoid flicker.

    private void ListView_DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
        => _listViewRenderController.DrawSubItem(sender, e);

}
