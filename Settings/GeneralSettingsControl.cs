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
    private ToggleSwitch _showIconsChk = null!, _useEmojiChk = null!, _useSystemIconsChk = null!;
    private ToggleSwitch _resolveUniqueChk = null!, _showThumbnailsChk = null!;
    private ToggleSwitch _showRecentChk = null!, _showCommonChk = null!;
    private ToggleSwitch _showDesktopChk = null!, _showDocumentsChk = null!;
    private ToggleSwitch _showDownloadsChk = null!, _showPicturesChk = null!;
    private ToggleSwitch _showSidebarVScrollChk = null!;
    private ToggleSwitch _showFullPathsSidebarChk = null!;
    private ToggleSwitch _showSidebarIconsChk = null!, _useEmojiSidebarIconsChk = null!, _useColoredSidebarIconsChk = null!;
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

        // Icons
        var icons = new SettingsGroup();
        _showIconsChk = icons.AddToggle(Localization.T("show_icons"), string.Empty, true,
            _ => UpdateToggles());
        _useEmojiChk = icons.AddToggle(Localization.T("use_emoji"), string.Empty, false,
            _ => UpdateToggles());
        _useSystemIconsChk = icons.AddToggle(Localization.T("use_colored_icons"), string.Empty, false, _ => { });
        _resolveUniqueChk = icons.AddToggle(Localization.T("resolve_unique"), string.Empty, false, _ => { });
        _showThumbnailsChk = icons.AddToggle(Localization.T("show_thumbnails"), string.Empty, false, _ => { });
        AddGroup(Localization.T("icons_section"), icons);

        var sidebarIcons = new SettingsGroup();
        _showSidebarIconsChk = sidebarIcons.AddToggle(Localization.T("show_sidebar_icons"), Localization.T("show_sidebar_icons_desc"), true,
            _ => UpdateToggles());
        _useEmojiSidebarIconsChk = sidebarIcons.AddToggle(Localization.T("use_emoji_sidebar_icons"), Localization.T("use_emoji_sidebar_icons_desc"), false,
            _ => UpdateToggles());
        _useColoredSidebarIconsChk = sidebarIcons.AddToggle(Localization.T("use_colored_sidebar_icons"), Localization.T("use_colored_sidebar_icons_desc"), true, _ => { });
        AddGroup(Localization.T("sidebar_icons_section"), sidebarIcons);

        // Windows folders
        var folders = new SettingsGroup();
        _showRecentChk = folders.AddToggle(Localization.T("recent_folders"), string.Empty, false, _ => { });
        _showFullPathsSidebarChk = folders.AddToggle(
            Localization.T("show_full_paths_sidebar"), Localization.T("show_full_paths_sidebar_desc"), false, _ => { });
        _showCommonChk = folders.AddToggle(Localization.T("show_windows_folders"), string.Empty, false,
            _ => UpdateToggles());
        _showDesktopChk = folders.AddToggle(Localization.T("desktop"), string.Empty, false, _ => { });
        _showDocumentsChk = folders.AddToggle(Localization.T("documents"), string.Empty, false, _ => { });
        _showDownloadsChk = folders.AddToggle(Localization.T("downloads"), string.Empty, false, _ => { });
        _showPicturesChk = folders.AddToggle(Localization.T("pictures"), string.Empty, false, _ => { });
        _showSidebarVScrollChk = folders.AddToggle(Localization.T("show_sidebar_scrollbar"), string.Empty, false, _ => { });
        AddGroup(Localization.T("windows_folders_section"), folders);

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
        _showIconsChk.Checked = s.ShowIcons;
        _useEmojiChk.Checked = s.UseEmojiIcons;
        _useSystemIconsChk.Checked = s.UseSystemIcons;
        _resolveUniqueChk.Checked = s.ResolveUniqueIcons;
        _showThumbnailsChk.Checked = s.ShowThumbnails;
        _showSidebarIconsChk.Checked = s.ShowSidebarIcons;
        _useEmojiSidebarIconsChk.Checked = s.UseEmojiSidebarIcons;
        _useColoredSidebarIconsChk.Checked = s.UseColoredSidebarIcons;
        _showCommonChk.Checked = s.ShowSidebarCommon;
        _showDesktopChk.Checked = s.ShowSidebarDesktop;
        _showDocumentsChk.Checked = s.ShowSidebarDocuments;
        _showDownloadsChk.Checked = s.ShowSidebarDownloads;
        _showPicturesChk.Checked = s.ShowSidebarPictures;
        _showRecentChk.Checked = s.ShowSidebarRecent;
        _showFullPathsSidebarChk.Checked = s.ShowFullPathsInSidebar;
        _showSidebarVScrollChk.Checked = s.ShowSidebarVerticalScrollbar;
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
        s.ShowIcons = _showIconsChk.Checked;
        s.UseEmojiIcons = _useEmojiChk.Checked;
        s.UseSystemIcons = _useSystemIconsChk.Checked;
        s.ResolveUniqueIcons = _resolveUniqueChk.Checked;
        s.ShowThumbnails = _showThumbnailsChk.Checked;
        s.ShowSidebarIcons = _showSidebarIconsChk.Checked;
        s.UseEmojiSidebarIcons = _useEmojiSidebarIconsChk.Checked;
        s.UseColoredSidebarIcons = _useColoredSidebarIconsChk.Checked;
        s.ShowSidebarCommon = _showCommonChk.Checked;
        s.ShowSidebarDesktop = _showDesktopChk.Checked;
        s.ShowSidebarDocuments = _showDocumentsChk.Checked;
        s.ShowSidebarDownloads = _showDownloadsChk.Checked;
        s.ShowSidebarPictures = _showPicturesChk.Checked;
        s.ShowSidebarRecent = _showRecentChk.Checked;
        s.ShowFullPathsInSidebar = _showFullPathsSidebarChk.Checked;
        s.ShowSidebarVerticalScrollbar = _showSidebarVScrollChk.Checked;
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

    public bool RequiresMainWindowRefresh(Control changedControl) =>
        ReferenceEquals(changedControl, _showIconsChk) ||
        ReferenceEquals(changedControl, _useEmojiChk) ||
        ReferenceEquals(changedControl, _useSystemIconsChk) ||
        ReferenceEquals(changedControl, _resolveUniqueChk) ||
        ReferenceEquals(changedControl, _showThumbnailsChk) ||
        ReferenceEquals(changedControl, _showSidebarIconsChk) ||
        ReferenceEquals(changedControl, _useEmojiSidebarIconsChk) ||
        ReferenceEquals(changedControl, _useColoredSidebarIconsChk) ||
        ReferenceEquals(changedControl, _showRecentChk) ||
        ReferenceEquals(changedControl, _showFullPathsSidebarChk) ||
        ReferenceEquals(changedControl, _showCommonChk) ||
        ReferenceEquals(changedControl, _showDesktopChk) ||
        ReferenceEquals(changedControl, _showDocumentsChk) ||
        ReferenceEquals(changedControl, _showDownloadsChk) ||
        ReferenceEquals(changedControl, _showPicturesChk) ||
        ReferenceEquals(changedControl, _showSidebarVScrollChk);

    /// <summary>Re-applies enabled/disabled state for dependent toggles.</summary>
    public void UpdateToggles()
    {
        bool show = _showIconsChk.Checked;
        bool emoji = _useEmojiChk.Checked;
        _useEmojiChk.Enabled = true;
        _useSystemIconsChk.Enabled = show && !emoji;
        _resolveUniqueChk.Enabled = show && !emoji;
        _showThumbnailsChk.Enabled = show && !emoji;

        bool showSidebarIcons = _showSidebarIconsChk.Checked;
        bool emojiSidebarIcons = _useEmojiSidebarIconsChk.Checked;
        _useEmojiSidebarIconsChk.Enabled = showSidebarIcons;
        _useColoredSidebarIconsChk.Enabled = showSidebarIcons && !emojiSidebarIcons;

        bool common = _showCommonChk.Checked;
        _showDesktopChk.Enabled = common;
        _showDocumentsChk.Enabled = common;
        _showDownloadsChk.Enabled = common;
        _showPicturesChk.Enabled = common;

        bool navDebug = _debugNavLogChk.Checked;
        _debugNavGcChk.Enabled = navDebug;
        _debugNavUiQueueChk.Enabled = navDebug;
        _debugNavPostBindChk.Enabled = navDebug;
    }
}
