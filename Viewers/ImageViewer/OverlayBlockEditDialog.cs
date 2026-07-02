using System;
using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

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

    public OverlayBlockEditDialog(
        string ocrText,
        string translationText,
        RectangleF normalizedRect,
        float normalizedFontSize)
    {
        Text = "Edit OCR/Translation Box";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(640, 650);
        MinimumSize = new Size(560, 520);
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
            RowCount = 8,
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

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = BackColor,
            WrapContents = false
        };
        var ok = CreateButton("Save", DialogResult.OK);
        var cancel = CreateButton("Cancel", DialogResult.Cancel);
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 7);
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
