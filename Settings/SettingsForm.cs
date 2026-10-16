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

/// <summary>
/// Settings window: a left <see cref="NavList"/> sidebar plus one page per section,
/// all rendered with the card design from Settings/Design.
/// </summary>
public class SettingsForm : Form
{
    private readonly GeneralSettingsControl _generalSection = new();
    private readonly AppBehaviorSettingsControl _appBehaviorSection = new();
    private readonly ManualContextActionsSettingsControl _manualActionsSection = new();
    private readonly LlmSettingsSectionControl _llmSettingsSection = new();
    private readonly WindowsIntegrationSettingsControl _windowsIntegrationSection = new();
    private readonly SettingsPage _hotkeysSection = new(Localization.T("hotkeys_tab"));
    private readonly SettingsPage _aboutSection = new(Localization.T("about_tab"));

    private readonly NavList _nav = new();
    private readonly Panel _pages = new();
    private readonly FlowLayoutPanel _footer = new();
    private FlatButton _saveBtn = null!, _cancelBtn = null!;

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

    public SettingsForm()
    {
        Text = Localization.T("settings_title");
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowIcon = false;
        KeyPreview = true;
        MinimumSize = new Size(720, 520);

        var s = AppSettings.Current;
        Width = Math.Max(760, s.SettingsWidth);
        Height = Math.Max(560, s.SettingsHeight);
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
        Design.Native.UseDarkTitleBar(Handle, Design.Theme.Current.IsDark);
        if (Design.Theme.Style == ThemeStyle.Rounded)
            Design.Native.RoundCorners(Handle);
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

        _footer.Dock = DockStyle.Bottom;
        _footer.Height = 56;
        _footer.Padding = new Padding(16, 12, 16, 12);
        _footer.FlowDirection = FlowDirection.RightToLeft; // save at the far right, cancel next to it
        _saveBtn = new FlatButton
        {
            Text = Localization.T("save_changes"),
            Kind = ButtonKind.Primary,
            Size = new Size(MeasureButtonWidth(Localization.T("save_changes")), 32)
        };
        _saveBtn.Click += (sender, e) => SaveSettings();
        _cancelBtn = new FlatButton
        {
            Text = Localization.T("cancel"),
            Kind = ButtonKind.Secondary,
            Size = new Size(MeasureButtonWidth(Localization.T("cancel")), 32)
        };
        _cancelBtn.Click += (sender, e) => { DialogResult = DialogResult.Cancel; Close(); };
        _footer.Controls.Add(_saveBtn);
        _footer.Controls.Add(_cancelBtn);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.Controls.Add(center, 0, 0);
        root.Controls.Add(_footer, 0, 1);
        Controls.Add(root);

        // Pages
        var pages = new Control[]
        {
            _generalSection, _appBehaviorSection, _manualActionsSection,
            _windowsIntegrationSection, _llmSettingsSection, _hotkeysSection, _aboutSection
        };
        foreach (var p in pages)
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
        foreach (Control p in _pages.Controls)
            p.Visible = false;
        if (index >= 0 && index < _pages.Controls.Count)
            _pages.Controls[index].Visible = true;
    }

    // ---------------------------------------------------------------- theme

    /// <summary>Re-themes every control that doesn't paint itself.</summary>
    private void ApplyTheme()
    {
        var p = Design.Theme.Current;
        BackColor = p.Page;
        ForeColor = p.Text;
        _pages.BackColor = p.Page;
        _footer.BackColor = p.Card;
        foreach (Control page in _pages.Controls) page.BackColor = p.Page;
        _nav.Invalidate();
        ThemeWalk(this, p);
        if (IsHandleCreated)
            Design.Native.UseDarkTitleBar(Handle, p.IsDark);
    }

    private static void ThemeWalk(Control c, Palette p)
    {
        foreach (Control child in c.Controls)
        {
            if (child is IThemed)
            {
                if (child is NavList) child.BackColor = p.Page; // it doesn't paint its own background
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

        var misc = new SettingsGroup();
        misc.AddRow(Localization.T("honorable_mention"), string.Empty, CloneHost());
        misc.AddRow(Localization.T("ran_locally"), string.Empty, CloneHost());
        misc.AddButton(Localization.T("directed_by"), string.Empty, Localization.T("view_license"), ButtonKind.Secondary, ViewLicense);
        _aboutSection.Controls.Add(misc);
    }

    private static Label PageLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Tag = "dim",
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

    private void SaveSettings()
    {
        var s = AppSettings.Current;
        _generalSection.ApplyToSettings(s);
        _llmSettingsSection.ApplyToSettings(s);
        _appBehaviorSection.ApplyToSettings(s);
        _manualActionsSection.ApplyToSettings(s);

        foreach (var kvp in _hotkeyEdits) s.Hotkeys[kvp.Key] = kvp.Value;
        s.Save();
        StartupService.SyncWithSettings();
        try { Program.MultiWindowContext.Instance.SetTrayIconVisible(_generalSection.ShowTrayIcon); }
        catch (Exception ex) { Debug.WriteLine(ex); }

        DialogResult = DialogResult.OK;
        Close();
    }
}
