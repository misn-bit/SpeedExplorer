using System;
using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

internal sealed class OverlayStyleDefaults
{
    public int? TextColorArgb { get; set; }
    public int? TextOutlineColorArgb { get; set; }
    public StringAlignment? TextAlignment { get; set; }
    public StringAlignment? TextVerticalAlignment { get; set; }
    public bool? TextOutlineVisible { get; set; }
    public int? BoxFillColorArgb { get; set; }
    public bool? BoxFillVisible { get; set; }
    public int? BoxBorderColorArgb { get; set; }
    public bool? BoxBorderVisible { get; set; }

    public bool IsEmpty =>
        TextColorArgb == null &&
        TextOutlineColorArgb == null &&
        TextAlignment == null &&
        TextVerticalAlignment == null &&
        TextOutlineVisible == null &&
        BoxFillColorArgb == null &&
        BoxFillVisible == null &&
        BoxBorderColorArgb == null &&
        BoxBorderVisible == null;

    public OverlayStyleDefaults Clone()
        => new()
        {
            TextColorArgb = TextColorArgb,
            TextOutlineColorArgb = TextOutlineColorArgb,
            TextAlignment = TextAlignment,
            TextVerticalAlignment = TextVerticalAlignment,
            TextOutlineVisible = TextOutlineVisible,
            BoxFillColorArgb = BoxFillColorArgb,
            BoxFillVisible = BoxFillVisible,
            BoxBorderColorArgb = BoxBorderColorArgb,
            BoxBorderVisible = BoxBorderVisible
        };
}

internal sealed class OverlayBlockEditDialog : Form
{
    private const int SliderScale = 10000;

    private readonly TextBox _ocrTextBox;
    private readonly TextBox _translationTextBox;
    private readonly NumericUpDown _xBox;
    private readonly NumericUpDown _yBox;
    private readonly NumericUpDown _wBox;
    private readonly NumericUpDown _hBox;
    private readonly NumericUpDown _fontBox;
    private readonly TrackBar _xSlider;
    private readonly TrackBar _ySlider;
    private readonly TrackBar _wSlider;
    private readonly TrackBar _hSlider;
    private readonly TrackBar _fontSlider;
    private readonly Button _textColorButton;
    private readonly ComboBox _alignmentCombo;
    private readonly ComboBox _verticalAlignmentCombo;
    private readonly Button _textOutlineColorButton;
    private readonly Button _boxFillColorButton;
    private readonly NumericUpDown _boxFillOpacityBox;
    private readonly Button _boxBorderColorButton;
    private readonly CheckBox _boxFillVisibleCheck;
    private readonly CheckBox _boxBorderVisibleCheck;
    private readonly CheckBox _textOutlineVisibleCheck;
    private int? _textColorArgb;
    private int? _textOutlineColorArgb;
    private StringAlignment? _textAlignment;
    private StringAlignment? _textVerticalAlignment;
    private bool? _textOutlineVisible;
    private int? _boxFillColorArgb;
    private int? _boxBorderColorArgb;
    private bool? _boxFillVisible;
    private bool? _boxBorderVisible;
    private bool _styleSettingsChanged;
    private bool _syncing;

    public event EventHandler? PreviewChanged;

    public string OcrText => _ocrTextBox.Text;
    public string TranslationText => _translationTextBox.Text;
    public RectangleF NormalizedRect => new(
        (float)_xBox.Value,
        (float)_yBox.Value,
        (float)_wBox.Value,
        (float)_hBox.Value);
    public float NormalizedFontSize => (float)_fontBox.Value;
    public int? TextColorArgb => _textColorArgb;
    public int? TextOutlineColorArgb => _textOutlineColorArgb;
    public StringAlignment? TextAlignment => _textAlignment;
    public StringAlignment? TextVerticalAlignment => _textVerticalAlignment;
    public bool? TextOutlineVisible => _textOutlineVisible;
    public int? BoxFillColorArgb => _boxFillColorArgb;
    public int? BoxBorderColorArgb => _boxBorderColorArgb;
    public bool? BoxFillVisible => _boxFillVisible;
    public bool? BoxBorderVisible => _boxBorderVisible;
    public bool StyleSettingsChanged => _styleSettingsChanged;

