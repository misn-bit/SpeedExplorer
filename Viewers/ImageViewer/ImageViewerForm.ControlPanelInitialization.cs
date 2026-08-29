using System;
using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    private void InitializeControlPanel()
    {
        _controlPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = ControlPanelHeight,
            BackColor = ControlPanelColor,
            Padding = Scale(new Padding(8, 6, 8, 6))
        };

        // Navigation
        _prevBtn = CreateButton("◀", Scale(50));
        _prevBtn.Click += (s, e) => ShowPrevious();
        _prevBtn.Location = new Point(Scale(8), Scale(15));

        _nextBtn = CreateButton("▶", Scale(50));
        _nextBtn.Click += (s, e) => ShowNext();
        _nextBtn.Location = new Point(Scale(64), Scale(15));

        // Info container to handle layout better
        _infoContainer = new Panel
        {
            Location = new Point(Scale(120), 0),
            Height = ControlPanelHeight,
            Width = Scale(400),
            BackColor = Color.Transparent
        };
        _infoContainer.Resize += (s, e) => LayoutInfoControls();

        _fileNameLabel = new Label
        {
            AutoSize = true,
            Location = new Point(Scale(8), Scale(4)),
            ForeColor = ForeColor_Dark,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _tagsPanel = new FlowLayoutPanel
        {
            Location = new Point(Scale(8), Scale(24)),
            Size = new Size(Scale(380), Scale(18)),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };

        _indexLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _fileNameLabel.TextChanged += (s, e) => LayoutInfoControls();

        _infoContainer.Controls.Add(_fileNameLabel);
        _infoContainer.Controls.Add(_indexLabel);
        _infoContainer.Controls.Add(_tagsPanel);

        // Zoom
        _zoomOutBtn = CreateButton("−", Scale(35));
        _zoomOutBtn.Click += (s, e) => AdjustZoom(-0.1f);

        _zoomLabel = new Label { Text = "100%", AutoSize = false, Size = new Size(Scale(48), ControlButtonHeight), ForeColor = ForeColor_Dark, Font = new Font("Segoe UI", 8), TextAlign = ContentAlignment.MiddleCenter };

        _zoomSlider = new TrackBar { Minimum = 10, Maximum = 500, Value = 100, Width = Scale(150), TickStyle = TickStyle.None, AutoSize = true, TickFrequency = 50, BackColor = ControlPanelColor };
        _zoomSlider.ValueChanged += (s, e) =>
        {
            if (_suppressZoomSliderEvent)
                return;
            _autoFitEnabled = false;
            _zoomLevel = _zoomSlider.Value / 100f;
            _zoomLabel.Text = $"{_zoomSlider.Value}%";
            if (IsImageFullyVisibleAtZoom(_zoomLevel))
                _panOffset = Point.Empty;
            _pictureBox.Invalidate();
        };

        _zoomInBtn = CreateButton("+", Scale(35));
        _zoomInBtn.Click += (s, e) => AdjustZoom(0.1f);

        _fitBtn = CreateButton("Fit", Scale(50));
        _fitBtn.Click += (s, e) => FitToWindow();

        _actualBtn = CreateButton("1:1", Scale(50));
        _actualBtn.Click += (s, e) => ActualSize();

        _rotateBtn = CreateButton("↻", Scale(38));
        _rotateBtn.Click += (s, e) => RotateImageClockwise();

        _fullscreenBtn = CreateButton("⛶", Scale(40));
        _fullscreenBtn.Click += (s, e) => ToggleFullscreen();

        _aiToggleBtn = CreateButton("AI", Scale(42));
        _aiToggleBtn.Click += (s, e) => ToggleAiPanel();

    }
}
