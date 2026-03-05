using System;
using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

public sealed class WindowsIntegrationSettingsControl : UserControl
{
    private readonly Label _defaultFileManagerStatusLabel;
    private readonly Label _defaultImageAssocStatusLabel;

    public event Action<bool>? DefaultFileManagerRequested;
    public event Action<bool>? ImageViewerAssociationRequested;

    private int Scale(int pixels) => (int)(pixels * (DeviceDpi / 96.0));

    public WindowsIntegrationSettingsControl()
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

        panel.Controls.Add(CreateLabel(Localization.T("default_app_label"), Point.Empty));

        _defaultFileManagerStatusLabel = CreateLabel(Localization.T("status_defaults"), Point.Empty);
        _defaultFileManagerStatusLabel.ForeColor = Color.Gray;
        panel.Controls.Add(_defaultFileManagerStatusLabel);

        var defaultFileManagerApplyBtn = new Button
        {
            Text = Localization.T("apply_default"),
            AutoSize = true,
            Margin = new Padding(0, Scale(8), 0, 0)
        };
        SettingsButtonStyle.ApplyNeutral(defaultFileManagerApplyBtn);
        defaultFileManagerApplyBtn.Click += (s, e) => DefaultFileManagerRequested?.Invoke(true);
        panel.Controls.Add(defaultFileManagerApplyBtn);

        var defaultFileManagerRestoreBtn = new Button
        {
            Text = Localization.T("restore_default"),
            AutoSize = true,
            Margin = new Padding(0, Scale(8), 0, 0)
        };
        SettingsButtonStyle.ApplyNeutral(defaultFileManagerRestoreBtn);
        defaultFileManagerRestoreBtn.Click += (s, e) => DefaultFileManagerRequested?.Invoke(false);
        panel.Controls.Add(defaultFileManagerRestoreBtn);

        panel.Controls.Add(new Panel { Height = Scale(12), Width = Scale(1), BackColor = Color.Transparent });
        panel.Controls.Add(CreateLabel(Localization.T("default_image_app_label"), Point.Empty));

        _defaultImageAssocStatusLabel = CreateLabel(Localization.T("status_defaults"), Point.Empty);
        _defaultImageAssocStatusLabel.ForeColor = Color.Gray;
        panel.Controls.Add(_defaultImageAssocStatusLabel);

        var defaultImageAssocApplyBtn = new Button
        {
            Text = Localization.T("apply_default_images"),
            AutoSize = true,
            Margin = new Padding(0, Scale(8), 0, 0)
        };
        SettingsButtonStyle.ApplyNeutral(defaultImageAssocApplyBtn);
        defaultImageAssocApplyBtn.Click += (s, e) => ImageViewerAssociationRequested?.Invoke(true);
        panel.Controls.Add(defaultImageAssocApplyBtn);

        var defaultImageAssocRestoreBtn = new Button
        {
            Text = Localization.T("restore_default_images"),
            AutoSize = true,
            Margin = new Padding(0, Scale(8), 0, 0)
        };
        SettingsButtonStyle.ApplyNeutral(defaultImageAssocRestoreBtn);
        defaultImageAssocRestoreBtn.Click += (s, e) => ImageViewerAssociationRequested?.Invoke(false);
        panel.Controls.Add(defaultImageAssocRestoreBtn);
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

    private Label CreateLabel(string text, Point loc) => new()
    {
        Text = text,
        Location = loc,
        AutoSize = true,
        Font = new Font("Segoe UI", 10),
        ForeColor = Color.FromArgb(240, 240, 240),
        BackColor = Color.Transparent
    };
}
