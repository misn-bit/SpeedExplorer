using System;
using System.Drawing;
using System.Windows.Forms;
using SpeedExplorer.Design;

namespace SpeedExplorer;

/// <summary>
/// Windows integration page: set SpeedExplorer as the default file manager
/// and/or take over image file associations, with live status under each button pair.
/// </summary>
internal sealed class WindowsIntegrationSettingsControl : SettingsPage
{
    private readonly Label _defaultFileManagerStatusLabel;
    private readonly Label _defaultImageAssocStatusLabel;

    public event Action<bool>? DefaultFileManagerRequested;
    public event Action<bool>? ImageViewerAssociationRequested;

    private int Scale(int pixels) => (int)(pixels * (DeviceDpi / 96.0));

    public WindowsIntegrationSettingsControl() : base(Localization.T("windows_integration_tab"))
    {
        Font = new Font("Segoe UI", 10);

        Controls.Add(Header(Localization.T("default_app_label")));

        _defaultFileManagerStatusLabel = new Label
        {
            Text = Localization.T("status_defaults"),
            AutoSize = true,
            Margin = new Padding(2, 0, 0, 6)
        };
        // "keep-color": status colors are set dynamically and preserved on theme changes
        _defaultFileManagerStatusLabel.Tag = "keep-color";
        Controls.Add(_defaultFileManagerStatusLabel);

        var fmApply = new FlatButton { Text = Localization.T("apply_default"), Kind = ButtonKind.Secondary };
        fmApply.Click += (s, e) => DefaultFileManagerRequested?.Invoke(true);
        var fmRestore = new FlatButton { Text = Localization.T("restore_default"), Kind = ButtonKind.Secondary };
        fmRestore.Click += (s, e) => DefaultFileManagerRequested?.Invoke(false);
        Controls.Add(ButtonRow(fmApply, fmRestore));

        Controls.Add(Header(Localization.T("default_image_app_label")));

        _defaultImageAssocStatusLabel = new Label
        {
            Text = Localization.T("status_defaults"),
            AutoSize = true,
            Margin = new Padding(2, 0, 0, 6)
        };
        _defaultImageAssocStatusLabel.Tag = "keep-color";
        Controls.Add(_defaultImageAssocStatusLabel);

        var imgApply = new FlatButton { Text = Localization.T("apply_default_images"), Kind = ButtonKind.Secondary };
        imgApply.Click += (s, e) => ImageViewerAssociationRequested?.Invoke(true);
        var imgRestore = new FlatButton { Text = Localization.T("restore_default_images"), Kind = ButtonKind.Secondary };
        imgRestore.Click += (s, e) => ImageViewerAssociationRequested?.Invoke(false);
        Controls.Add(ButtonRow(imgApply, imgRestore));
    }

    private Label Header(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(2, 12, 0, 4)
    };

    private FlowLayoutPanel ButtonRow(FlatButton a, FlatButton b)
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(0)
        };
        a.Margin = new Padding(0, 0, 8, 0);
        row.Controls.Add(a);
        row.Controls.Add(b);
        return row;
    }

    public void SetDefaultFileManagerStatus(string text, Color color)
    {
        _defaultFileManagerStatusLabel.Text = text;
        _defaultFileManagerStatusLabel.ForeColor = color;
    }

    public void SetImageViewerAssociationStatus(string text, Color color)
    {
        _defaultImageAssocStatusLabel.Text = text;
        _defaultImageAssocStatusLabel.ForeColor = color;
    }
}
