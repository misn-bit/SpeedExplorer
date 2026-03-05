using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

public sealed class AppBehaviorSettingsControl : UserControl
{
    private readonly CheckBox _middleClickNewTabChk;
    private readonly CheckBox _permanentDeleteDefaultChk;
    private readonly CheckBox _useBuiltInImageViewerChk;
    private readonly CheckBox _useWindowsContextMenuChk;
    private readonly CheckBox _enableShellContextMenuChk;

    private int Scale(int pixels) => (int)(pixels * (DeviceDpi / 96.0));

    public AppBehaviorSettingsControl()
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

        _middleClickNewTabChk = CreateCheckBox(Localization.T("middle_click_new_tab"), Point.Empty);
        panel.Controls.Add(_middleClickNewTabChk);

        _useBuiltInImageViewerChk = CreateCheckBox(Localization.T("use_builtin_image_viewer"), Point.Empty);
        panel.Controls.Add(_useBuiltInImageViewerChk);

        panel.Controls.Add(new Panel { Height = Scale(14), Width = Scale(1), BackColor = Color.Transparent });

        var dangerLabel = CreateLabel(Localization.T("dangerous_setting_warning"), Point.Empty);
        dangerLabel.Font = new Font("Segoe UI Semibold", 9.5F);
        dangerLabel.ForeColor = Color.FromArgb(255, 198, 109);
        panel.Controls.Add(dangerLabel);

        _permanentDeleteDefaultChk = CreateCheckBox(Localization.T("permanent_delete_default"), Point.Empty);
        panel.Controls.Add(_permanentDeleteDefaultChk);

        panel.Controls.Add(new Panel { Height = Scale(16), Width = Scale(1), BackColor = Color.Transparent });

        var contextMenuLabel = CreateLabel(Localization.T("context_menu_section"), Point.Empty);
        contextMenuLabel.Font = new Font("Segoe UI Semibold", 9.5F);
        panel.Controls.Add(contextMenuLabel);

        _enableShellContextMenuChk = CreateCheckBox(Localization.T("enable_shell_menu"), Point.Empty);
        panel.Controls.Add(_enableShellContextMenuChk);

        _useWindowsContextMenuChk = CreateCheckBox(Localization.T("use_windows_menu"), Point.Empty);
        panel.Controls.Add(_useWindowsContextMenuChk);
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

    private Label CreateLabel(string text, Point loc) => new()
    {
        Text = text,
        Location = loc,
        AutoSize = true,
        Font = new Font("Segoe UI", 10),
        ForeColor = Color.FromArgb(240, 240, 240),
        BackColor = Color.Transparent
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
