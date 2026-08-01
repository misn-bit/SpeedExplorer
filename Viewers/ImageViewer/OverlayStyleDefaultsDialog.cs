using System;
using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

internal sealed class OverlayStyleDefaultsDialog : Form
{
    private static readonly Color DefaultTextColor = Color.FromArgb(250, 250, 250);
    private static readonly Color DefaultTextOutlineColor = Color.FromArgb(255, 0, 0, 0);
    private static readonly Color DefaultBoxFillColor = Color.FromArgb(242, 7, 19, 36);
    private static readonly Color DefaultBoxBorderColor = Color.FromArgb(220, 125, 198, 255);

    private readonly OverlayStyleDefaults _settings;
    private readonly Button _textColorButton;
    private readonly Button _textOutlineColorButton;
    private readonly ComboBox _horizontalAlignmentCombo;
    private readonly ComboBox _verticalAlignmentCombo;
    private readonly Button _boxFillColorButton;
    private readonly NumericUpDown _boxFillOpacityBox;
    private readonly Button _boxBorderColorButton;
    private readonly CheckBox _boxFillVisibleCheck;
    private readonly CheckBox _boxBorderVisibleCheck;
    private readonly CheckBox _textOutlineVisibleCheck;

    public OverlayStyleDefaults Settings => _settings.Clone();

    public OverlayStyleDefaultsDialog(OverlayStyleDefaults settings, string title)
    {
        _settings = settings.Clone();
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(520, 470);
        MinimumSize = new Size(460, 430);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 9,
            Padding = new Padding(10),
            BackColor = BackColor
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < layout.RowCount; i++)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        _textColorButton = CreateColorButton(_settings.TextColorArgb, ChooseTextColor);
        _textOutlineColorButton = CreateColorButton(_settings.TextOutlineColorArgb, ChooseTextOutlineColor);
        _horizontalAlignmentCombo = CreateAlignmentCombo(_settings.TextAlignment, vertical: false);
        _verticalAlignmentCombo = CreateAlignmentCombo(_settings.TextVerticalAlignment, vertical: true);
        _boxFillColorButton = CreateColorButton(_settings.BoxFillColorArgb, ChooseBoxFillColor);
        _boxFillOpacityBox = CreateOpacityNumber(_settings.BoxFillColorArgb);
        _boxBorderColorButton = CreateColorButton(_settings.BoxBorderColorArgb, ChooseBoxBorderColor);
        _boxFillVisibleCheck = CreateVisibilityCheck("Fill visible", _settings.BoxFillVisible ?? true, value => _settings.BoxFillVisible = value);
        _boxBorderVisibleCheck = CreateVisibilityCheck("Outline visible", _settings.BoxBorderVisible ?? true, value => _settings.BoxBorderVisible = value);
        _textOutlineVisibleCheck = CreateVisibilityCheck("Text outline visible", _settings.TextOutlineVisible ?? false, value => _settings.TextOutlineVisible = value);
        _boxFillOpacityBox.ValueChanged += (_, _) => UpdateBoxFillOpacity();