    public OverlayBlockEditDialog(
        string ocrText,
        string translationText,
        RectangleF normalizedRect,
        float normalizedFontSize,
        int? textColorArgb = null,
        int? textOutlineColorArgb = null,
        StringAlignment? textAlignment = null,
        StringAlignment? textVerticalAlignment = null,
        bool? textOutlineVisible = null,
        int? boxFillColorArgb = null,
        int? boxBorderColorArgb = null,
        bool? boxFillVisible = null,
        bool? boxBorderVisible = null)
    {
        _textColorArgb = textColorArgb;
        _textOutlineColorArgb = textOutlineColorArgb;
        _textAlignment = textAlignment;
        _textVerticalAlignment = textVerticalAlignment;
        _textOutlineVisible = textOutlineVisible;
        _boxFillColorArgb = boxFillColorArgb;
        _boxBorderColorArgb = boxBorderColorArgb;
        _boxFillVisible = boxFillVisible;
        _boxBorderVisible = boxBorderVisible;

        Text = "Edit OCR/Translation Box";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(640, 920);
        MinimumSize = new Size(560, 770);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 16,
            Padding = new Padding(10),
            BackColor = BackColor
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        _ocrTextBox = CreateTextBox(ocrText);
        _translationTextBox = CreateTextBox(translationText);
        _xBox = CreateNumber(normalizedRect.X);
        _yBox = CreateNumber(normalizedRect.Y);
        _wBox = CreateNumber(normalizedRect.Width);
        _hBox = CreateNumber(normalizedRect.Height);
        _fontBox = CreateNumber(normalizedFontSize, 0m, 0.5m);
        _xSlider = CreateSlider(normalizedRect.X);
        _ySlider = CreateSlider(normalizedRect.Y);
        _wSlider = CreateSlider(normalizedRect.Width);
        _hSlider = CreateSlider(normalizedRect.Height);
        _fontSlider = CreateSlider(normalizedFontSize, 0f, 0.5f);
        _textColorButton = CreateColorButton("Text color", _textColorArgb, ChooseTextColor);
        _alignmentCombo = CreateAlignmentCombo(_textAlignment, vertical: false);
        _verticalAlignmentCombo = CreateAlignmentCombo(_textVerticalAlignment, vertical: true);
        _textOutlineColorButton = CreateColorButton("Text outline", _textOutlineColorArgb, ChooseTextOutlineColor);
        _boxFillColorButton = CreateColorButton("Box fill", _boxFillColorArgb, ChooseBoxFillColor);
        _boxFillOpacityBox = CreateOpacityNumber(_boxFillColorArgb);
        _boxBorderColorButton = CreateColorButton("Box outline", _boxBorderColorArgb, ChooseBoxBorderColor);
        _boxFillVisibleCheck = CreateVisibilityCheck("Fill visible", _boxFillVisible, value =>
        {
            _boxFillVisible = value;
            _styleSettingsChanged = true;
            RaisePreviewChanged();
        });
        _boxBorderVisibleCheck = CreateVisibilityCheck("Outline visible", _boxBorderVisible, value =>
        {
            _boxBorderVisible = value;
            _styleSettingsChanged = true;
            RaisePreviewChanged();
        });
        _textOutlineVisibleCheck = CreateVisibilityCheck("Text outline visible", _textOutlineVisible, value =>
        {
            _textOutlineVisible = value;
            _styleSettingsChanged = true;
            RaisePreviewChanged();
        });
        _boxFillOpacityBox.ValueChanged += (_, _) => UpdateBoxFillOpacityFromControl();

        AddLabel(layout, "OCR text:", 0);
        layout.Controls.Add(_ocrTextBox, 1, 0);
        AddLabel(layout, "Translation:", 1);
        layout.Controls.Add(_translationTextBox, 1, 1);

        AddLabel(layout, "X:", 2);
        layout.Controls.Add(CreateSliderRow(_xBox, _xSlider), 1, 2);
        AddLabel(layout, "Y:", 3);
        layout.Controls.Add(CreateSliderRow(_yBox, _ySlider), 1, 3);
        AddLabel(layout, "Width:", 4);
        layout.Controls.Add(CreateSliderRow(_wBox, _wSlider), 1, 4);
        AddLabel(layout, "Height:", 5);
        layout.Controls.Add(CreateSliderRow(_hBox, _hSlider), 1, 5);
        AddLabel(layout, "Font scale:", 6);
        layout.Controls.Add(CreateSliderRow(_fontBox, _fontSlider), 1, 6);

        AddLabel(layout, "Text color:", 7);
        layout.Controls.Add(_textColorButton, 1, 7);
        AddLabel(layout, "Alignment:", 8);
        layout.Controls.Add(_alignmentCombo, 1, 8);
        AddLabel(layout, "Vertical alignment:", 9);
        layout.Controls.Add(_verticalAlignmentCombo, 1, 9);
        AddLabel(layout, "Text outline:", 10);
        layout.Controls.Add(_textOutlineColorButton, 1, 10);
        AddLabel(layout, "Box fill:", 11);
        layout.Controls.Add(_boxFillColorButton, 1, 11);
        AddLabel(layout, "Fill opacity:", 12);
        layout.Controls.Add(_boxFillOpacityBox, 1, 12);
        AddLabel(layout, "Box outline:", 13);
        layout.Controls.Add(_boxBorderColorButton, 1, 13);
        AddLabel(layout, "Visibility:", 14);
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
        layout.Controls.Add(visibilityPanel, 1, 14);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = BackColor,
            WrapContents = false
        };
        var ok = CreateButton("Save", DialogResult.OK);
        var cancel = CreateButton("Cancel", DialogResult.Cancel);
        var resetStyle = CreateButton("Reset style", DialogResult.None);
        resetStyle.Click += (_, _) => ResetStyle();
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(resetStyle);
        layout.Controls.Add(buttons, 0, 15);
        layout.SetColumnSpan(buttons, 2);

