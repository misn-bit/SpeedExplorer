using System;
using System.Drawing;
using System.Windows.Forms;
using SpeedExplorer.Design;

namespace SpeedExplorer.Settings;

/// <summary>
/// General page: the app's real settings, laid out with the card design (SettingsGroup rows).
/// Loads and saves directly against AppSettings like before.
/// </summary>
internal sealed class GeneralSettingsControl : SettingsPage
{
    private Dropdown _languageDrop = null!;

    private ToggleSwitch _runAtStartupChk = null!, _runInBackgroundChk = null!, _showTrayIconChk = null!;
    private ToggleSwitch _debugNavLogChk = null!, _debugNavGcChk = null!, _debugNavUiQueueChk = null!;
    private ToggleSwitch _debugNavPostBindChk = null!, _debugCrashLogChk = null!, _debugShellLogChk = null!;

    public GeneralSettingsControl() : base(Localization.T("general_tab"))
    {
        Font = new Font("Segoe UI", 10);

        // Language
        var language = new SettingsGroup();
        _languageDrop = language.AddDropdown(
            Localization.T("language"), string.Empty, "English", string.Empty, string.Empty, _ => { });
        _languageDrop.SetItems(new[] { "English", "Русский" });
        AddGroup(null, language);

        // Startup & background
        var startup = new SettingsGroup();
        _runAtStartupChk = startup.AddToggle(Localization.T("run_startup"), string.Empty, false, _ => { });
        _runInBackgroundChk = startup.AddToggle(Localization.T("run_in_background"), string.Empty, false, _ => { });
        _showTrayIconChk = startup.AddToggle(Localization.T("show_tray_icon"), string.Empty, false, _ => { });
        AddGroup(Localization.T("startup_bg"), startup);

        // Debug
        var debug = new SettingsGroup();
        _debugNavLogChk = debug.AddToggle(Localization.T("debug_nav_log"), string.Empty, false,
            _ => UpdateToggles());
        _debugNavGcChk = debug.AddToggle(Localization.T("debug_nav_gc"), string.Empty, false, _ => { });
        _debugNavUiQueueChk = debug.AddToggle(Localization.T("debug_nav_uiq"), string.Empty, false, _ => { });
        _debugNavPostBindChk = debug.AddToggle(Localization.T("debug_nav_postbind"), string.Empty, false, _ => { });
        _debugCrashLogChk = debug.AddToggle(Localization.T("debug_crash_log"), string.Empty, false, _ => { });
        _debugShellLogChk = debug.AddToggle(Localization.T("debug_shell_log"), string.Empty, false, _ => { });
        AddGroup(Localization.T("debug_section"), debug);

        UpdateToggles();
    }

    // This control is a plain FlowLayoutPanel host; sections are added top to bottom.
    private void AddGroup(string? label, SettingsGroup group)
    {
        if (label is not null)
            Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(2, 16, 0, 6), Tag = "section-title" });
        Controls.Add(group);
    }

    public void LoadFromSettings(AppSettings s)
    {
        _languageDrop.Value = string.Equals(s.UiLanguage, "ru", StringComparison.OrdinalIgnoreCase) ? "Русский" : "English";
        _runAtStartupChk.Checked = s.RunAtStartup;
        _runInBackgroundChk.Checked = s.RunInBackground;
        _showTrayIconChk.Checked = s.ShowTrayIcon;
        _debugNavLogChk.Checked = s.DebugNavigationLogging;
        _debugNavGcChk.Checked = s.DebugNavigationGcStats;
        _debugNavUiQueueChk.Checked = s.DebugNavigationUiQueue;
        _debugNavPostBindChk.Checked = s.DebugNavigationPostBind;
        _debugCrashLogChk.Checked = s.DebugCrashLogging;
        _debugShellLogChk.Checked = s.DebugShellLogging;
        UpdateToggles();
    }

    public void ApplyToSettings(AppSettings s)
    {
        s.UiLanguage = _languageDrop.Value == "Русский" ? "ru" : "en";
        s.RunAtStartup = _runAtStartupChk.Checked;
        s.RunInBackground = _runInBackgroundChk.Checked;
        s.ShowTrayIcon = _showTrayIconChk.Checked;
        s.DebugNavigationLogging = _debugNavLogChk.Checked;
        s.DebugNavigationGcStats = _debugNavGcChk.Checked;
        s.DebugNavigationUiQueue = _debugNavUiQueueChk.Checked;
        s.DebugNavigationPostBind = _debugNavPostBindChk.Checked;
        s.DebugCrashLogging = _debugCrashLogChk.Checked;
        s.DebugShellLogging = _debugShellLogChk.Checked;
    }

    public bool ShowTrayIcon => _showTrayIconChk.Checked;
    public bool RunAtStartup => _runAtStartupChk.Checked;

    /// <summary>Re-applies enabled/disabled state for dependent toggles.</summary>
    public void UpdateToggles()
    {
        bool navDebug = _debugNavLogChk.Checked;
        _debugNavGcChk.Enabled = navDebug;
        _debugNavUiQueueChk.Enabled = navDebug;
        _debugNavPostBindChk.Enabled = navDebug;
    }
}
