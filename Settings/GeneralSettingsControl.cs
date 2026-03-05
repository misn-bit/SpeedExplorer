using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

public sealed class GeneralSettingsControl : UserControl
{
    private readonly NumericUpDown _fontSizeNum;
    private readonly NumericUpDown _iconSizeNum;
    private readonly CheckBox _showIconsChk;
    private readonly CheckBox _useEmojiChk;
    private readonly CheckBox _useSystemIconsChk;
    private readonly CheckBox _resolveUniqueChk;
    private readonly CheckBox _showThumbnailsChk;
    private readonly CheckBox _showCommonChk;
    private readonly CheckBox _showDesktopChk;
    private readonly CheckBox _showDocumentsChk;
    private readonly CheckBox _showDownloadsChk;
    private readonly CheckBox _showPicturesChk;
    private readonly CheckBox _showRecentChk;
    private readonly CheckBox _showSidebarVScrollChk;
    private readonly CheckBox _runAtStartupChk;
    private readonly CheckBox _runInBackgroundChk;
    private readonly CheckBox _showTrayIconChk;
    private readonly CheckBox _debugNavLogChk;
    private readonly CheckBox _debugNavGcChk;
    private readonly CheckBox _debugNavUiQueueChk;
    private readonly CheckBox _debugNavPostBindChk;
    private readonly ComboBox _languageCombo;

    private int Scale(int pixels) => (int)(pixels * (DeviceDpi / 96.0));

    public GeneralSettingsControl()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Color.Transparent;
        Margin = new Padding(0);

        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        Controls.Add(panel);