        WirePair(_xBox, _xSlider);
        WirePair(_yBox, _ySlider);
        WirePair(_wBox, _wSlider);
        WirePair(_hBox, _hSlider);
        WirePair(_fontBox, _fontSlider);
        _ocrTextBox.TextChanged += (_, _) => RaisePreviewChanged();
        _translationTextBox.TextChanged += (_, _) => RaisePreviewChanged();

        Controls.Add(layout);
        CancelButton = cancel;
    }

    private void WirePair(NumericUpDown number, TrackBar slider)
    {
        number.ValueChanged += (_, _) =>
        {
            if (_syncing)
                return;

            _syncing = true;
            slider.Value = DecimalToSliderValue(number.Value, slider.Minimum, slider.Maximum);
            _syncing = false;
            RaisePreviewChanged();
        };

        slider.ValueChanged += (_, _) =>
        {
            if (_syncing)
                return;

            _syncing = true;
            number.Value = SliderToDecimalValue(slider.Value, number.Minimum, number.Maximum);
            _syncing = false;
            RaisePreviewChanged();
        };
    }

    private void RaisePreviewChanged()
    {
        if (!_syncing)
            PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    private static readonly Color DefaultTextColor = Color.FromArgb(250, 250, 250);
    private static readonly Color DefaultTextOutlineColor = Color.FromArgb(255, 0, 0, 0);
    private static readonly Color DefaultBoxFillColor = Color.FromArgb(242, 7, 19, 36);
    private static readonly Color DefaultBoxBorderColor = Color.FromArgb(220, 125, 198, 255);

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
                _textVerticalAlignment = value;
            else
                _textAlignment = value;
            _styleSettingsChanged = true;
            RaisePreviewChanged();
        };
        return combo;
    }

    private Button CreateColorButton(string defaultText, int? argb, EventHandler chooseHandler)
    {
        var button = new Button
        {
            Text = defaultText,
            Width = 180,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.Gainsboro,
            TextAlign = ContentAlignment.MiddleCenter
        };
        button.Click += chooseHandler;
        UpdateColorButton(button, argb);
        return button;
    }

    private static CheckBox CreateVisibilityCheck(string text, bool? value, Action<bool> changed)
    {
        var check = new CheckBox
        {
            Text = text,
            AutoSize = true,
            Checked = value != false,
            ForeColor = Color.Gainsboro,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 12, 0)
        };
        check.CheckedChanged += (_, _) => changed(check.Checked);
        return check;
    }

    private void ChooseTextColor(object? sender, EventArgs e)
        => ChooseColor(_textColorButton, ref _textColorArgb, DefaultTextColor);

    private void ChooseTextOutlineColor(object? sender, EventArgs e)
        => ChooseColor(_textOutlineColorButton, ref _textOutlineColorArgb, DefaultTextOutlineColor);

    private void ChooseBoxFillColor(object? sender, EventArgs e)
    {
        ChooseColor(_boxFillColorButton, ref _boxFillColorArgb, DefaultBoxFillColor);
        if (_boxFillColorArgb.HasValue)
            _boxFillOpacityBox.Value = AlphaToPercent(Color.FromArgb(_boxFillColorArgb.Value).A);
    }

    private void ChooseBoxBorderColor(object? sender, EventArgs e)
        => ChooseColor(_boxBorderColorButton, ref _boxBorderColorArgb, DefaultBoxBorderColor);

    private void ChooseColor(Button button, ref int? value, Color fallback)
    {
        Color current = value.HasValue ? Color.FromArgb(value.Value) : fallback;
        using var dialog = new ColorDialog
        {
            AllowFullOpen = true,
            AnyColor = true,
            FullOpen = true,
            Color = current
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        value = Color.FromArgb(
            current.A,
            dialog.Color.R,
            dialog.Color.G,
            dialog.Color.B).ToArgb();
        UpdateColorButton(button, value);
        _styleSettingsChanged = true;
        RaisePreviewChanged();
    }

    private void UpdateBoxFillOpacityFromControl()
    {
        Color current = _boxFillColorArgb.HasValue
            ? Color.FromArgb(_boxFillColorArgb.Value)
            : DefaultBoxFillColor;
        int alpha = PercentToAlpha(_boxFillOpacityBox.Value);
        _boxFillColorArgb = Color.FromArgb(alpha, current.R, current.G, current.B).ToArgb();
        UpdateColorButton(_boxFillColorButton, _boxFillColorArgb);
        _styleSettingsChanged = true;
        RaisePreviewChanged();
    }

    private void ResetStyle()
    {
        _textColorArgb = null;
        _textOutlineColorArgb = null;
        _textAlignment = null;
        _textVerticalAlignment = null;
        _textOutlineVisible = null;
        _boxBorderColorArgb = null;
        _boxFillVisible = null;
        _boxBorderVisible = null;
        _alignmentCombo.SelectedIndex = 0;
        _verticalAlignmentCombo.SelectedIndex = 0;
        _boxFillOpacityBox.Value = AlphaToPercent(DefaultBoxFillColor.A);
        _boxFillColorArgb = null;
        _boxFillVisibleCheck.Checked = true;
        _boxBorderVisibleCheck.Checked = true;
        UpdateColorButton(_textColorButton, null);
        UpdateColorButton(_textOutlineColorButton, null);
        UpdateColorButton(_boxFillColorButton, null);
        UpdateColorButton(_boxBorderColorButton, null);
        _styleSettingsChanged = true;
        RaisePreviewChanged();
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
        // Show the RGB swatch opaquely so white remains visibly white even when the
        // separately controlled fill opacity is below 100%.
        Color swatchColor = Color.FromArgb(255, color.R, color.G, color.B);
        button.Text = color.A < 255
            ? $"Choose color ({AlphaToPercent(color.A)}%)"
            : "Choose color";
        button.BackColor = swatchColor;
        button.ForeColor = swatchColor.GetBrightness() > 0.55f ? Color.Black : Color.White;
    }

    private static int DecimalToSliderValue(decimal value, int minimum, int maximum)
        => Math.Clamp((int)Math.Round(value * SliderScale), minimum, maximum);

    private static decimal SliderToDecimalValue(int value, decimal minimum, decimal maximum)
    {
        decimal scaled = value / (decimal)SliderScale;
        if (scaled < minimum) return minimum;
        if (scaled > maximum) return maximum;
        return Math.Round(scaled, 4);
    }

    private static TextBox CreateTextBox(string value)
        => new()
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            ScrollBars = ScrollBars.Vertical,
            Text = value,
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.Gainsboro,
            BorderStyle = BorderStyle.FixedSingle
        };

    private static NumericUpDown CreateNumber(float value, decimal min = 0m, decimal max = 1m)
        => new()
        {
            Width = 90,
            DecimalPlaces = 4,
            Increment = 0.005m,
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp((decimal)value, min, max),
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.Gainsboro
        };

    private static TrackBar CreateSlider(float value, float min = 0f, float max = 1f)
    {
        int minimum = (int)Math.Round(min * SliderScale);
        int maximum = (int)Math.Round(max * SliderScale);
        return new TrackBar
        {
            Dock = DockStyle.Fill,
            Minimum = minimum,
            Maximum = maximum,
            Value = Math.Clamp((int)Math.Round(value * SliderScale), minimum, maximum),
            TickFrequency = 1000,
            LargeChange = 500,
            SmallChange = 50,
            BackColor = Color.FromArgb(30, 30, 30)
        };
    }

    private static Panel CreateSliderRow(NumericUpDown number, TrackBar slider)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        number.Location = new Point(0, 7);
        number.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        slider.Location = new Point(number.Right + 8, 0);
        slider.Width = Math.Max(80, panel.Width - number.Width - 8);
        slider.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        panel.Resize += (_, _) => slider.Width = Math.Max(80, panel.Width - number.Width - 10);
        panel.Controls.Add(number);
        panel.Controls.Add(slider);
        return panel;
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
            Width = 86,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.Gainsboro
        };
}
