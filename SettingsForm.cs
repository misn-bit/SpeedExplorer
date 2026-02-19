using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpeedExplorer;

public class SettingsForm : Form
{
    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    
    [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HT_CAPTION = 0x2;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private NumericUpDown _fontSizeNum = null!;
    private NumericUpDown _iconSizeNum = null!;
    private CheckBox _showIconsChk = null!;
    private CheckBox _useEmojiChk = null!;
    private CheckBox _useSystemIconsChk = null!;
    private CheckBox _resolveUniqueChk = null!;
    private CheckBox _showThumbnailsChk = null!;
    private CheckBox _useBuiltInImageViewerChk = null!;
    private CheckBox _showCommonChk = null!;
    private CheckBox _showDesktopChk = null!;
    private CheckBox _showDocumentsChk = null!;
    private CheckBox _showDownloadsChk = null!;
    private CheckBox _showPicturesChk = null!;
    private CheckBox _showRecentChk = null!;
    private CheckBox _showSidebarVScrollChk = null!;
    private CheckBox _runAtStartupChk = null!;
    private CheckBox _startMinimizedChk = null!;
    private CheckBox _showTrayIconChk = null!;
    private CheckBox _enableShellContextMenuChk = null!;
    private Button _manageIntegrationsBtn = null!;
    private CheckBox _useWindowsContextMenuChk = null!;
    private CheckBox _debugNavLogChk = null!;
    private CheckBox _debugNavGcChk = null!;
    private CheckBox _debugNavUiQueueChk = null!;
    private CheckBox _debugNavPostBindChk = null!;
    private CheckBox _permanentDeleteDefaultChk = null!;
    private FlowLayoutPanel _generalPanel = null!;
    private FlowLayoutPanel _aboutPanel = null!;
    private ComboBox _languageCombo = null!;
    private CheckBox _middleClickNewTabChk = null!;
    private Button _defaultFileManagerApplyBtn = null!;
    private Button _defaultFileManagerRestoreBtn = null!;
    private Label _defaultFileManagerStatusLabel = null!;
    private Button _viewLicenseBtn = null!;
    
    private TabControl _tabControl = null!;
    private Button _saveBtn = null!;
    private Button _cancelBtn = null!;
    private FlowLayoutPanel _hotkeyPanel = null!;

    // LLM Settings
    private CheckBox _llmEnabledChk = null!;
    private TextBox _llmApiUrlBox = null!;
    private CheckBox _llmChatEnabledChk = null!; // New
    private TextBox _llmChatApiUrlBox = null!;   // New
    private ComboBox _llmModelComboBox = null!;
    private ComboBox _llmBatchVisionModelComboBox = null!;
    private NumericUpDown _llmMaxTokensNum = null!;
    private NumericUpDown _llmTempNum = null!;

    private Dictionary<string, Button> _hotkeyButtons = new();
    private Dictionary<string, string> _hotkeyEdits = new();
    private string? _recordingAction = null;

    private int Scale(int pixels) => (int)(pixels * (this.DeviceDpi / 96.0));
    private Padding Scale(Padding p) => new Padding(Scale(p.Left), Scale(p.Top), Scale(p.Right), Scale(p.Bottom));

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED may interfere with TabControl headers
            return cp;
        }
    }

    public SettingsForm()
    {
        Text = Localization.T("settings_title");
        MinimumSize = new Size(Scale(500), Scale(400));
        var s = AppSettings.Current;
        Size = new Size(s.SettingsWidth, s.SettingsHeight);
        StartPosition = FormStartPosition.CenterParent;

        this.ResizeEnd += (s, e) => {
            AppSettings.Current.SettingsWidth = this.Width;
            AppSettings.Current.SettingsHeight = this.Height;
            AppSettings.Current.Save();
        };
        FormBorderStyle = FormBorderStyle.None; 
        BackColor = Color.FromArgb(45, 45, 48);
        ForeColor = Color.FromArgb(240, 240, 240);
        KeyPreview = true; 
        DoubleBuffered = true;
        Padding = Scale(new Padding(1)); 

        InitializeComponents();
        LoadSettings();

        Shown += (s, e) =>
        {
            _tabControl?.Invalidate();
            _tabControl?.Update();
        };
        
        _hotkeyPanel.HandleCreated += (s, e) => {
            SetWindowTheme(_hotkeyPanel.Handle, "DarkMode_Explorer", null);
            int darkMode = 1;
            DwmSetWindowAttribute(_hotkeyPanel.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        };
    }

    private void InitializeComponents()
    {
        // Custom Title Bar
        var titleBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = Scale(40),
            BackColor = Color.FromArgb(32, 32, 32)
        };
        titleBar.MouseDown += TitleBar_MouseDown;

        var titleLabel = new Label
        {
            Text = Localization.T("settings_title"),
            Font = new Font("Segoe UI Semibold", 10),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(Scale(15), Scale(10)),
            BackColor = Color.Transparent
        };
        titleLabel.MouseDown += TitleBar_MouseDown;
        
        var closeBtn = new Button
        {
            Text = "✕",
            Size = new Size(Scale(46), Scale(40)),
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            BackColor = Color.Transparent
        };
        closeBtn.FlatAppearance.BorderSize = 0;
        closeBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 17, 35);
        closeBtn.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

        titleBar.Controls.Add(titleLabel);
        titleBar.Controls.Add(closeBtn);

        // Footer
        var footer = new Panel
        {
            Dock = DockStyle.Fill,
            Height = Scale(75),
            BackColor = Color.FromArgb(32, 32, 32),
            Padding = Scale(new Padding(10)),
            Margin = Scale(new Padding(0))
        };

        _cancelBtn = new Button
        {
            Text = Localization.T("cancel"),
            Size = new Size(Scale(120), Scale(38)),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(70, 70, 70),
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        _cancelBtn.FlatAppearance.BorderSize = 0;
        _cancelBtn.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

        _saveBtn = new Button
        {
            Text = Localization.T("save_changes"),
            Size = new Size(Scale(140), Scale(38)),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 120, 212),
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        _saveBtn.FlatAppearance.BorderSize = 0;
        _saveBtn.Click += SaveSettings;

        footer.Resize += (s, e) => 
        {
            int spacing = Scale(20);
            int totalWidth = _cancelBtn.Width + spacing + _saveBtn.Width;
            int startX = (footer.Width - totalWidth) / 2;
            int startY = (footer.Height - _cancelBtn.Height) / 2;
            _cancelBtn.Location = new Point(startX, startY);
            _saveBtn.Location = new Point(startX + _cancelBtn.Width + spacing, startY);
        };

        footer.Controls.Add(_cancelBtn);
        footer.Controls.Add(_saveBtn);

        // Content Area
        var content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(45, 45, 48),
            Padding = Scale(new Padding(0, 5, 0, 0)), // Subtle top padding for tabs
            Margin = Scale(new Padding(0))
        };

        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            SizeMode = TabSizeMode.Normal,
            Multiline = true,
            Padding = new Point(Scale(16), Scale(6)),
            DrawMode = TabDrawMode.OwnerDrawFixed,
            Appearance = TabAppearance.Normal,
            BackColor = Color.FromArgb(45, 45, 48)
        };
        _tabControl.DrawItem += TabControl_DrawItem;
        _tabControl.Paint += (s, e) =>
        {
            e.Graphics.Clear(Color.FromArgb(45, 45, 48));
            using var brush = new SolidBrush(Color.FromArgb(45, 45, 48));
            e.Graphics.FillRectangle(brush, _tabControl.DisplayRectangle);
        };
        _tabControl.HandleCreated += (s, e) =>
        {
            SetWindowTheme(_tabControl.Handle, "DarkMode_Explorer", null);
            int darkMode = 1;
            DwmSetWindowAttribute(_tabControl.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        };

        var generalTab = new TabPage(Localization.T("general_tab")) { BackColor = Color.FromArgb(45, 45, 48), Padding = Scale(new Padding(15)) };
        var hotkeysTab = new TabPage(Localization.T("hotkeys_tab")) { BackColor = Color.FromArgb(45, 45, 48), Padding = Scale(new Padding(10)) };
        var aboutTab = new TabPage(Localization.T("about_tab")) { BackColor = Color.FromArgb(45, 45, 48), Padding = Scale(new Padding(15)) };
        
        InitializeGeneralTab(generalTab);
        InitializeHotkeysTab(hotkeysTab);
        InitializeAboutTab(aboutTab);

        _tabControl.TabPages.Add(generalTab);
        _tabControl.TabPages.Add(hotkeysTab);
        _tabControl.TabPages.Add(aboutTab);
        content.Controls.Add(_tabControl);

        // Layout using TableLayoutPanel to prevent any overlap
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            BackColor = Color.FromArgb(32, 32, 32),
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(40)));  // Title Bar
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Content
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(75)));  // Footer
        
        mainLayout.Controls.Add(titleBar, 0, 0);
        mainLayout.Controls.Add(content, 0, 1);
        mainLayout.Controls.Add(footer, 0, 2);

        Controls.Add(mainLayout);
    }

    private void TitleBar_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }
    }

    private void InitializeGeneralTab(TabPage tab)
    {
        var panel = new FlowLayoutPanel 
        { 
            Dock = DockStyle.Fill, 
            FlowDirection = FlowDirection.TopDown, 
            Padding = Scale(new Padding(20)), 
            BackColor = Color.Transparent,
            AutoScroll = true,
            WrapContents = false
        };
        tab.Controls.Add(panel);
        _generalPanel = panel;
        panel.HandleCreated += (s, e) =>
        {
            SetWindowTheme(panel.Handle, "DarkMode_Explorer", null);
            int darkMode = 1;
            DwmSetWindowAttribute(panel.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        };

        // Font + Icon size row
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
        sizeRow.Controls.Add(new Panel { Width = Scale(24), Height = 1, BackColor = tab.BackColor });
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

        panel.Controls.Add(new Panel { Height = Scale(30), Width = Scale(1), BackColor = tab.BackColor }); 

        _showIconsChk = CreateCheckBox(Localization.T("show_icons"), Point.Empty);
        _showIconsChk.CheckedChanged += (s, e) => UpdateToggles();
        panel.Controls.Add(_showIconsChk);

        _useEmojiChk = CreateCheckBox(Localization.T("use_emoji"), Point.Empty);
        _useEmojiChk.Padding = Scale(new Padding(20, 0, 0, 0));
        _useEmojiChk.CheckedChanged += (s, e) => UpdateToggles();
        panel.Controls.Add(_useEmojiChk);

        _useSystemIconsChk = CreateCheckBox(Localization.T("use_colored_icons"), Point.Empty);
        _useSystemIconsChk.Padding = Scale(new Padding(20, 0, 0, 0));
        panel.Controls.Add(_useSystemIconsChk);

        _resolveUniqueChk = CreateCheckBox(Localization.T("resolve_unique"), Point.Empty);
        _resolveUniqueChk.Padding = Scale(new Padding(20, 0, 0, 0));
        panel.Controls.Add(_resolveUniqueChk);

        _showThumbnailsChk = CreateCheckBox(Localization.T("show_thumbnails"), Point.Empty);
        _showThumbnailsChk.Padding = Scale(new Padding(20, 0, 0, 0));
        panel.Controls.Add(_showThumbnailsChk);

        panel.Controls.Add(new Panel { Height = Scale(10), Width = Scale(1), BackColor = tab.BackColor });
        var viewerRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(Scale(20), 0, 0, Scale(6))
        };
        _useBuiltInImageViewerChk = CreateCheckBox(Localization.T("use_builtin_image_viewer"), Point.Empty);
        viewerRow.Controls.Add(_useBuiltInImageViewerChk);
        panel.Controls.Add(viewerRow);

        _showCommonChk = CreateCheckBox(Localization.T("show_windows_folders"), Point.Empty);
        _showCommonChk.Font = new Font("Segoe UI Semibold", 9);
        _showCommonChk.CheckedChanged += (s, e) => UpdateToggles();
        panel.Controls.Add(_showCommonChk);

        _showDesktopChk = CreateCheckBox(Localization.T("desktop"), Point.Empty);
        _showDesktopChk.Padding = Scale(new Padding(20, 0, 0, 0));
        panel.Controls.Add(_showDesktopChk);

        _showDocumentsChk = CreateCheckBox(Localization.T("documents"), Point.Empty);
        _showDocumentsChk.Padding = Scale(new Padding(20, 0, 0, 0));
        panel.Controls.Add(_showDocumentsChk);

        _showDownloadsChk = CreateCheckBox(Localization.T("downloads"), Point.Empty);
        _showDownloadsChk.Padding = Scale(new Padding(20, 0, 0, 0));
        panel.Controls.Add(_showDownloadsChk);

        _showPicturesChk = CreateCheckBox(Localization.T("pictures"), Point.Empty);
        _showPicturesChk.Padding = Scale(new Padding(20, 0, 0, 0));
        panel.Controls.Add(_showPicturesChk);

        panel.Controls.Add(new Panel { Height = Scale(8), Width = Scale(1), BackColor = tab.BackColor });

        _showRecentChk = CreateCheckBox(Localization.T("recent_folders"), Point.Empty);
        _showRecentChk.Padding = Scale(new Padding(0, 0, 0, 0));
        panel.Controls.Add(_showRecentChk);

        _showSidebarVScrollChk = CreateCheckBox(Localization.T("show_sidebar_scrollbar"), Point.Empty);
        _showSidebarVScrollChk.Padding = Scale(new Padding(0, 0, 0, 0));
        panel.Controls.Add(_showSidebarVScrollChk);

        panel.Controls.Add(new Panel { Height = Scale(20), Width = Scale(1), BackColor = tab.BackColor });
        var startupHeader = CreateLabel(Localization.T("startup_bg"), Point.Empty);
        ApplySectionHeaderStyle(startupHeader);
        panel.Controls.Add(startupHeader);

        _runAtStartupChk = CreateCheckBox(Localization.T("run_startup"), Point.Empty);
        panel.Controls.Add(_runAtStartupChk);

        _startMinimizedChk = CreateCheckBox(Localization.T("start_minimized"), Point.Empty);
        panel.Controls.Add(_startMinimizedChk);

        _showTrayIconChk = CreateCheckBox(Localization.T("show_tray_icon"), Point.Empty);
        panel.Controls.Add(_showTrayIconChk);

        _enableShellContextMenuChk = CreateCheckBox(Localization.T("enable_shell_menu"), Point.Empty);
        panel.Controls.Add(_enableShellContextMenuChk);

        _useWindowsContextMenuChk = CreateCheckBox(Localization.T("use_windows_menu"), Point.Empty);
        panel.Controls.Add(_useWindowsContextMenuChk);

        _manageIntegrationsBtn = new Button
        {
            Text = Localization.T("manage_integrations"),
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(70, 70, 70),
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Margin = Scale(new Padding(0, 8, 0, 0))
        };
        _manageIntegrationsBtn.FlatAppearance.BorderSize = 0;
        _manageIntegrationsBtn.Click += (s, e) =>
        {
            using var dlg = new ManualContextActionsForm();
            dlg.ShowDialog(this);
        };
        panel.Controls.Add(_manageIntegrationsBtn);

        panel.Controls.Add(new Panel { Height = Scale(20), Width = Scale(1), BackColor = tab.BackColor });
        var tabsHeader = CreateLabel(Localization.T("tabs_windows"), Point.Empty);
        ApplySectionHeaderStyle(tabsHeader);
        panel.Controls.Add(tabsHeader);

        _middleClickNewTabChk = CreateCheckBox(Localization.T("middle_click_new_tab"), Point.Empty);
        panel.Controls.Add(_middleClickNewTabChk);

        panel.Controls.Add(new Panel { Height = Scale(20), Width = Scale(1), BackColor = tab.BackColor });
        var safetyHeader = CreateLabel(Localization.T("safety_section"), Point.Empty);
        ApplySectionHeaderStyle(safetyHeader);
        panel.Controls.Add(safetyHeader);

        _permanentDeleteDefaultChk = CreateCheckBox(Localization.T("permanent_delete_default"), Point.Empty);
        panel.Controls.Add(_permanentDeleteDefaultChk);

        panel.Controls.Add(new Panel { Height = Scale(20), Width = Scale(1), BackColor = tab.BackColor });

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

        panel.Controls.Add(new Panel { Height = Scale(30), Width = Scale(1), BackColor = tab.BackColor });

        var winHeader = CreateLabel(Localization.T("windows_integration"), Point.Empty);
        ApplySectionHeaderStyle(winHeader);
        panel.Controls.Add(winHeader);

        panel.Controls.Add(CreateLabel(Localization.T("default_app_label"), Point.Empty));

        _defaultFileManagerStatusLabel = CreateLabel(Localization.T("status_defaults"), Point.Empty);
        _defaultFileManagerStatusLabel.ForeColor = Color.Gray;
        panel.Controls.Add(_defaultFileManagerStatusLabel);

        _defaultFileManagerApplyBtn = new Button
        {
            Text = Localization.T("apply_default"),
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(70, 70, 70),
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Margin = Scale(new Padding(0, 8, 0, 0))
        };
        _defaultFileManagerApplyBtn.FlatAppearance.BorderSize = 0;
        _defaultFileManagerApplyBtn.Click += (s, e) => ApplyDefaultFileManagerSettings(enable: true);
        panel.Controls.Add(_defaultFileManagerApplyBtn);

        _defaultFileManagerRestoreBtn = new Button
        {
            Text = Localization.T("restore_default"),
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(70, 70, 70),
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Margin = Scale(new Padding(0, 8, 0, 0))
        };
        _defaultFileManagerRestoreBtn.FlatAppearance.BorderSize = 0;
        _defaultFileManagerRestoreBtn.Click += (s, e) => ApplyDefaultFileManagerSettings(enable: false);
        panel.Controls.Add(_defaultFileManagerRestoreBtn);

        panel.Controls.Add(new Panel { Height = Scale(30), Width = Scale(1), BackColor = tab.BackColor });

        // LLM Section
        var llmHeader = CreateLabel(Localization.T("llm_section"), Point.Empty);
        ApplySectionHeaderStyle(llmHeader);
        panel.Controls.Add(llmHeader);

        _llmEnabledChk = CreateCheckBox(Localization.T("llm_enable"), Point.Empty);
        panel.Controls.Add(_llmEnabledChk);

        panel.Controls.Add(CreateLabel(Localization.T("api_url"), Point.Empty));
        _llmApiUrlBox = new TextBox
        {
            Width = Scale(300),
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.LightGray,
            BorderStyle = BorderStyle.FixedSingle
        };
        panel.Controls.Add(_llmApiUrlBox);
        panel.Controls.Add(new Panel { Height = Scale(10), Width = Scale(1), BackColor = tab.BackColor });

        // Chat Mode Settings
        _llmChatEnabledChk = new CheckBox 
        { 
            Text = Localization.T("chat_enable"), 
            AutoSize = true, 
            ForeColor = Color.White,
            Margin = Scale(new Padding(0, 10, 0, 5))
        };
        panel.Controls.Add(_llmChatEnabledChk);

        panel.Controls.Add(CreateLabel(Localization.T("chat_api_url"), Point.Empty));
        _llmChatApiUrlBox = new TextBox 
        { 
            Width = Scale(300), 
            BackColor = Color.FromArgb(50, 50, 50), 
            ForeColor = Color.LightGray, 
            BorderStyle = BorderStyle.FixedSingle 
        };
        panel.Controls.Add(_llmChatApiUrlBox);
        panel.Controls.Add(new Panel { Height = Scale(10), Width = Scale(1), BackColor = tab.BackColor });

        // Model Selection
        var modelPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        modelPanel.Controls.Add(CreateLabel(Localization.T("model_name"), new Point(0, Scale(5))));
        
        _llmModelComboBox = new ComboBox
        {
            Width = Scale(200),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DropDownStyle = ComboBoxStyle.DropDown // Allow typing too
        };
        modelPanel.Controls.Add(_llmModelComboBox);

        var fetchBtn = new Button
        {
            Text = Localization.T("fetch_models"),
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 120, 212),
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        fetchBtn.FlatAppearance.BorderSize = 0;
        fetchBtn.Click += FetchModels_Click;
        modelPanel.Controls.Add(fetchBtn);
        
        panel.Controls.Add(modelPanel);

        // Separate default model for batch image/vision work
        var batchVisionModelPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        batchVisionModelPanel.Controls.Add(CreateLabel(Localization.T("batch_vision_model_name"), new Point(0, Scale(5))));

        _llmBatchVisionModelComboBox = new ComboBox
        {
            Width = Scale(200),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DropDownStyle = ComboBoxStyle.DropDown
        };
        batchVisionModelPanel.Controls.Add(_llmBatchVisionModelComboBox);
        panel.Controls.Add(batchVisionModelPanel);

        // Max Tokens
        var tokenPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        tokenPanel.Controls.Add(CreateLabel(Localization.T("max_tokens"), new Point(0, Scale(5))));
        _llmMaxTokensNum = CreateNumeric(100, 32000, Point.Empty);
        _llmMaxTokensNum.Width = Scale(100);
        tokenPanel.Controls.Add(_llmMaxTokensNum);
        panel.Controls.Add(tokenPanel);

        // Temperature
        var tempPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        tempPanel.Controls.Add(CreateLabel(Localization.T("temperature"), new Point(0, Scale(5))));
        _llmTempNum = new NumericUpDown
        {
            Minimum = 0, Maximum = 2, DecimalPlaces = 2, Increment = 0.1M,
            Location = Point.Empty, Size = new Size(Scale(80), Scale(25)),
            BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White
        };
        tempPanel.Controls.Add(_llmTempNum);
        panel.Controls.Add(tempPanel);
        panel.Controls.Add(new Panel { Height = Scale(16), Width = Scale(1), BackColor = tab.BackColor });
    }

    private void InitializeHotkeysTab(TabPage tab)
    {
        _hotkeyPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = Scale(new Padding(20)),
            BackColor = Color.Transparent,
            AutoScroll = true,
            WrapContents = false
        };
        tab.Controls.Add(_hotkeyPanel);
    }

    private void InitializeAboutTab(TabPage tab)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = Scale(new Padding(20)),
            BackColor = Color.Transparent
        };
        tab.Controls.Add(panel);
        _aboutPanel = panel;
        panel.HandleCreated += (s, e) =>
        {
            SetWindowTheme(panel.Handle, "DarkMode_Explorer", null);
            int darkMode = 1;
            DwmSetWindowAttribute(panel.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        };

        var title = CreateLabel(Localization.T("about_title"), Point.Empty);
        title.Font = new Font("Segoe UI Semibold", 10);
        panel.Controls.Add(title);

        panel.Controls.Add(new Panel { Height = Scale(10), Width = Scale(1), BackColor = tab.BackColor });

        panel.Controls.Add(CreateLabel(Localization.T("antigravity"), Point.Empty));
        panel.Controls.Add(CreateLabel("Claude Opus 4.5", Point.Empty));
        panel.Controls.Add(CreateLabel("Gemini 3.0 Pro", Point.Empty));
        panel.Controls.Add(CreateLabel("Gemini 3.0 Flash", Point.Empty));

        panel.Controls.Add(new Panel { Height = Scale(14), Width = Scale(1), BackColor = tab.BackColor });

        panel.Controls.Add(CreateLabel(Localization.T("codex"), Point.Empty));
        panel.Controls.Add(CreateLabel("GPT-5.3-Codex", Point.Empty));
        panel.Controls.Add(CreateLabel("GPT-5.2-Codex", Point.Empty));
        panel.Controls.Add(new Panel { Height = Scale(14), Width = Scale(1), BackColor = tab.BackColor });

        panel.Controls.Add(CreateLabel(Localization.T("directed_by"), Point.Empty));
        panel.Controls.Add(new Panel { Height = Scale(16), Width = Scale(1), BackColor = tab.BackColor });

        _viewLicenseBtn = new Button
        {
            Text = Localization.T("view_license"),
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(70, 70, 70),
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Margin = Scale(new Padding(0, 6, 0, 0))
        };
        _viewLicenseBtn.FlatAppearance.BorderSize = 0;
        _viewLicenseBtn.Click += (s, e) =>
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
        };
        panel.Controls.Add(_viewLicenseBtn);
    }

    private void PopulateHotkeys()
    {
        _hotkeyPanel.Controls.Clear();
        _hotkeyButtons.Clear();

        var defaultBtn = new Button
        {
            Text = Localization.T("hotkeys_revert"),
            Width = Scale(440),
            Height = Scale(35),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Margin = Scale(new Padding(5, 5, 5, 15))
        };
        defaultBtn.FlatAppearance.BorderSize = 0;
        defaultBtn.Click += RevertToDefaults;
        _hotkeyPanel.Controls.Add(defaultBtn);

        foreach (var kvp in _hotkeyEdits)
        {
            var row = new Panel { Width = Scale(440), Height = Scale(45), BackColor = Color.Transparent, Margin = Scale(new Padding(0, 0, 0, 5)) };
            var lbl = new Label { 
                Text = GetActionName(kvp.Key), 
                AutoSize = false, 
                Width = Scale(220), 
                Height = Scale(32), 
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(Scale(5), Scale(6)),
                ForeColor = Color.White
            };
            var btn = new Button
            {
                Text = FormatHotkey(kvp.Value),
                Location = new Point(Scale(230), Scale(6)),
                Size = new Size(Scale(180), Scale(32)),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                Tag = kvp.Key,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += HotkeyBtn_Click;
            
            row.Controls.Add(lbl);
            row.Controls.Add(btn);
            _hotkeyPanel.Controls.Add(row);
            
            _hotkeyButtons[kvp.Key] = btn;
        }

        _hotkeyPanel.Controls.Add(new Panel { Height = Scale(12), Width = Scale(1), BackColor = Color.FromArgb(45, 45, 48) });
    }

    private string FormatHotkey(string keys) => (keys ?? "None").Replace(", ", "+");

    private void RevertToDefaults(object? sender, EventArgs e)
    {
        var temp = new AppSettings(); 
        foreach (var kvp in temp.Hotkeys)
        {
            _hotkeyEdits[kvp.Key] = kvp.Value;
            if (_hotkeyButtons.TryGetValue(kvp.Key, out var btn))
                btn.Text = FormatHotkey(kvp.Value);
        }
    }

    private string GetActionName(string action) => action switch
    {
        "NavBack" => Localization.T("hotkey_nav_back"),
        "NavForward" => Localization.T("hotkey_nav_forward"),
        "FocusAddress" => Localization.T("hotkey_focus_address"),
        "FocusSearch" => Localization.T("hotkey_focus_search"),
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
        _ => action
    };

    private void TabControl_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var tab = _tabControl.TabPages[e.Index];
        var rect = _tabControl.GetTabRect(e.Index);
        
        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        
        // Background for the tab item
        using var brush = new SolidBrush(selected ? Color.FromArgb(60, 60, 60) : Color.FromArgb(40, 40, 40));
        e.Graphics.FillRectangle(brush, rect);
        
        // Fix white bleed: fill the rest of the tab header area
        if (e.Index == _tabControl.TabCount - 1)
        {
            var headerRect = new Rectangle(rect.Right - 1, rect.Top, _tabControl.Width - rect.Right + 1, rect.Height);
            using var headerBrush = new SolidBrush(Color.FromArgb(32, 32, 32));
            e.Graphics.FillRectangle(headerBrush, headerRect);
        }

        if (selected)
        {
            using var pen = new Pen(Color.FromArgb(0, 120, 212), 3);
            e.Graphics.DrawLine(pen, rect.Left, rect.Bottom - 1, rect.Right, rect.Bottom - 1);
        }

        var textColor = selected ? Color.White : Color.Gray;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter;
        TextRenderer.DrawText(e.Graphics, tab.Text, Font, rect, textColor, flags);
    }


    private void HotkeyBtn_Click(object? sender, EventArgs e)
    {
        if (sender is Button btn)
        {
            // Reset previous recording if any
            if (_recordingAction != null && _hotkeyButtons.TryGetValue(_recordingAction, out var oldBtn))
            {
                oldBtn.Text = FormatHotkey(_hotkeyEdits[_recordingAction]);
                oldBtn.BackColor = Color.FromArgb(60, 60, 60);
            }

            _recordingAction = btn.Tag as string;
            if (string.IsNullOrEmpty(_recordingAction))
            {
                _recordingAction = null;
                return;
            }
            btn.Text = "Recording...";
            btn.BackColor = Color.FromArgb(0, 120, 212);
            btn.Focus();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var action = _recordingAction;
        if (action != null)
        {
            if (e.KeyCode == Keys.Escape)
            {
                var btn = _hotkeyButtons[action];
                btn.Text = FormatHotkey(_hotkeyEdits[action]);
                btn.BackColor = Color.FromArgb(60, 60, 60);
                _recordingAction = null;
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
                if (kvp.Value == keyString && kvp.Key != action)
                {
                    _hotkeyEdits[kvp.Key] = "None";
                    if (_hotkeyButtons.TryGetValue(kvp.Key, out var otherBtn))
                        otherBtn.Text = "None";
                }
            }

            _hotkeyEdits[action] = keyString;
            var targetBtn = _hotkeyButtons[action];
            targetBtn.Text = FormatHotkey(keyString);
            targetBtn.BackColor = Color.FromArgb(60, 60, 60);
            _recordingAction = null;
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else
        {
            base.OnKeyDown(e);
        }
    }

    private void LoadSettings()
    {
        AppSettings.ReloadCurrent();
        var s = AppSettings.Current;
        _fontSizeNum.Value = Math.Clamp(s.FontSize, 8, 24);
        _iconSizeNum.Value = Math.Clamp(s.IconSize, 16, 192);
        _showIconsChk.Checked = s.ShowIcons;
        _useEmojiChk.Checked = s.UseEmojiIcons;
        _useSystemIconsChk.Checked = s.UseSystemIcons;
        _resolveUniqueChk.Checked = s.ResolveUniqueIcons;
        _showThumbnailsChk.Checked = s.ShowThumbnails;
        _useBuiltInImageViewerChk.Checked = s.UseBuiltInImageViewer;
        _showCommonChk.Checked = s.ShowSidebarCommon;
        _showDesktopChk.Checked = s.ShowSidebarDesktop;
        _showDocumentsChk.Checked = s.ShowSidebarDocuments;
        _showDownloadsChk.Checked = s.ShowSidebarDownloads;
        _showPicturesChk.Checked = s.ShowSidebarPictures;
        _showRecentChk.Checked = s.ShowSidebarRecent;
        _showSidebarVScrollChk.Checked = s.ShowSidebarVerticalScrollbar;
        if (_languageCombo != null)
            _languageCombo.SelectedIndex = string.Equals(s.UiLanguage, "ru", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _runAtStartupChk.Checked = s.RunAtStartup;
        _startMinimizedChk.Checked = s.StartMinimized;
        _showTrayIconChk.Checked = s.ShowTrayIcon;
        _enableShellContextMenuChk.Checked = s.EnableShellContextMenu;
        _useWindowsContextMenuChk.Checked = s.UseWindowsContextMenu;
        _middleClickNewTabChk.Checked = s.MiddleClickOpensNewTab;
        _permanentDeleteDefaultChk.Checked = s.PermanentDeleteByDefault;
        _debugNavLogChk.Checked = s.DebugNavigationLogging;
        _debugNavGcChk.Checked = s.DebugNavigationGcStats;
        _debugNavUiQueueChk.Checked = s.DebugNavigationUiQueue;
        _debugNavPostBindChk.Checked = s.DebugNavigationPostBind;
        UpdateDefaultFileManagerStatusLabel();
        
        _hotkeyEdits.Clear();
        foreach (var kvp in s.Hotkeys) _hotkeyEdits[kvp.Key] = kvp.Value;

        // LLM settings
        _llmEnabledChk.Checked = s.LlmEnabled;
        _llmApiUrlBox.Text = s.LlmApiUrl;
        _llmChatEnabledChk.Checked = s.ChatModeEnabled;
        _llmChatApiUrlBox.Text = s.LlmChatApiUrl;
        _llmModelComboBox.Text = s.LlmModelName;
        _llmBatchVisionModelComboBox.Text = s.LlmBatchVisionModelName;
        _llmMaxTokensNum.Value = Math.Clamp(s.LlmMaxTokens, 100, 32000);
        _llmTempNum.Value = (decimal)Math.Clamp(s.LlmTemperature, 0, 2.0);

        PopulateHotkeys();
        UpdateToggles();
    }

    private void UpdateDefaultFileManagerStatusLabel()
    {
        var status = FileManagerIntegrationService.GetCurrentStatus();
        switch (status)
        {
            case FileManagerIntegrationService.IntegrationStatus.AppliedHkcuHklm:
                _defaultFileManagerStatusLabel.Text = Localization.T("status_applied");
                _defaultFileManagerStatusLabel.ForeColor = Color.LightGreen;
                break;
            case FileManagerIntegrationService.IntegrationStatus.AppliedHkcu:
                _defaultFileManagerStatusLabel.Text = Localization.T("status_applied_hkcu");
                _defaultFileManagerStatusLabel.ForeColor = Color.LightGreen;
                break;
            case FileManagerIntegrationService.IntegrationStatus.AppliedHklm:
                _defaultFileManagerStatusLabel.Text = Localization.T("status_applied_hklm");
                _defaultFileManagerStatusLabel.ForeColor = Color.LightGreen;
                break;
            default:
                _defaultFileManagerStatusLabel.Text = Localization.T("status_defaults");
                _defaultFileManagerStatusLabel.ForeColor = Color.Gray;
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

    private void UpdateToggles()
    {
        bool show = _showIconsChk.Checked;
        bool emoji = _useEmojiChk.Checked;
        _useEmojiChk.Enabled = show;
        _useSystemIconsChk.Enabled = show && !emoji;
        _resolveUniqueChk.Enabled = show && !emoji;
        
        _useEmojiChk.ForeColor = show ? Color.FromArgb(240, 240, 240) : Color.Gray;
        _useSystemIconsChk.ForeColor = (show && !emoji) ? Color.FromArgb(240, 240, 240) : Color.Gray;
        _resolveUniqueChk.ForeColor = (show && !emoji) ? Color.FromArgb(240, 240, 240) : Color.Gray;
        _showThumbnailsChk.Enabled = show && !emoji;
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
        _showRecentChk.Enabled = true;
        _showRecentChk.ForeColor = Color.FromArgb(240, 240, 240);
        _showSidebarVScrollChk.Enabled = true;
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

    private async void FetchModels_Click(object? sender, EventArgs e)
    {
        var btn = (Button)sender!;
        btn.Enabled = false;
        btn.Text = "Fetching...";
        try
        {
            var service = new LlmService { ApiUrl = _llmApiUrlBox.Text.Trim() };
            var catalog = await service.GetModelCatalogAsync(_llmApiUrlBox.Text.Trim());
            var models = catalog.AvailableModels.Select(m => m.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var visionModels = catalog.AvailableModels
                .Where(m => m.IsVision)
                .Select(m => m.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string currentAssistant = _llmModelComboBox.Text.Trim();
            string currentBatchVision = _llmBatchVisionModelComboBox.Text.Trim();

            _llmModelComboBox.Items.Clear();
            _llmBatchVisionModelComboBox.Items.Clear();

            if (models.Count > 0)
            {
                _llmModelComboBox.Items.AddRange(models.ToArray());
                if (!string.IsNullOrWhiteSpace(currentAssistant) &&
                    !models.Contains(currentAssistant, StringComparer.OrdinalIgnoreCase))
                {
                    _llmModelComboBox.Items.Add(currentAssistant);
                }

                _llmModelComboBox.Text = string.IsNullOrWhiteSpace(currentAssistant) ? models[0] : currentAssistant;
                _llmModelComboBox.DroppedDown = true;

                if (visionModels.Count > 0)
                {
                    _llmBatchVisionModelComboBox.Items.AddRange(visionModels.ToArray());
                    if (!string.IsNullOrWhiteSpace(currentBatchVision) &&
                        !visionModels.Contains(currentBatchVision, StringComparer.OrdinalIgnoreCase))
                    {
                        _llmBatchVisionModelComboBox.Items.Add(currentBatchVision);
                    }

                    _llmBatchVisionModelComboBox.Text = string.IsNullOrWhiteSpace(currentBatchVision) ? visionModels[0] : currentBatchVision;
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(currentBatchVision))
                    {
                        _llmBatchVisionModelComboBox.Items.Add(currentBatchVision);
                        _llmBatchVisionModelComboBox.Text = currentBatchVision;
                    }
                    MessageBox.Show("No vision-capable models were detected. Load a vision model in LM Studio and fetch again.", "Fetch Models", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            else
            {
                MessageBox.Show("No models found.", "Fetch Models", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to fetch models: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btn.Text = Localization.T("fetch_models");
            btn.Enabled = true;
        }
    }

    private void SaveSettings(object? sender, EventArgs e)
    {
        var s = AppSettings.Current;
        s.FontSize = (int)_fontSizeNum.Value;
        s.IconSize = (int)_iconSizeNum.Value;
        s.ShowIcons = _showIconsChk.Checked;
        s.UseEmojiIcons = _useEmojiChk.Checked;
        s.UseSystemIcons = _useSystemIconsChk.Checked;
        s.ResolveUniqueIcons = _resolveUniqueChk.Checked;
        s.ShowThumbnails = _showThumbnailsChk.Checked;
        s.UseBuiltInImageViewer = _useBuiltInImageViewerChk.Checked;
        s.ShowSidebarCommon = _showCommonChk.Checked;
        s.ShowSidebarDesktop = _showDesktopChk.Checked;
        s.ShowSidebarDocuments = _showDocumentsChk.Checked;
        s.ShowSidebarDownloads = _showDownloadsChk.Checked;
        s.ShowSidebarPictures = _showPicturesChk.Checked;
        s.ShowSidebarRecent = _showRecentChk.Checked;
        s.ShowSidebarVerticalScrollbar = _showSidebarVScrollChk.Checked;
        if (_languageCombo != null)
            s.UiLanguage = _languageCombo.SelectedIndex == 1 ? "ru" : "en";

        // LLM settings
        s.LlmEnabled = _llmEnabledChk.Checked;
        s.LlmApiUrl = _llmApiUrlBox.Text.Trim();
        s.ChatModeEnabled = _llmChatEnabledChk.Checked;
        s.LlmChatApiUrl = _llmChatApiUrlBox.Text.Trim();
        s.LlmModelName = _llmModelComboBox.Text.Trim();
        s.LlmBatchVisionModelName = _llmBatchVisionModelComboBox.Text.Trim();
        s.LlmMaxTokens = (int)_llmMaxTokensNum.Value;
        s.LlmTemperature = (double)_llmTempNum.Value;

        s.RunAtStartup = _runAtStartupChk.Checked;
        s.StartMinimized = _startMinimizedChk.Checked;
        s.ShowTrayIcon = _showTrayIconChk.Checked;
        s.EnableShellContextMenu = _enableShellContextMenuChk.Checked;
        s.UseWindowsContextMenu = _useWindowsContextMenuChk.Checked;
        s.MiddleClickOpensNewTab = _middleClickNewTabChk.Checked;
        s.PermanentDeleteByDefault = _permanentDeleteDefaultChk.Checked;
        s.DebugNavigationLogging = _debugNavLogChk.Checked;
        s.DebugNavigationGcStats = _debugNavGcChk.Checked;
        s.DebugNavigationUiQueue = _debugNavUiQueueChk.Checked;
        s.DebugNavigationPostBind = _debugNavPostBindChk.Checked;

        foreach (var kvp in _hotkeyEdits) s.Hotkeys[kvp.Key] = kvp.Value;
        s.Save();
        StartupService.SyncWithSettings();
        try { Program.MultiWindowContext.Instance.SetTrayIconVisible(s.ShowTrayIcon); } catch { }
        this.DialogResult = DialogResult.OK;
        Close();
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
        const int HTCLIENT = 1;
        const int HTLEFT = 10;
        const int HTRIGHT = 11;
        const int HTTOP = 12;
        const int HTTOPLEFT = 13;
        const int HTTOPRIGHT = 14;
        const int HTBOTTOM = 15;
        const int HTBOTTOMLEFT = 16;
        const int HTBOTTOMRIGHT = 17;

        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            if ((int)m.Result == HTCLIENT)
            {
                Point screenPoint = new Point(m.LParam.ToInt32());
                Point clientPoint = this.PointToClient(screenPoint);
                int b = 10; 

                if (clientPoint.Y <= b)
                {
                    if (clientPoint.X <= b) m.Result = (IntPtr)HTTOPLEFT;
                    else if (clientPoint.X >= (this.Size.Width - b)) m.Result = (IntPtr)HTTOPRIGHT;
                    else m.Result = (IntPtr)HTTOP;
                }
                else if (clientPoint.Y >= (this.Size.Height - b))
                {
                    if (clientPoint.X <= b) m.Result = (IntPtr)HTBOTTOMLEFT;
                    else if (clientPoint.X >= (this.Size.Width - b)) m.Result = (IntPtr)HTBOTTOMRIGHT;
                    else m.Result = (IntPtr)HTBOTTOM;
                }
                else
                {
                    if (clientPoint.X <= b) m.Result = (IntPtr)HTLEFT;
                    else if (clientPoint.X >= (this.Size.Width - b)) m.Result = (IntPtr)HTRIGHT;
                }
            }
            return;
        }
        base.WndProc(ref m);
    }

    private Label CreateLabel(string text, Point loc) => new Label
    {
        Text = text, Location = loc, AutoSize = true,
        Font = new Font("Segoe UI", 10), ForeColor = Color.FromArgb(240, 240, 240),
        BackColor = Color.Transparent
    };

    private NumericUpDown CreateNumeric(int min, int max, Point loc) => new NumericUpDown
    {
        Minimum = min, Maximum = max, Location = loc, Size = new Size(80, 25),
        BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White
    };

    private CheckBox CreateCheckBox(string text, Point loc) => new CheckBox
    {
        Text = text, Location = loc, AutoSize = true,
        Font = new Font("Segoe UI", 10), ForeColor = Color.FromArgb(240, 240, 240),
        Cursor = Cursors.Hand, BackColor = Color.Transparent
    };
}