        AddLabel(layout, "Text color:", 0);
        layout.Controls.Add(_textColorButton, 1, 0);
        AddLabel(layout, "Text outline:", 1);
        layout.Controls.Add(_textOutlineColorButton, 1, 1);
        AddLabel(layout, "Alignment:", 2);
        layout.Controls.Add(_horizontalAlignmentCombo, 1, 2);
        AddLabel(layout, "Vertical alignment:", 3);
        layout.Controls.Add(_verticalAlignmentCombo, 1, 3);
        AddLabel(layout, "Box fill:", 4);
        layout.Controls.Add(_boxFillColorButton, 1, 4);
        AddLabel(layout, "Fill opacity:", 5);
        layout.Controls.Add(_boxFillOpacityBox, 1, 5);
        AddLabel(layout, "Box outline:", 6);
        layout.Controls.Add(_boxBorderColorButton, 1, 6);
        AddLabel(layout, "Visibility:", 7);
        var visibilityPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 7, 0, 0)
        };
        visibilityPanel.Controls.Add(_boxFillVisibleCheck);
        visibilityPanel.Controls.Add(_boxBorderVisibleCheck);
        visibilityPanel.Controls.Add(_textOutlineVisibleCheck);
        layout.Controls.Add(visibilityPanel, 1, 7);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = BackColor
        };
        var ok = CreateButton("Save", DialogResult.OK);
        var cancel = CreateButton("Cancel", DialogResult.Cancel);
        var reset = CreateButton("Reset defaults", DialogResult.None);
        reset.Click += (_, _) => ResetDefaults();
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(reset);
        layout.Controls.Add(buttons, 0, 8);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        CancelButton = cancel;
    }

    private ComboBox CreateAlignmentCombo(StringAlignment? alignment, bool vertical)
    {
        var combo = new ComboBox
        {
            Dock = DockStyle.Left,
            Width = 180,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.Gainsboro
        };
        combo.Items.AddRange(vertical
            ? new object[] { "Default", "Top", "Center", "Bottom" }
            : new object[] { "Default", "Left", "Center", "Right" });
        combo.SelectedIndex = alignment switch
        {
            StringAlignment.Near => 1,
            StringAlignment.Center => 2,
            StringAlignment.Far => 3,
            _ => 0
        };
        combo.SelectedIndexChanged += (_, _) =>
        {
            StringAlignment? value = combo.SelectedIndex switch
            {
                1 => StringAlignment.Near,
                2 => StringAlignment.Center,
                3 => StringAlignment.Far,
                _ => null
            };
            if (vertical)
                _settings.TextVerticalAlignment = value;
            else
                _settings.TextAlignment = value;
        };
        return combo;
    }

    private Button CreateColorButton(int? argb, EventHandler handler)
    {
        var button = new Button
        {
            Width = 220,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleCenter
        };
        button.Click += handler;
        UpdateColorButton(button, argb);
        return button;
    }

    private static CheckBox CreateVisibilityCheck(string text, bool value, Action<bool> changed)
    {
        var check = new CheckBox
        {
            Text = text,
            AutoSize = true,
            Checked = value,
            ForeColor = Color.Gainsboro,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 10, 0)
        };
        check.CheckedChanged += (_, _) => changed(check.Checked);
        return check;
    }

    private void ChooseTextColor(object? sender, EventArgs e)
        => ChooseColor(_textColorButton, value => _settings.TextColorArgb = value, _settings.TextColorArgb, DefaultTextColor);

    private void ChooseTextOutlineColor(object? sender, EventArgs e)
        => ChooseColor(_textOutlineColorButton, value => _settings.TextOutlineColorArgb = value, _settings.TextOutlineColorArgb, DefaultTextOutlineColor);

    private void ChooseBoxFillColor(object? sender, EventArgs e)
    {
        ChooseColor(_boxFillColorButton, value => _settings.BoxFillColorArgb = value, _settings.BoxFillColorArgb, DefaultBoxFillColor);
        if (_settings.BoxFillColorArgb.HasValue)
            _boxFillOpacityBox.Value = AlphaToPercent(Color.FromArgb(_settings.BoxFillColorArgb.Value).A);
    }

    private void ChooseBoxBorderColor(object? sender, EventArgs e)
        => ChooseColor(_boxBorderColorButton, value => _settings.BoxBorderColorArgb = value, _settings.BoxBorderColorArgb, DefaultBoxBorderColor);

    private void ChooseColor(Button button, Action<int> setValue, int? currentValue, Color fallback)
    {
        Color current = currentValue.HasValue ? Color.FromArgb(currentValue.Value) : fallback;
        using var dialog = new ColorDialog
        {
            AllowFullOpen = true,
            AnyColor = true,
            FullOpen = true,
            Color = current
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        int value = Color.FromArgb(current.A, dialog.Color.R, dialog.Color.G, dialog.Color.B).ToArgb();
        setValue(value);
        UpdateColorButton(button, value);
    }

    private void UpdateBoxFillOpacity()
    {
        Color current = _settings.BoxFillColorArgb.HasValue
            ? Color.FromArgb(_settings.BoxFillColorArgb.Value)
            : DefaultBoxFillColor;
        int alpha = PercentToAlpha(_boxFillOpacityBox.Value);
        _settings.BoxFillColorArgb = Color.FromArgb(alpha, current.R, current.G, current.B).ToArgb();
        UpdateColorButton(_boxFillColorButton, _settings.BoxFillColorArgb);
    }

    private void ResetDefaults()
    {
        _settings.TextColorArgb = null;
        _settings.TextOutlineColorArgb = null;
        _settings.TextAlignment = null;
        _settings.TextVerticalAlignment = null;
        _settings.TextOutlineVisible = null;
        _settings.BoxFillColorArgb = null;
        _settings.BoxFillVisible = null;
        _settings.BoxBorderColorArgb = null;
        _settings.BoxBorderVisible = null;
        _horizontalAlignmentCombo.SelectedIndex = 0;
        _verticalAlignmentCombo.SelectedIndex = 0;
        _boxFillOpacityBox.Value = AlphaToPercent(DefaultBoxFillColor.A);
        _settings.BoxFillColorArgb = null;
        _boxFillVisibleCheck.Checked = true;
        _boxBorderVisibleCheck.Checked = true;
        _textOutlineVisibleCheck.Checked = false;
        UpdateColorButton(_textColorButton, null);
        UpdateColorButton(_textOutlineColorButton, null);
        UpdateColorButton(_boxFillColorButton, null);
        UpdateColorButton(_boxBorderColorButton, null);
    }

    private static NumericUpDown CreateOpacityNumber(int? argb)
        => new()
        {
            Width = 90,
            DecimalPlaces = 0,
            Increment = 5,
            Minimum = 0,
            Maximum = 100,
            Value = AlphaToPercent(argb.HasValue ? Color.FromArgb(argb.Value).A : DefaultBoxFillColor.A),
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.Gainsboro
        };

    private static int AlphaToPercent(int alpha)
        => Math.Clamp((int)Math.Round(alpha * 100d / 255d), 0, 100);

    private static int PercentToAlpha(decimal percent)
        => Math.Clamp((int)Math.Round(percent * 255m / 100m), 0, 255);

    private static void UpdateColorButton(Button button, int? argb)
    {
        if (!argb.HasValue)
        {
            button.Text = "Default";
            button.BackColor = Color.FromArgb(60, 60, 60);
            button.ForeColor = Color.Gainsboro;
            return;
        }

        Color color = Color.FromArgb(argb.Value);
        Color swatch = Color.FromArgb(255, color.R, color.G, color.B);
        button.Text = color.A < 255 ? $"Choose color ({AlphaToPercent(color.A)}%)" : "Choose color";
        button.BackColor = swatch;
        button.ForeColor = swatch.GetBrightness() > 0.55f ? Color.Black : Color.White;
    }

    private static void AddLabel(TableLayoutPanel layout, string text, int row)
    {
        layout.Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = Color.Gainsboro
        }, 0, row);
    }

    private static Button CreateButton(string text, DialogResult result)
        => new()
        {
            Text = text,
            DialogResult = result,
            Width = 105,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.Gainsboro
        };
}
