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

    private FlowLayoutPanel _aboutPanel = null!;
    private GeneralSettingsControl _generalSection = null!;
    private AppBehaviorSettingsControl _appBehaviorSection = null!;
    private ManualContextActionsSettingsControl _manualActionsSection = null!;
    private WindowsIntegrationSettingsControl _windowsIntegrationSection = null!;
    private Button _viewLicenseBtn = null!;
    
    private TabControl _tabControl = null!;
    private Button _saveBtn = null!;
    private Button _cancelBtn = null!;
    private FlowLayoutPanel _hotkeyPanel = null!;

    // LLM Settings
    private LlmSettingsSectionControl _llmSettingsSection = null!;

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
            Size = new Size(Scale(120), Scale(38))
        };
        SettingsButtonStyle.ApplyNeutral(_cancelBtn);
        _cancelBtn.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

        _saveBtn = new Button
        {
            Text = Localization.T("save_changes"),
            Size = new Size(Scale(140), Scale(38))
        };
        SettingsButtonStyle.ApplyPrimary(_saveBtn);
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
        ApplyDarkTheme(_tabControl);

        var generalTab = new TabPage(Localization.T("general_tab")) { BackColor = Color.FromArgb(45, 45, 48), Padding = Scale(new Padding(15)) };
        var appBehaviorTab = new TabPage(Localization.T("app_behavior_tab")) { BackColor = Color.FromArgb(45, 45, 48), Padding = Scale(new Padding(15)) };
        var integrationsTab = new TabPage(Localization.T("manage_actions_title")) { BackColor = Color.FromArgb(45, 45, 48), Padding = Scale(new Padding(15)) };
        var windowsTab = new TabPage(Localization.T("windows_integration")) { BackColor = Color.FromArgb(45, 45, 48), Padding = Scale(new Padding(15)) };
        var llmTab = new TabPage("LLM") { BackColor = Color.FromArgb(45, 45, 48), Padding = Scale(new Padding(15)) };
        var hotkeysTab = new TabPage(Localization.T("hotkeys_tab")) { BackColor = Color.FromArgb(45, 45, 48), Padding = Scale(new Padding(10)) };
        var aboutTab = new TabPage(Localization.T("about_tab")) { BackColor = Color.FromArgb(45, 45, 48), Padding = Scale(new Padding(15)) };
        
        InitializeGeneralTab(generalTab);
        InitializeAppBehaviorTab(appBehaviorTab);
        InitializeIntegrationsTab(integrationsTab);
        InitializeWindowsIntegrationTab(windowsTab);
        InitializeLlmTab(llmTab);
        InitializeHotkeysTab(hotkeysTab);
        InitializeAboutTab(aboutTab);

        _tabControl.TabPages.Add(generalTab);
        _tabControl.TabPages.Add(appBehaviorTab);
        _tabControl.TabPages.Add(integrationsTab);
        _tabControl.TabPages.Add(windowsTab);
        _tabControl.TabPages.Add(llmTab);
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

    private void ApplyDarkTheme(Control control)
    {
        control.HandleCreated += (s, e) =>
        {
            SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
            int darkMode = 1;
            DwmSetWindowAttribute(control.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        };
    }

    private FlowLayoutPanel CreateScrollableFlowTab(TabPage tab, int padding)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = Scale(new Padding(padding)),
            BackColor = Color.Transparent
        };
        tab.Controls.Add(panel);
        ApplyDarkTheme(panel);
        return panel;
    }

    private Panel CreatePaddedPanelTab(TabPage tab, int padding)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = Scale(new Padding(padding))
        };
        tab.Controls.Add(panel);
        ApplyDarkTheme(panel);
        return panel;
    }

    private void InitializeGeneralTab(TabPage tab)
    {
        var panel = CreateScrollableFlowTab(tab, 20);

        _generalSection = new GeneralSettingsControl();
        panel.Controls.Add(_generalSection);
    }

    private void InitializeLlmTab(TabPage tab)
    {
        var panel = CreateScrollableFlowTab(tab, 20);

        _llmSettingsSection = new LlmSettingsSectionControl();
        panel.Controls.Add(_llmSettingsSection);
    }

    private void InitializeAppBehaviorTab(TabPage tab)
    {
        var panel = CreateScrollableFlowTab(tab, 20);

        _appBehaviorSection = new AppBehaviorSettingsControl();
        panel.Controls.Add(_appBehaviorSection);
    }

    private void InitializeIntegrationsTab(TabPage tab)
    {
        var panel = CreatePaddedPanelTab(tab, 8);

        _manualActionsSection = new ManualContextActionsSettingsControl();
        panel.Controls.Add(_manualActionsSection);
    }

    private void InitializeWindowsIntegrationTab(TabPage tab)
    {
        var panel = CreateScrollableFlowTab(tab, 20);

        _windowsIntegrationSection = new WindowsIntegrationSettingsControl();
        _windowsIntegrationSection.DefaultFileManagerRequested += enable => ApplyDefaultFileManagerSettings(enable);
        _windowsIntegrationSection.ImageViewerAssociationRequested += enable => ApplyImageViewerAssociationSettings(enable);
        panel.Controls.Add(_windowsIntegrationSection);
    }

    private void InitializeHotkeysTab(TabPage tab)
    {
        _hotkeyPanel = CreateScrollableFlowTab(tab, 20);
    }

    private void InitializeAboutTab(TabPage tab)
    {
        var panel = CreateScrollableFlowTab(tab, 20);
        _aboutPanel = panel;

        var title = CreateLabel(Localization.T("about_title"), Point.Empty);
        title.Font = new Font("Segoe UI Semibold", 10);
        panel.Controls.Add(title);

        panel.Controls.Add(new Panel { Height = Scale(10), Width = Scale(1), BackColor = tab.BackColor });

        panel.Controls.Add(CreateLabel(Localization.T("antigravity"), Point.Empty));
        panel.Controls.Add(CreateLabel("Claude Opus 4.6", Point.Empty));
        panel.Controls.Add(CreateLabel("Claude Opus 4.5", Point.Empty));
        panel.Controls.Add(CreateLabel("Gemini 3.1 Pro", Point.Empty));
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
            Margin = Scale(new Padding(0, 6, 0, 0))
        };
        SettingsButtonStyle.ApplyNeutral(_viewLicenseBtn);
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
            Margin = Scale(new Padding(5, 5, 5, 15))
        };
        SettingsButtonStyle.ApplySubtle(defaultBtn);
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
                Tag = kvp.Key
            };
            SettingsButtonStyle.ApplySubtle(btn);
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
        "ToggleOcrBoxes" => Localization.T("hotkey_toggle_ocr_boxes"),
        "ToggleSavedTranslation" => Localization.T("hotkey_toggle_saved_translation"),
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
                oldBtn.BackColor = SettingsButtonStyle.SubtleColor;
            }

            _recordingAction = btn.Tag as string;
            if (string.IsNullOrEmpty(_recordingAction))
            {
                _recordingAction = null;
                return;
            }
            btn.Text = "Recording...";
            btn.BackColor = SettingsButtonStyle.PrimaryColor;
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
                btn.BackColor = SettingsButtonStyle.SubtleColor;
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
            targetBtn.BackColor = SettingsButtonStyle.SubtleColor;
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
        _generalSection.LoadFromSettings(s);
        _appBehaviorSection.LoadFromSettings(s);
        _manualActionsSection.LoadFromSettings(s);
        UpdateDefaultFileManagerStatusLabel();
        UpdateImageViewerAssociationStatusLabel();
        
        _hotkeyEdits.Clear();
        foreach (var kvp in s.Hotkeys) _hotkeyEdits[kvp.Key] = kvp.Value;

        _llmSettingsSection.LoadFromSettings(s);

        PopulateHotkeys();
    }

    private void UpdateDefaultFileManagerStatusLabel()
    {
        var status = FileManagerIntegrationService.GetCurrentStatus();
        switch (status)
        {
            case FileManagerIntegrationService.IntegrationStatus.AppliedHkcuHklm:
                _windowsIntegrationSection?.SetDefaultFileManagerStatus(Localization.T("status_applied"), Color.LightGreen);
                break;
            case FileManagerIntegrationService.IntegrationStatus.AppliedHkcu:
                _windowsIntegrationSection?.SetDefaultFileManagerStatus(Localization.T("status_applied_hkcu"), Color.LightGreen);
                break;
            case FileManagerIntegrationService.IntegrationStatus.AppliedHklm:
                _windowsIntegrationSection?.SetDefaultFileManagerStatus(Localization.T("status_applied_hklm"), Color.LightGreen);
                break;
            default:
                _windowsIntegrationSection?.SetDefaultFileManagerStatus(Localization.T("status_defaults"), Color.Gray);
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
                _windowsIntegrationSection?.SetImageViewerAssociationStatus(Localization.T("status_applied_hkcu"), Color.LightGreen);
                break;
            case ImageViewerAssociationService.AssociationStatus.PartialHkcu:
                _windowsIntegrationSection?.SetImageViewerAssociationStatus(Localization.T("status_partial_hkcu"), Color.Khaki);
                break;
            default:
                _windowsIntegrationSection?.SetImageViewerAssociationStatus(Localization.T("status_defaults"), Color.Gray);
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

    private void SaveSettings(object? sender, EventArgs e)
    {
        var s = AppSettings.Current;
        _generalSection.ApplyToSettings(s);

        _llmSettingsSection.ApplyToSettings(s);
        _appBehaviorSection.ApplyToSettings(s);
        _manualActionsSection.ApplyToSettings(s);

        foreach (var kvp in _hotkeyEdits) s.Hotkeys[kvp.Key] = kvp.Value;
        s.Save();
        StartupService.SyncWithSettings();
        try { Program.MultiWindowContext.Instance.SetTrayIconVisible(_generalSection.ShowTrayIcon); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
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

}