        var sizeRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, Scale(6))
        };
        sizeRow.Controls.Add(CreateLabel(Localization.T("font_size"), Point.Empty));
        _fontSizeNum = CreateNumeric(8, 24, Point.Empty);
        _fontSizeNum.Width = Scale(90);
        sizeRow.Controls.Add(_fontSizeNum);
        sizeRow.Controls.Add(new Panel { Width = Scale(24), Height = 1, BackColor = Color.Transparent });
        sizeRow.Controls.Add(CreateLabel(Localization.T("icon_size"), Point.Empty));
        _iconSizeNum = CreateNumeric(16, 192, Point.Empty);
        _iconSizeNum.Width = Scale(90);
        sizeRow.Controls.Add(_iconSizeNum);
        panel.Controls.Add(sizeRow);

        var langRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, Scale(6))
        };
        langRow.Controls.Add(CreateLabel(Localization.T("language"), Point.Empty));
        _languageCombo = new ComboBox
        {
            Width = Scale(160),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _languageCombo.Items.AddRange(new object[] { "English", "Русский" });
        langRow.Controls.Add(_languageCombo);
        panel.Controls.Add(langRow);

        panel.Controls.Add(new Panel { Height = Scale(20), Width = Scale(1), BackColor = Color.Transparent });
        var startupHeader = CreateLabel(Localization.T("startup_bg"), Point.Empty);
        ApplySectionHeaderStyle(startupHeader);
        panel.Controls.Add(startupHeader);

        _runAtStartupChk = CreateCheckBox(Localization.T("run_startup"), Point.Empty);
        panel.Controls.Add(_runAtStartupChk);

        _runInBackgroundChk = CreateCheckBox(Localization.T("run_in_background"), Point.Empty);
        panel.Controls.Add(_runInBackgroundChk);

        _showTrayIconChk = CreateCheckBox(Localization.T("show_tray_icon"), Point.Empty);
        panel.Controls.Add(_showTrayIconChk);

        panel.Controls.Add(new Panel { Height = Scale(20), Width = Scale(1), BackColor = Color.Transparent });
        var iconsHeader = CreateLabel(Localization.T("icons_section"), Point.Empty);
        ApplySectionHeaderStyle(iconsHeader);
        panel.Controls.Add(iconsHeader);

        _showIconsChk = CreateCheckBox(Localization.T("show_icons"), Point.Empty);
        _showIconsChk.CheckedChanged += (s, e) => UpdateToggles();
        panel.Controls.Add(_showIconsChk);

        _useEmojiChk = CreateCheckBox(Localization.T("use_emoji"), Point.Empty);
        _useEmojiChk.Padding = new Padding(Scale(20), 0, 0, 0);
        _useEmojiChk.CheckedChanged += (s, e) => UpdateToggles();
        panel.Controls.Add(_useEmojiChk);

        _useSystemIconsChk = CreateCheckBox(Localization.T("use_colored_icons"), Point.Empty);
        _useSystemIconsChk.Padding = new Padding(Scale(20), 0, 0, 0);
        panel.Controls.Add(_useSystemIconsChk);

        _resolveUniqueChk = CreateCheckBox(Localization.T("resolve_unique"), Point.Empty);
        _resolveUniqueChk.Padding = new Padding(Scale(20), 0, 0, 0);
        panel.Controls.Add(_resolveUniqueChk);

        _showThumbnailsChk = CreateCheckBox(Localization.T("show_thumbnails"), Point.Empty);
        _showThumbnailsChk.Padding = new Padding(Scale(20), 0, 0, 0);
        panel.Controls.Add(_showThumbnailsChk);

        panel.Controls.Add(new Panel { Height = Scale(10), Width = Scale(1), BackColor = Color.Transparent });

        var windowsFoldersHeader = CreateLabel(Localization.T("windows_folders_section"), Point.Empty);
        ApplySectionHeaderStyle(windowsFoldersHeader);
        panel.Controls.Add(windowsFoldersHeader);

        _showRecentChk = CreateCheckBox(Localization.T("recent_folders"), Point.Empty);
        panel.Controls.Add(_showRecentChk);

        panel.Controls.Add(new Panel { Height = Scale(8), Width = Scale(1), BackColor = Color.Transparent });

        _showCommonChk = CreateCheckBox(Localization.T("show_windows_folders"), Point.Empty);
        _showCommonChk.CheckedChanged += (s, e) => UpdateToggles();
        panel.Controls.Add(_showCommonChk);

        _showDesktopChk = CreateCheckBox(Localization.T("desktop"), Point.Empty);
        _showDesktopChk.Padding = new Padding(Scale(20), 0, 0, 0);
        panel.Controls.Add(_showDesktopChk);

        _showDocumentsChk = CreateCheckBox(Localization.T("documents"), Point.Empty);
        _showDocumentsChk.Padding = new Padding(Scale(20), 0, 0, 0);
        panel.Controls.Add(_showDocumentsChk);

        _showDownloadsChk = CreateCheckBox(Localization.T("downloads"), Point.Empty);
        _showDownloadsChk.Padding = new Padding(Scale(20), 0, 0, 0);
        panel.Controls.Add(_showDownloadsChk);

        _showPicturesChk = CreateCheckBox(Localization.T("pictures"), Point.Empty);
        _showPicturesChk.Padding = new Padding(Scale(20), 0, 0, 0);
        panel.Controls.Add(_showPicturesChk);

        _showSidebarVScrollChk = CreateCheckBox(Localization.T("show_sidebar_scrollbar"), Point.Empty);
        panel.Controls.Add(_showSidebarVScrollChk);

        panel.Controls.Add(new Panel { Height = Scale(20), Width = Scale(1), BackColor = Color.Transparent });

        var debugHeader = CreateLabel(Localization.T("debug_section"), Point.Empty);
        ApplySectionHeaderStyle(debugHeader);
        panel.Controls.Add(debugHeader);

        _debugNavLogChk = CreateCheckBox(Localization.T("debug_nav_log"), Point.Empty);
        _debugNavLogChk.CheckedChanged += (s, e) => UpdateToggles();
        panel.Controls.Add(_debugNavLogChk);

        _debugNavGcChk = CreateCheckBox(Localization.T("debug_nav_gc"), Point.Empty);
        panel.Controls.Add(_debugNavGcChk);

        _debugNavUiQueueChk = CreateCheckBox(Localization.T("debug_nav_uiq"), Point.Empty);
        panel.Controls.Add(_debugNavUiQueueChk);

        _debugNavPostBindChk = CreateCheckBox(Localization.T("debug_nav_postbind"), Point.Empty);
        panel.Controls.Add(_debugNavPostBindChk);

        panel.Controls.Add(new Panel { Height = Scale(30), Width = Scale(1), BackColor = Color.Transparent });
    }

    public void LoadFromSettings(AppSettings s)
    {
        _fontSizeNum.Value = Math.Clamp(s.FontSize, 8, 24);
        _iconSizeNum.Value = Math.Clamp(s.IconSize, 16, 192);
        _showIconsChk.Checked = s.ShowIcons;
        _useEmojiChk.Checked = s.UseEmojiIcons;
        _useSystemIconsChk.Checked = s.UseSystemIcons;
        _resolveUniqueChk.Checked = s.ResolveUniqueIcons;
        _showThumbnailsChk.Checked = s.ShowThumbnails;
        _showCommonChk.Checked = s.ShowSidebarCommon;
        _showDesktopChk.Checked = s.ShowSidebarDesktop;
        _showDocumentsChk.Checked = s.ShowSidebarDocuments;
        _showDownloadsChk.Checked = s.ShowSidebarDownloads;
        _showPicturesChk.Checked = s.ShowSidebarPictures;
        _showRecentChk.Checked = s.ShowSidebarRecent;
        _showSidebarVScrollChk.Checked = s.ShowSidebarVerticalScrollbar;
        _languageCombo.SelectedIndex = string.Equals(s.UiLanguage, "ru", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _runAtStartupChk.Checked = s.RunAtStartup;
        _runInBackgroundChk.Checked = s.RunInBackground;
        _showTrayIconChk.Checked = s.ShowTrayIcon;
        _debugNavLogChk.Checked = s.DebugNavigationLogging;
        _debugNavGcChk.Checked = s.DebugNavigationGcStats;
        _debugNavUiQueueChk.Checked = s.DebugNavigationUiQueue;
        _debugNavPostBindChk.Checked = s.DebugNavigationPostBind;
        UpdateToggles();
    }

    public void ApplyToSettings(AppSettings s)
    {
        s.FontSize = (int)_fontSizeNum.Value;
        s.IconSize = (int)_iconSizeNum.Value;
        s.ShowIcons = _showIconsChk.Checked;
        s.UseEmojiIcons = _useEmojiChk.Checked;
        s.UseSystemIcons = _useSystemIconsChk.Checked;
        s.ResolveUniqueIcons = _resolveUniqueChk.Checked;
        s.ShowThumbnails = _showThumbnailsChk.Checked;
        s.ShowSidebarCommon = _showCommonChk.Checked;
        s.ShowSidebarDesktop = _showDesktopChk.Checked;
        s.ShowSidebarDocuments = _showDocumentsChk.Checked;
        s.ShowSidebarDownloads = _showDownloadsChk.Checked;
        s.ShowSidebarPictures = _showPicturesChk.Checked;
        s.ShowSidebarRecent = _showRecentChk.Checked;
        s.ShowSidebarVerticalScrollbar = _showSidebarVScrollChk.Checked;
        s.UiLanguage = _languageCombo.SelectedIndex == 1 ? "ru" : "en";
        s.RunAtStartup = _runAtStartupChk.Checked;
        s.RunInBackground = _runInBackgroundChk.Checked;
        s.ShowTrayIcon = _showTrayIconChk.Checked;
        s.DebugNavigationLogging = _debugNavLogChk.Checked;
        s.DebugNavigationGcStats = _debugNavGcChk.Checked;
        s.DebugNavigationUiQueue = _debugNavUiQueueChk.Checked;
        s.DebugNavigationPostBind = _debugNavPostBindChk.Checked;
    }

    public bool ShowTrayIcon => _showTrayIconChk.Checked;

    private void UpdateToggles()
    {
        bool show = _showIconsChk.Checked;
        bool emoji = _useEmojiChk.Checked;
        _useEmojiChk.Enabled = show;
        _useSystemIconsChk.Enabled = show && !emoji;
        _resolveUniqueChk.Enabled = show && !emoji;
        _showThumbnailsChk.Enabled = show && !emoji;

        _useEmojiChk.ForeColor = show ? Color.FromArgb(240, 240, 240) : Color.Gray;
        _useSystemIconsChk.ForeColor = (show && !emoji) ? Color.FromArgb(240, 240, 240) : Color.Gray;
        _resolveUniqueChk.ForeColor = (show && !emoji) ? Color.FromArgb(240, 240, 240) : Color.Gray;
        _showThumbnailsChk.ForeColor = (show && !emoji) ? Color.FromArgb(240, 240, 240) : Color.Gray;

        bool common = _showCommonChk.Checked;
        _showDesktopChk.Enabled = common;
        _showDocumentsChk.Enabled = common;
        _showDownloadsChk.Enabled = common;
        _showPicturesChk.Enabled = common;
        _showDesktopChk.ForeColor = common ? Color.FromArgb(240, 240, 240) : Color.Gray;
        _showDocumentsChk.ForeColor = common ? Color.FromArgb(240, 240, 240) : Color.Gray;
        _showDownloadsChk.ForeColor = common ? Color.FromArgb(240, 240, 240) : Color.Gray;
        _showPicturesChk.ForeColor = common ? Color.FromArgb(240, 240, 240) : Color.Gray;
        _showRecentChk.ForeColor = Color.FromArgb(240, 240, 240);
        _showSidebarVScrollChk.ForeColor = Color.FromArgb(240, 240, 240);

        bool navDebug = _debugNavLogChk.Checked;
        _debugNavGcChk.Enabled = navDebug;
        _debugNavUiQueueChk.Enabled = navDebug;
        _debugNavPostBindChk.Enabled = navDebug;
        _debugNavGcChk.ForeColor = navDebug ? Color.FromArgb(240, 240, 240) : Color.Gray;
        _debugNavUiQueueChk.ForeColor = navDebug ? Color.FromArgb(240, 240, 240) : Color.Gray;
        _debugNavPostBindChk.ForeColor = navDebug ? Color.FromArgb(240, 240, 240) : Color.Gray;
    }

    private void ApplySectionHeaderStyle(Label label)
    {
        label.Font = new Font("Segoe UI Semibold", 10);
        label.ForeColor = Color.White;
        label.Margin = new Padding(0, Scale(6), 0, Scale(4));
    }

    private Label CreateLabel(string text, Point loc) => new()
    {
        Text = text,
        Location = loc,
        AutoSize = true,
        Font = new Font("Segoe UI", 10),
        ForeColor = Color.FromArgb(240, 240, 240),
        BackColor = Color.Transparent
    };

    private NumericUpDown CreateNumeric(int min, int max, Point loc) => new()
    {
        Minimum = min,
        Maximum = max,
        Location = loc,
        Size = new Size(80, 25),
        BackColor = Color.FromArgb(60, 60, 60),
        ForeColor = Color.White
    };

    private CheckBox CreateCheckBox(string text, Point loc) => new()
    {
        Text = text,
        Location = loc,
        AutoSize = true,
        Font = new Font("Segoe UI", 10),
        ForeColor = Color.FromArgb(240, 240, 240),
        Cursor = Cursors.Hand,
        BackColor = Color.Transparent
    };
}
