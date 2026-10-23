using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SpeedExplorer.Design;

namespace SpeedExplorer.Settings;

/// <summary>App behavior page: middle-click, image viewer, context menu, permanent delete.</summary>
internal sealed class AppBehaviorSettingsControl : SettingsPage
{
    private const string ThisPcPath = "::ThisPC";

    private Dropdown _defaultNewLocationDrop = null!;
    private string _lastDefaultNewLocationChoice = ThisPcPath;
    private ToggleSwitch _middleClickNewTabChk = null!;
    private ToggleSwitch _permanentDeleteDefaultChk = null!;
    private ToggleSwitch _useBuiltInImageViewerChk = null!;
    private ToggleSwitch _useWindowsContextMenuChk = null!;
    private ToggleSwitch _enableShellContextMenuChk = null!;

    public AppBehaviorSettingsControl() : base(Localization.T("app_behavior_tab"))
    {
        Font = new Font("Segoe UI", 10);

        var behavior = new SettingsGroup();
        _defaultNewLocationDrop = behavior.AddDropdown(
            Localization.T("default_new_location"),
            Localization.T("default_new_location_desc"),
            null,
            Localization.T("default_new_location_placeholder"),
            Localization.T("default_new_location_empty"),
            HandleDefaultNewLocationChanged);
        _defaultNewLocationDrop.SetItems(new[]
        {
            Localization.T("this_pc"),
            Localization.T("browse_for_folder")
        });
        _middleClickNewTabChk = behavior.AddToggle(Localization.T("middle_click_new_tab"), string.Empty, false, _ => { });
        _useBuiltInImageViewerChk = behavior.AddToggle(Localization.T("use_builtin_image_viewer"), string.Empty, false, _ => { });
        _useWindowsContextMenuChk = behavior.AddToggle(Localization.T("use_windows_menu"), string.Empty, false, _ => { });
        _enableShellContextMenuChk = behavior.AddToggle(Localization.T("enable_shell_menu"), string.Empty, false, _ => { });
        _permanentDeleteDefaultChk = behavior.AddToggle(Localization.T("permanent_delete_default"), string.Empty, false, _ => { });

        Controls.Add(behavior);
    }

    public void LoadFromSettings(AppSettings settings)
    {
        _defaultNewLocationDrop.SetItems(new[]
        {
            Localization.T("this_pc"),
            Localization.T("browse_for_folder")
        });
        _lastDefaultNewLocationChoice = string.IsNullOrWhiteSpace(settings.DefaultNewTabPath) ||
            string.Equals(settings.DefaultNewTabPath, ThisPcPath, StringComparison.OrdinalIgnoreCase)
                ? Localization.T("this_pc")
                : settings.DefaultNewTabPath;
        _defaultNewLocationDrop.Value = _lastDefaultNewLocationChoice;

        _middleClickNewTabChk.Checked = settings.MiddleClickOpensNewTab;
        _permanentDeleteDefaultChk.Checked = settings.PermanentDeleteByDefault;
        _useBuiltInImageViewerChk.Checked = settings.UseBuiltInImageViewer;
        _useWindowsContextMenuChk.Checked = settings.UseWindowsContextMenu;
        _enableShellContextMenuChk.Checked = settings.EnableShellContextMenu;
    }

    public void ApplyToSettings(AppSettings settings)
    {
        var choice = _defaultNewLocationDrop.Value;
        if (string.Equals(choice, Localization.T("this_pc"), StringComparison.Ordinal))
            settings.DefaultNewTabPath = ThisPcPath;
        else if (string.Equals(choice, Localization.T("browse_for_folder"), StringComparison.Ordinal))
            settings.DefaultNewTabPath = _lastDefaultNewLocationChoice;
        else if (!string.IsNullOrWhiteSpace(choice))
            settings.DefaultNewTabPath = choice;

        settings.MiddleClickOpensNewTab = _middleClickNewTabChk.Checked;
        settings.PermanentDeleteByDefault = _permanentDeleteDefaultChk.Checked;
        settings.UseBuiltInImageViewer = _useBuiltInImageViewerChk.Checked;
        settings.UseWindowsContextMenu = _useWindowsContextMenuChk.Checked;
        settings.EnableShellContextMenu = _enableShellContextMenuChk.Checked;
    }

    private void HandleDefaultNewLocationChanged(string? choice)
    {
        if (string.Equals(choice, Localization.T("browse_for_folder"), StringComparison.Ordinal))
        {
            var previousChoice = _lastDefaultNewLocationChoice;
            using var dialog = new FolderBrowserDialog
            {
                Description = Localization.T("default_new_location_browse_prompt"),
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };
            if (Directory.Exists(previousChoice))
                dialog.SelectedPath = previousChoice;

            if (dialog.ShowDialog(FindForm()) == DialogResult.OK && Directory.Exists(dialog.SelectedPath))
            {
                _lastDefaultNewLocationChoice = dialog.SelectedPath;
                _defaultNewLocationDrop.Value = dialog.SelectedPath;
            }
            else
            {
                _defaultNewLocationDrop.Value = previousChoice;
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(choice))
            _lastDefaultNewLocationChoice = choice;
    }
}
