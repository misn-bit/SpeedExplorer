using System;
using System.Drawing;
using System.Windows.Forms;
using SpeedExplorer.Design;

namespace SpeedExplorer.Settings;

/// <summary>App behavior page: middle-click, image viewer, context menu, permanent delete.</summary>
internal sealed class AppBehaviorSettingsControl : SettingsPage
{
    private ToggleSwitch _middleClickNewTabChk = null!;
    private ToggleSwitch _permanentDeleteDefaultChk = null!;
    private ToggleSwitch _useBuiltInImageViewerChk = null!;
    private ToggleSwitch _useWindowsContextMenuChk = null!;
    private ToggleSwitch _enableShellContextMenuChk = null!;

    public AppBehaviorSettingsControl() : base(Localization.T("app_behavior_tab"))
    {
        Font = new Font("Segoe UI", 10);

        var behavior = new SettingsGroup();
        _middleClickNewTabChk = behavior.AddToggle(Localization.T("middle_click_new_tab"), string.Empty, false, _ => { });
        _useBuiltInImageViewerChk = behavior.AddToggle(Localization.T("use_builtin_image_viewer"), string.Empty, false, _ => { });
        _useWindowsContextMenuChk = behavior.AddToggle(Localization.T("use_windows_menu"), string.Empty, false, _ => { });
        _enableShellContextMenuChk = behavior.AddToggle(Localization.T("enable_shell_menu"), string.Empty, false, _ => { });
        _permanentDeleteDefaultChk = behavior.AddToggle(Localization.T("permanent_delete_default"), string.Empty, false, _ => { });

        Controls.Add(behavior);
    }

    public void LoadFromSettings(AppSettings settings)
    {
        _middleClickNewTabChk.Checked = settings.MiddleClickOpensNewTab;
        _permanentDeleteDefaultChk.Checked = settings.PermanentDeleteByDefault;
        _useBuiltInImageViewerChk.Checked = settings.UseBuiltInImageViewer;
        _useWindowsContextMenuChk.Checked = settings.UseWindowsContextMenu;
        _enableShellContextMenuChk.Checked = settings.EnableShellContextMenu;
    }

    public void ApplyToSettings(AppSettings settings)
    {
        settings.MiddleClickOpensNewTab = _middleClickNewTabChk.Checked;
        settings.PermanentDeleteByDefault = _permanentDeleteDefaultChk.Checked;
        settings.UseBuiltInImageViewer = _useBuiltInImageViewerChk.Checked;
        settings.UseWindowsContextMenu = _useWindowsContextMenuChk.Checked;
        settings.EnableShellContextMenu = _enableShellContextMenuChk.Checked;
    }
}
