using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using SpeedExplorer.Design;
using SpeedExplorer.LLM;
using SpeedExplorer.Settings;

namespace SpeedExplorer;

public sealed class SettingsFormChangedEventArgs(
    bool refreshMainWindow, bool refreshMainTheme, bool reloadHotkeys) : EventArgs
{
    public bool RefreshMainWindow { get; } = refreshMainWindow;
    public bool RefreshMainTheme { get; } = refreshMainTheme;
    public bool ReloadHotkeys { get; } = reloadHotkeys;
}

/// <summary>
/// Settings window: a left <see cref="NavList"/> sidebar plus one page per section,
/// all rendered with the card design from Settings/Design.
/// </summary>
public class SettingsForm : Form
{
    private const int ResizeBorder = 8;
    private const int OwnerClickAwayMargin = 48;

    [Flags]
    private enum ResizeEdges
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8
    }

    [Flags]
    private enum PendingSettingsSections
    {
        None = 0,
        General = 1,
        AppBehavior = 2,
        ManualActions = 4,
        Llm = 8
    }

    private readonly GeneralSettingsControl _generalSection = new();
    private readonly AppBehaviorSettingsControl _appBehaviorSection = new();
    private readonly ManualContextActionsSettingsControl _manualActionsSection = new();
    private readonly LlmSettingsSectionControl _llmSettingsSection = new();
    private readonly WindowsIntegrationSettingsControl _windowsIntegrationSection = new();
    private readonly SettingsPage _hotkeysSection = new(Localization.T("hotkeys_tab"));
    private readonly SettingsPage _aboutSection = new(Localization.T("about_tab"));

    private readonly NavList _nav = new();
    private readonly Panel _pages = new();
    private readonly Panel _chromeRoot = new();
    private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 250 };
    private Control[] _pageOrder = Array.Empty<Control>();
    private Rectangle _ownerClientBounds;
    private bool _positioningToOwner;
    private ResizeEdges _manualResizeEdges;
    private Point _resizeStartPointer;
    private Size _resizeStartSize;
    private bool _manualResizing;
    private bool _hasPendingChanges;
    private bool _isCommitting;
    private PendingSettingsSections _pendingSections;
    private bool _refreshMainWindow;
    private bool _refreshMainTheme;
    private bool _reloadHotkeys;

    private readonly Dictionary<string, FlatButton> _hotkeyButtons = new();
    private readonly Dictionary<string, string> _hotkeyEdits = new();
    private string? _recordingAction = null;

    private static readonly HashSet<string> ImageViewerHotkeyActions = new(StringComparer.Ordinal)
    {
        "ToggleOcrBoxes",
        "ToggleSavedTranslation",
        "FitSmallDimension",
        "ImageViewerToggleAI",
        "ImageViewerStartTranslation",
        "ImageViewerStartOcr",
        "ImageViewerTag",
        "ImageViewerPrevious",
        "ImageViewerPreviousAlt",
        "ImageViewerNext",
        "ImageViewerNextAlt",
        "ImageViewerNextSpace",
        "ImageViewerClose",
        "ImageViewerToggleFullscreen",
        "ImageViewerToggleFullscreenAlt",
        "ImageViewerZoomIn",
        "ImageViewerZoomInAlt",
        "ImageViewerZoomOut",
        "ImageViewerZoomOutAlt",
        "ImageViewerRotate",
        "ImageViewerFitWindow",
        "ImageViewerFitWindowNumpad",
        "ImageViewerFitWindowPlain",
        "ImageViewerFitWindowPlainNumpad",
        "ImageViewerActualSize",
        "ImageViewerActualSizeNumpad",
        "ImageViewerActualSizePlain",
        "ImageViewerActualSizePlainNumpad",
        "ImageViewerFitSmallDimensionControl",
        "ImageViewerFitSmallDimensionControlNumpad",
        "ImageViewerFitSmallDimensionNumpad"
    };

    public event EventHandler<SettingsFormChangedEventArgs>? SettingsChanged;

    public SettingsForm()
    {
        Text = Localization.T("settings_title");
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.Dpi;
        ShowIcon = false;
        ShowInTaskbar = false;
        ControlBox = false;
        MinimizeBox = false;
        MaximizeBox = false;
        KeyPreview = true;
        MinimumSize = new Size(720, 520);

        var s = AppSettings.Current;
        Size = new Size(Math.Max(760, s.SettingsWidth), Math.Max(560, s.SettingsHeight));
        StartPosition = FormStartPosition.CenterParent;

        // The settings window's own design style (square "Flat" is the default).
        Design.Theme.Style = string.Equals(s.SettingsDesign, "Rounded", StringComparison.OrdinalIgnoreCase)
            ? ThemeStyle.Rounded
            : ThemeStyle.Flat;

        BuildLayout();
        BuildAboutPage();
        _windowsIntegrationSection.DefaultFileManagerRequested += on => ApplyDefaultFileManagerSettings(on);
        _windowsIntegrationSection.ImageViewerAssociationRequested += on => ApplyImageViewerAssociationSettings(on);
        LoadSettings();
        ApplyTheme();

        HookSettingChanges(_generalSection, PendingSettingsSections.General);
        HookSettingChanges(_appBehaviorSection, PendingSettingsSections.AppBehavior);
        _manualActionsSection.SettingsChanged += (sender, e) =>
            QueueSettingsCommit(PendingSettingsSections.ManualActions);
        HookSettingChanges(_llmSettingsSection, PendingSettingsSections.Llm);

        _saveTimer.Tick += (sender, e) => CommitSettings();
        FormClosing += (sender, e) => CommitSettings();

        this.ResizeEnd += (sender, e) =>
        {
            AppSettings.Current.SettingsWidth = this.Width;
            AppSettings.Current.SettingsHeight = this.Height;
            AppSettings.Current.Save();
        };

        Design.Theme.Changed += ApplyTheme;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (Design.Theme.Style == ThemeStyle.Rounded)
            Design.Native.RoundCorners(Handle);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        KeepCenteredInOwner();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_chromeRoot.IsHandleCreated)
            UpdateChromePadding();
        KeepCenteredInOwner();
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        KeepCenteredInOwner();
    }

    /// <summary>Constrains the settings overlay to the owner's client area and keeps it centered.</summary>
    public void SetOwnerClientBounds(Rectangle bounds)
    {
        _ownerClientBounds = bounds;
        KeepCenteredInOwner();
    }

    private void KeepCenteredInOwner()
    {
        if (_positioningToOwner || _ownerClientBounds.Width <= 0 || _ownerClientBounds.Height <= 0 || IsDisposed)
            return;

        var limits = GetOwnerSizeLimits();
        MinimumSize = new Size(limits.MinWidth, limits.MinHeight);
        MaximumSize = new Size(limits.MaxWidth, limits.MaxHeight);
        SetCenteredSize(Width, Height);
    }

    private (int MinWidth, int MinHeight, int MaxWidth, int MaxHeight) GetOwnerSizeLimits()
    {
        int margin = ScaleLogical(OwnerClickAwayMargin);
        int maxWidth = Math.Max(1, _ownerClientBounds.Width - margin * 2);
        int maxHeight = Math.Max(1, _ownerClientBounds.Height - margin * 2);
        int minWidth = Math.Min(ScaleLogical(720), Math.Min(maxWidth,
            Math.Max(ScaleLogical(440), maxWidth * 85 / 100)));
        int minHeight = Math.Min(ScaleLogical(520), Math.Min(maxHeight,
            Math.Max(ScaleLogical(360), maxHeight * 85 / 100)));
        return (minWidth, minHeight, maxWidth, maxHeight);
    }

    private void SetCenteredSize(int width, int height)
    {
        if (_ownerClientBounds.Width <= 0 || _ownerClientBounds.Height <= 0)
            return;

        var limits = GetOwnerSizeLimits();
        width = Math.Clamp(width, limits.MinWidth, limits.MaxWidth);
        height = Math.Clamp(height, limits.MinHeight, limits.MaxHeight);
        int left = _ownerClientBounds.Left + (_ownerClientBounds.Width - width) / 2;
        int top = _ownerClientBounds.Top + (_ownerClientBounds.Height - height) / 2;

        _positioningToOwner = true;
        try { SetBounds(left, top, width, height, BoundsSpecified.All); }
        finally { _positioningToOwner = false; }
    }

    public bool IsManualResizing => _manualResizing;

    public bool TryGetResizeCursor(Point screenPoint, out Cursor resizeCursor)
    {
        ResizeEdges edges = GetResizeEdges(screenPoint);
        resizeCursor = CursorForEdges(edges);
        return edges != ResizeEdges.None;
    }

    public bool TryBeginManualResize(Point screenPoint)
    {
        if (_manualResizing || !TryGetResizeCursor(screenPoint, out var resizeCursor))
            return false;

        _manualResizeEdges = GetResizeEdges(screenPoint);
        _resizeStartPointer = screenPoint;
        _resizeStartSize = Size;
        _manualResizing = true;
        Cursor = resizeCursor;
        Capture = true;
        Cursor.Current = resizeCursor;
        return true;
    }

    private ResizeEdges GetResizeEdges(Point screenPoint)
    {
        if (!Bounds.Contains(screenPoint))
            return ResizeEdges.None;

        int border = ScaleLogical(ResizeBorder);
        Point point = PointToClient(screenPoint);
        ResizeEdges edges = ResizeEdges.None;
        if (point.X <= border) edges |= ResizeEdges.Left;
        if (point.X >= ClientSize.Width - border) edges |= ResizeEdges.Right;
        if (point.Y <= border) edges |= ResizeEdges.Top;
        if (point.Y >= ClientSize.Height - border) edges |= ResizeEdges.Bottom;
        return edges;
    }

    private static Cursor CursorForEdges(ResizeEdges edges)
    {
        bool horizontal = (edges & (ResizeEdges.Left | ResizeEdges.Right)) != 0;
        bool vertical = (edges & (ResizeEdges.Top | ResizeEdges.Bottom)) != 0;
        if (horizontal && vertical)
        {
            bool sameDirection = (edges.HasFlag(ResizeEdges.Left) && edges.HasFlag(ResizeEdges.Top)) ||
                (edges.HasFlag(ResizeEdges.Right) && edges.HasFlag(ResizeEdges.Bottom));
            return sameDirection ? Cursors.SizeNWSE : Cursors.SizeNESW;
        }
        if (horizontal) return Cursors.SizeWE;
        if (vertical) return Cursors.SizeNS;
        return Cursors.Default;
    }

    private void UpdateManualResize(Point screenPoint)
    {
        int dx = screenPoint.X - _resizeStartPointer.X;
        int dy = screenPoint.Y - _resizeStartPointer.Y;
        int widthDelta = _manualResizeEdges.HasFlag(ResizeEdges.Right) ? dx * 2 :
            _manualResizeEdges.HasFlag(ResizeEdges.Left) ? -dx * 2 : 0;
        int heightDelta = _manualResizeEdges.HasFlag(ResizeEdges.Bottom) ? dy * 2 :
            _manualResizeEdges.HasFlag(ResizeEdges.Top) ? -dy * 2 : 0;
        SetCenteredSize(_resizeStartSize.Width + widthDelta, _resizeStartSize.Height + heightDelta);
    }

    private void EndManualResize()
    {
        if (!_manualResizing) return;
        _manualResizing = false;
        _manualResizeEdges = ResizeEdges.None;
        Cursor = Cursors.Default;
        Capture = false;
        AppSettings.Current.SettingsWidth = Width;
        AppSettings.Current.SettingsHeight = Height;
        AppSettings.Current.Save();
    }

    protected override void WndProc(ref Message m)
    {
        const int WmNcHitTest = 0x84;
        const int WmMouseMove = 0x0200;
        const int WmLeftButtonUp = 0x0202;

        if (m.Msg == WmMouseMove && _manualResizing)
        {
            UpdateManualResize(Cursor.Position);
            m.Result = IntPtr.Zero;
            return;
        }

        if (m.Msg == WmLeftButtonUp && _manualResizing)
        {
            EndManualResize();
            m.Result = (IntPtr)1;
            return;
        }

        if (m.Msg == WmNcHitTest)
        {
            m.Result = (IntPtr)1; // HTCLIENT: resizing is handled by the app's own edge hit zones.
            return;
        }

        if (m.Msg is 0x0085 or 0x0086) // WM_NCPAINT / WM_NCACTIVATE
        {
            m.Result = (IntPtr)1;
            return;
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Design.Theme.Changed -= ApplyTheme;
            _saveTimer.Stop();
            _saveTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private void HookSettingChanges(Control parent, PendingSettingsSections section)
    {
        foreach (Control child in parent.Controls)
        {
            bool refreshMainWindow = section == PendingSettingsSections.Llm ||
                (section == PendingSettingsSections.General &&
                 _generalSection.RequiresMainWindowRefresh(child));
            bool refreshMainTheme = section == PendingSettingsSections.General &&
                _generalSection.RequiresMainThemeRefresh(child);
            switch (child)
            {
                case ToggleSwitch toggle:
                    toggle.CheckedChanged += (sender, e) => QueueSettingsCommit(section, refreshMainWindow, refreshMainTheme);
                    break;
                case Dropdown dropdown:
                    dropdown.ValueChanged += (sender, e) => QueueSettingsCommit(section, refreshMainWindow, refreshMainTheme);
                    break;
                case TextField textField:
                    textField.ValueChanged += (sender, e) => QueueSettingsCommit(section, refreshMainWindow, refreshMainTheme);
                    break;
                case SliderField slider:
                    slider.ValueChanged += (sender, e) => QueueSettingsCommit(section, refreshMainWindow, refreshMainTheme);
                    break;
            }
            HookSettingChanges(child, section);
        }
    }

    private void QueueSettingsCommit(
        PendingSettingsSections sections = PendingSettingsSections.None,
        bool refreshMainWindow = false,
        bool refreshMainTheme = false,
        bool reloadHotkeys = false)
    {
        _hasPendingChanges = true;
        _pendingSections |= sections;
        _refreshMainWindow |= refreshMainWindow;
        _refreshMainTheme |= refreshMainTheme;
        _reloadHotkeys |= reloadHotkeys;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // ---------------------------------------------------------------- layout

    private void BuildLayout()
    {
        _pages.Dock = DockStyle.Fill;
        _pages.BackColor = Design.Theme.Current.Page;

        _nav.Dock = DockStyle.Fill;

        var center = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        center.Controls.Add(_nav, 0, 0);
        center.Controls.Add(_pages, 1, 0);

        _chromeRoot.Dock = DockStyle.Fill;
        _chromeRoot.Controls.Add(center);
        _chromeRoot.Padding = new Padding(ScaleLogical(1));
        Controls.Add(_chromeRoot);

        _pageOrder = new Control[]
        {
            _generalSection, _appBehaviorSection, _manualActionsSection,
            _windowsIntegrationSection, _llmSettingsSection, _hotkeysSection, _aboutSection
        };

        // Pages
        foreach (var p in _pageOrder)
        {
            p.Visible = false;
            _pages.Controls.Add(p);
        }

        _nav.Add(Localization.T("general_tab"), "");
        _nav.Add(Localization.T("app_behavior_tab"), "");
        _nav.Add(Localization.T("context_menu_tab"), "");
        _nav.Add(Localization.T("windows_integration_tab"), "");
        _nav.Add(Localization.T("llm_tab"), "");
        _nav.Add(Localization.T("hotkeys_tab"), "");
        _nav.Add(Localization.T("about_tab"), "");
        _nav.SelectedIndexChanged += (sender, e) => ShowPage(_nav.SelectedIndex);

        ShowPage(0);
    }

    private void ShowPage(int index)
    {
        foreach (Control p in _pageOrder)
            p.Visible = false;
        if (index >= 0 && index < _pageOrder.Length)
            _pageOrder[index].Visible = true;
    }

    // ---------------------------------------------------------------- theme

    /// <summary>Re-themes every control that doesn't paint itself.</summary>
    private void ApplyTheme()
    {
        var p = Design.Theme.Current;
        BackColor = p.Page;
        ForeColor = p.Text;
        _chromeRoot.BackColor = p.IsDark
            ? Color.FromArgb(45, 45, 48)
            : Color.FromArgb(214, 214, 220);
        UpdateChromePadding();
        _pages.BackColor = p.Page;
        foreach (Control page in _pageOrder)
        {
            page.BackColor = p.Page;
            if (page is SettingsPage settingsPage)
                settingsPage.ApplyScrollbarTheme();
        }
        _nav.Invalidate();
        ThemeWalk(this, p);
    }

    private void UpdateChromePadding()
    {
        _chromeRoot.Padding = new Padding(ScaleLogical(1));
    }

    private int ScaleLogical(int pixels) => (int)Math.Round(pixels * (DeviceDpi / 96.0));

    private static void ThemeWalk(Control c, Palette p)
    {
        foreach (Control child in c.Controls)
        {
            if (child is IThemed)
            {
                if (child is NavList)
                {
                    // Match the main window's navigation strip surface (PanelBackground).
                    child.BackColor = p.IsDark
                        ? Color.FromArgb(30, 30, 30)
                        : Color.FromArgb(238, 238, 242);
                }
                child.Invalidate();
                ThemeWalk(child, p);
                continue;
            }
            string? tag = child.Tag as string;
            if (tag == "keep-color")
            {
                ThemeWalk(child, p);
                continue;
            }
            if (child is TextBox or ComboBox or ListBox or NumericUpDown)
            {
                child.BackColor = p.Field;
                child.ForeColor = p.Text;
            }
            else
            {
                child.ForeColor = tag == "dim" ? p.TextDim : p.Text;
            }
            ThemeWalk(child, p);
        }
    }

    private static int MeasureButtonWidth(string text, Font? font = null)
        => TextRenderer.MeasureText(text, font ?? SystemFonts.MessageBoxFont).Width + 32;

    // ---------------------------------------------------------------- pages

    private void BuildAboutPage()
    {
        _aboutSection.Font = new Font("Segoe UI", 10);

        _aboutSection.Controls.Add(PageLabel(Localization.T("about_title")));

        var antigravity = new SettingsGroup();
        foreach (var model in new[] { "Claude Opus 4.6", "Claude Opus 4.5", "Gemini 3.1 Pro", "Gemini 3.0 Pro", "Gemini 3.0 Flash" })
            antigravity.AddRow(model, string.Empty, CloneHost());
        _aboutSection.Controls.Add(PageLabel(Localization.T("antigravity")));
        _aboutSection.Controls.Add(antigravity);

        var codex = new SettingsGroup();
        foreach (var model in new[] { "GPT-5.2-Codex", "GPT-5.3-Codex", "GPT-5.4", "GPT-5.5", "GPT-5.6 Luna, Terra", "GPT-6 Luna" })
            codex.AddRow(model, string.Empty, CloneHost());
        _aboutSection.Controls.Add(PageLabel(Localization.T("codex")));
        _aboutSection.Controls.Add(codex);

        var honorableMention = new SettingsGroup();
        honorableMention.AddRow(Localization.T("ran_locally"), string.Empty, CloneHost());
        _aboutSection.Controls.Add(PageLabel(Localization.T("honorable_mention")));
        _aboutSection.Controls.Add(honorableMention);

        var misc = new SettingsGroup();
        misc.AddButton(Localization.T("directed_by"), string.Empty, Localization.T("view_license"), ButtonKind.Secondary, ViewLicense);
        _aboutSection.Controls.Add(misc);
    }

    private static Label PageLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Tag = "section-title",
        Margin = new Padding(2, 16, 0, 6)
    };

    /// <summary>Zero-size host for title-only rows.</summary>
    private static Label CloneHost() => new() { Text = string.Empty, Size = new Size(1, 1), Margin = Padding.Empty };

    private void ViewLicense()
    {
        try
        {
            string licensePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LICENSE");
            if (!File.Exists(licensePath))
            {
                // Fallback for development: check parent directories
                var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LICENSE")))
                    dir = dir.Parent;

                if (dir != null) licensePath = Path.Combine(dir.FullName, "LICENSE");
            }

            if (File.Exists(licensePath))
                Process.Start(new ProcessStartInfo(licensePath) { UseShellExecute = true });
            else
                MessageBox.Show("LICENSE file not found.", "Error");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open LICENSE: {ex.Message}", "Error");
        }
    }

    // ---------------------------------------------------------------- hotkeys

    private void PopulateHotkeys()
    {
        _hotkeysSection.Font = new Font("Segoe UI", 10);
        _hotkeysSection.Controls.Clear();
        _hotkeyButtons.Clear();

        var revert = new FlatButton
        {
            Text = Localization.T("hotkeys_revert"),
            Kind = ButtonKind.Secondary,
            Size = new Size(MeasureButtonWidth(Localization.T("hotkeys_revert"), _hotkeysSection.Font), 32),
            Margin = new Padding(0, 0, 0, 8)
        };
        revert.Click += (sender, e) => RevertToDefaults();
        _hotkeysSection.Controls.Add(revert);

        var mainAppHotkeys = _hotkeyEdits.Where(kvp => !ImageViewerHotkeyActions.Contains(kvp.Key)).ToList();
        var imageViewerHotkeys = _hotkeyEdits.Where(kvp => ImageViewerHotkeyActions.Contains(kvp.Key)).ToList();

        _hotkeysSection.Controls.Add(PageLabel(Localization.T("hotkeys_main_app_section")));
        var mainGroup = new SettingsGroup();
        foreach (var kvp in mainAppHotkeys)
            AddHotkeyRow(mainGroup, kvp.Key, kvp.Value);
        _hotkeysSection.Controls.Add(mainGroup);

        _hotkeysSection.Controls.Add(PageLabel(Localization.T("hotkeys_image_viewer_section")));
        var ivGroup = new SettingsGroup();
        foreach (var kvp in imageViewerHotkeys)
            AddHotkeyRow(ivGroup, kvp.Key, kvp.Value);
        _hotkeysSection.Controls.Add(ivGroup);
    }

    private void AddHotkeyRow(SettingsGroup group, string key, string value)
    {
        var btn = new FlatButton
        {
            Text = FormatHotkey(value),
            Kind = ButtonKind.Secondary,
            Size = new Size(180, 32),
            Margin = Padding.Empty
        };
        btn.Click += (sender, e) => HotkeyBtn_Click(key);
        group.AddRow(GetActionName(key), string.Empty, btn);
        _hotkeyButtons[key] = btn;
    }

    private string FormatHotkey(string keys) => (keys ?? "None").Replace(", ", "+");

    private void RevertToDefaults()
    {
        var temp = new AppSettings();
        foreach (var kvp in temp.Hotkeys)
        {
            _hotkeyEdits[kvp.Key] = kvp.Value;
            if (_hotkeyButtons.TryGetValue(kvp.Key, out var btn))
                btn.Text = FormatHotkey(kvp.Value);
        }
        QueueSettingsCommit(reloadHotkeys: true);
    }

    private void HotkeyBtn_Click(string action)
    {
        if (_recordingAction != null && _recordingAction != action)
            ResetRecording();

        _recordingAction = action;
        if (!_hotkeyButtons.TryGetValue(action, out var btn))
            return;
        btn.Kind = ButtonKind.Primary;
        btn.Text = "Recording...";
        btn.Focus();
    }

    private void ResetRecording()
    {
        var action = _recordingAction;
        _recordingAction = null;
        if (action == null || !_hotkeyButtons.TryGetValue(action, out var btn))
            return;
        btn.Kind = ButtonKind.Secondary;
        btn.Text = FormatHotkey(_hotkeyEdits[action]);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var action = _recordingAction;
        if (action != null)
        {
            if (e.KeyCode == Keys.Escape)
            {
                ResetRecording();
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu)
                return;

            var keyData = e.KeyData;
            var keyString = new KeysConverter().ConvertToString(keyData) ?? "None";

            // Overlap handling
            foreach (var kvp in _hotkeyEdits.ToList())
            {
                if (kvp.Value == keyString && kvp.Key != action &&
                    IsImageViewerHotkey(kvp.Key) == IsImageViewerHotkey(action))
                {
                    _hotkeyEdits[kvp.Key] = "None";
                    if (_hotkeyButtons.TryGetValue(kvp.Key, out var otherBtn))
                        otherBtn.Text = "None";
                }
            }

            _hotkeyEdits[action] = keyString;
            var targetBtn = _hotkeyButtons[action];
            targetBtn.Text = FormatHotkey(keyString);
            ResetRecording();
            QueueSettingsCommit(reloadHotkeys: true);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else
        {
            base.OnKeyDown(e);
        }
    }

    private static bool IsImageViewerHotkey(string action)
        => ImageViewerHotkeyActions.Contains(action);

    private string GetActionName(string action) => action switch
    {
        "NavBack" => Localization.T("hotkey_nav_back"),
        "NavForward" => Localization.T("hotkey_nav_forward"),
        "FocusAddress" => Localization.T("hotkey_focus_address"),
        "FocusSearch" => Localization.T("hotkey_focus_search"),
        "FocusTagSearch" => Localization.T("hotkey_focus_tag_search"),
        "FocusSidebar" => Localization.T("hotkey_focus_sidebar"),
        "Refresh" => Localization.T("hotkey_refresh"),
        "ShowProperties" => Localization.T("hotkey_properties"),
        "OpenSettings" => Localization.T("hotkey_open_settings"),
        "TogglePin" => Localization.T("hotkey_toggle_pin"),
        "ToggleFullscreen" => Localization.T("hotkey_toggle_fullscreen"),
        "Rename" => Localization.T("hotkey_rename"),
        "QuickLook" => Localization.T("hotkey_quicklook"),
        "SelectAll" => Localization.T("hotkey_select_all"),
        "FocusFilePanel" => Localization.T("hotkey_focus_file"),
        "FocusAI" => Localization.T("hotkey_focus_ai"),
        "ToggleSidebar" => Localization.T("hotkey_toggle_sidebar"),
        "EditTags" => Localization.T("hotkey_edit_tags"),
        "CloseApp" => Localization.T("hotkey_close_app"),
        "Copy" => Localization.T("hotkey_copy"),
        "Cut" => Localization.T("hotkey_cut"),
        "Paste" => Localization.T("hotkey_paste"),
        "Delete" => Localization.T("hotkey_delete"),
        "DeletePermanent" => Localization.T("hotkey_delete_perm"),
        "Undo" => Localization.T("hotkey_undo"),
        "Redo" => Localization.T("hotkey_redo"),
        "NewTab" => Localization.T("hotkey_new_tab"),
        "NextTab" => Localization.T("hotkey_next_tab"),
        "PrevTab" => Localization.T("hotkey_prev_tab"),
        "CloseTab" => Localization.T("hotkey_close_tab"),
        "SwitchTab1" => $"{Localization.T("hotkey_switch_tab")} 1",
        "SwitchTab2" => $"{Localization.T("hotkey_switch_tab")} 2",
        "SwitchTab3" => $"{Localization.T("hotkey_switch_tab")} 3",
        "SwitchTab4" => $"{Localization.T("hotkey_switch_tab")} 4",
        "SwitchTab5" => $"{Localization.T("hotkey_switch_tab")} 5",
        "SwitchTab6" => $"{Localization.T("hotkey_switch_tab")} 6",
        "SwitchTab7" => $"{Localization.T("hotkey_switch_tab")} 7",
        "SwitchTab8" => $"{Localization.T("hotkey_switch_tab")} 8",
        "SwitchTab9" => $"{Localization.T("hotkey_switch_tab")} 9",
        "ClearSelection" => Localization.T("hotkey_clear_selection"),
        "OpenSelected" => Localization.T("hotkey_open_selected"),
        "NavigateUp" => Localization.T("hotkey_navigate_up"),
        "ZoomIconsIn" => Localization.T("hotkey_zoom_icons_in"),
        "ZoomIconsInNumpad" => Localization.T("hotkey_zoom_icons_in_numpad"),
        "ZoomIconsOut" => Localization.T("hotkey_zoom_icons_out"),
        "ZoomIconsOutNumpad" => Localization.T("hotkey_zoom_icons_out_numpad"),
        "ToggleOcrBoxes" => Localization.T("hotkey_toggle_ocr_boxes"),
        "ToggleSavedTranslation" => Localization.T("hotkey_toggle_saved_translation"),
        "FitSmallDimension" => Localization.T("hotkey_fit_small_dimension"),
        "ImageViewerToggleAI" => Localization.T("hotkey_image_viewer_toggle_ai"),
        "ImageViewerStartTranslation" => Localization.T("hotkey_image_viewer_start_translation"),
        "ImageViewerStartOcr" => Localization.T("hotkey_image_viewer_start_ocr"),
        "ImageViewerTag" => Localization.T("hotkey_image_viewer_tag"),
        "ImageViewerPrevious" => Localization.T("hotkey_image_viewer_previous"),
        "ImageViewerPreviousAlt" => Localization.T("hotkey_image_viewer_previous_alt"),
        "ImageViewerNext" => Localization.T("hotkey_image_viewer_next"),
        "ImageViewerNextAlt" => Localization.T("hotkey_image_viewer_next_alt"),
        "ImageViewerNextSpace" => Localization.T("hotkey_image_viewer_next_space"),
        "ImageViewerClose" => Localization.T("hotkey_image_viewer_close"),
        "ImageViewerToggleFullscreen" => Localization.T("hotkey_image_viewer_fullscreen"),
        "ImageViewerToggleFullscreenAlt" => Localization.T("hotkey_image_viewer_fullscreen_alt"),
        "ImageViewerZoomIn" => Localization.T("hotkey_image_viewer_zoom_in"),
        "ImageViewerZoomInAlt" => Localization.T("hotkey_image_viewer_zoom_in_alt"),
        "ImageViewerZoomOut" => Localization.T("hotkey_image_viewer_zoom_out"),
        "ImageViewerZoomOutAlt" => Localization.T("hotkey_image_viewer_zoom_out_alt"),
        "ImageViewerRotate" => Localization.T("hotkey_image_viewer_rotate"),
        "ImageViewerFitWindow" => Localization.T("hotkey_image_viewer_fit_window"),
        "ImageViewerFitWindowNumpad" => Localization.T("hotkey_image_viewer_fit_window_numpad"),
        "ImageViewerFitWindowPlain" => Localization.T("hotkey_image_viewer_fit_window_plain"),
        "ImageViewerFitWindowPlainNumpad" => Localization.T("hotkey_image_viewer_fit_window_plain_numpad"),
        "ImageViewerActualSize" => Localization.T("hotkey_image_viewer_actual_size"),
        "ImageViewerActualSizeNumpad" => Localization.T("hotkey_image_viewer_actual_size_numpad"),
        "ImageViewerActualSizePlain" => Localization.T("hotkey_image_viewer_actual_size_plain"),
        "ImageViewerActualSizePlainNumpad" => Localization.T("hotkey_image_viewer_actual_size_plain_numpad"),
        "ImageViewerFitSmallDimensionControl" => Localization.T("hotkey_image_viewer_fit_small_control"),
        "ImageViewerFitSmallDimensionControlNumpad" => Localization.T("hotkey_image_viewer_fit_small_control_numpad"),
        "ImageViewerFitSmallDimensionNumpad" => Localization.T("hotkey_image_viewer_fit_small_numpad"),
        _ => action
    };

    // ---------------------------------------------------------------- data

    private void LoadSettings()
    {
        AppSettings.ReloadCurrent();
        var s = AppSettings.Current;
        _generalSection.LoadFromSettings(s);
        _appBehaviorSection.LoadFromSettings(s);
        _manualActionsSection.LoadFromSettings(s);
        _llmSettingsSection.LoadFromSettings(s);

        UpdateDefaultFileManagerStatusLabel();
        UpdateImageViewerAssociationStatusLabel();

        _hotkeyEdits.Clear();
        foreach (var kvp in s.Hotkeys) _hotkeyEdits[kvp.Key] = kvp.Value;
        PopulateHotkeys();
    }

    private void UpdateDefaultFileManagerStatusLabel()
    {
        var status = FileManagerIntegrationService.GetCurrentStatus();
        switch (status)
        {
            case FileManagerIntegrationService.IntegrationStatus.AppliedHkcuHklm:
                _windowsIntegrationSection.SetDefaultFileManagerStatus(Localization.T("status_applied"), Color.LightGreen);
                break;
            case FileManagerIntegrationService.IntegrationStatus.AppliedHkcu:
                _windowsIntegrationSection.SetDefaultFileManagerStatus(Localization.T("status_applied_hkcu"), Color.LightGreen);
                break;
            case FileManagerIntegrationService.IntegrationStatus.AppliedHklm:
                _windowsIntegrationSection.SetDefaultFileManagerStatus(Localization.T("status_applied_hklm"), Color.LightGreen);
                break;
            default:
                _windowsIntegrationSection.SetDefaultFileManagerStatus(Localization.T("status_defaults"), Design.Theme.Current.TextDim);
                break;
        }
    }

    private void ApplyDefaultFileManagerSettings(bool enable)
    {
        var result = FileManagerIntegrationService.ApplyFromUi(enable, this);
        if (result.ElevationLaunched)
        {
            AppSettings.ReloadCurrent();
            UpdateDefaultFileManagerStatusLabel();
            StartDefaultFileManagerStatusPolling();
            return;
        }

        if (!result.Success)
        {
            MessageBox.Show("Failed to apply default file manager settings.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateDefaultFileManagerStatusLabel();
            return;
        }

        AppSettings.ReloadCurrent();
        UpdateDefaultFileManagerStatusLabel();
        StartDefaultFileManagerStatusPolling();
    }

    private void StartDefaultFileManagerStatusPolling()
    {
        int remaining = 10;
        var t = new System.Windows.Forms.Timer { Interval = 500 };
        t.Tick += (s, e) =>
        {
            if (remaining-- <= 0)
            {
                t.Stop();
                t.Dispose();
                return;
            }
            UpdateDefaultFileManagerStatusLabel();
        };
        t.Start();
    }

    private void UpdateImageViewerAssociationStatusLabel()
    {
        var status = ImageViewerAssociationService.GetCurrentStatus();
        switch (status)
        {
            case ImageViewerAssociationService.AssociationStatus.AppliedHkcu:
                _windowsIntegrationSection.SetImageViewerAssociationStatus(Localization.T("status_applied_hkcu"), Color.LightGreen);
                break;
            case ImageViewerAssociationService.AssociationStatus.PartialHkcu:
                _windowsIntegrationSection.SetImageViewerAssociationStatus(Localization.T("status_partial_hkcu"), Color.Khaki);
                break;
            default:
                _windowsIntegrationSection.SetImageViewerAssociationStatus(Localization.T("status_defaults"), Design.Theme.Current.TextDim);
                break;
        }
    }

    private void ApplyImageViewerAssociationSettings(bool enable)
    {
        var result = ImageViewerAssociationService.ApplyFromUi(enable, this);
        if (!result.Success)
        {
            MessageBox.Show("Failed to apply image file default settings.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateImageViewerAssociationStatusLabel();
            return;
        }

        AppSettings.ReloadCurrent();
        UpdateImageViewerAssociationStatusLabel();
        StartImageViewerAssociationStatusPolling();
    }

    private void StartImageViewerAssociationStatusPolling()
    {
        int remaining = 10;
        var t = new System.Windows.Forms.Timer { Interval = 500 };
        t.Tick += (s, e) =>
        {
            if (remaining-- <= 0)
            {
                t.Stop();
                t.Dispose();
                return;
            }
            UpdateImageViewerAssociationStatusLabel();
        };
        t.Start();
    }

    // ---------------------------------------------------------------- save

    private void CommitSettings()
    {
        _saveTimer.Stop();
        if (!_hasPendingChanges || _isCommitting) return;

        _isCommitting = true;
        try
        {
            var s = AppSettings.Current;
            bool generalChanged = _pendingSections.HasFlag(PendingSettingsSections.General);
            bool startupChanged = generalChanged && s.RunAtStartup != _generalSection.RunAtStartup;
            bool trayIconChanged = generalChanged && s.ShowTrayIcon != _generalSection.ShowTrayIcon;
            if (generalChanged)
                _generalSection.ApplyToSettings(s);
            if (_pendingSections.HasFlag(PendingSettingsSections.Llm))
                _llmSettingsSection.ApplyToSettings(s);
            if (_pendingSections.HasFlag(PendingSettingsSections.AppBehavior))
                _appBehaviorSection.ApplyToSettings(s);
            if (_pendingSections.HasFlag(PendingSettingsSections.ManualActions))
                _manualActionsSection.ApplyToSettings(s);

            if (_reloadHotkeys)
                foreach (var kvp in _hotkeyEdits) s.Hotkeys[kvp.Key] = kvp.Value;
            s.Save();
            if (startupChanged)
                StartupService.SyncWithSettings();
            if (trayIconChanged)
            {
                try { Program.MultiWindowContext.Instance.SetTrayIconVisible(_generalSection.ShowTrayIcon); }
                catch (Exception ex) { Debug.WriteLine(ex); }
            }
            Design.Theme.Refresh();

            _hasPendingChanges = false;
            var change = new SettingsFormChangedEventArgs(_refreshMainWindow, _refreshMainTheme, _reloadHotkeys);
            _pendingSections = PendingSettingsSections.None;
            _refreshMainWindow = false;
            _refreshMainTheme = false;
            _reloadHotkeys = false;
            SettingsChanged?.Invoke(this, change);
        }
        finally
        {
            _isCommitting = false;
        }
    }
}
