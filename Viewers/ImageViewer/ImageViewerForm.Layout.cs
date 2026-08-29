using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    private void LayoutControls()
    {
        if (_controlPanel == null)
            return;

        int w = _controlPanel.ClientSize.Width;
        int centerButtonY = Math.Max(Scale(1), (_controlPanel.ClientSize.Height - ControlButtonHeight) / 2);
        int sliderHeight = _zoomSlider.PreferredSize.Height;
        int sliderY = ((_controlPanel.ClientSize.Height - sliderHeight) / 2) + ZoomSliderVisualOffsetY;
        sliderY = Math.Clamp(sliderY, Scale(1), Math.Max(Scale(1), _controlPanel.ClientSize.Height - _zoomSlider.Height - Scale(1)));
        int spacing = Scale(6);

        int right = w - Scale(8);

        _aiToggleBtn.Location = new Point(right - _aiToggleBtn.Width, centerButtonY);
        right = _aiToggleBtn.Left - spacing;

        _fullscreenBtn.Location = new Point(right - _fullscreenBtn.Width, centerButtonY);
        right = _fullscreenBtn.Left - spacing;

        _rotateBtn.Location = new Point(right - _rotateBtn.Width, centerButtonY);
        right = _rotateBtn.Left - spacing;

        _actualBtn.Location = new Point(right - _actualBtn.Width, centerButtonY);
        right = _actualBtn.Left - spacing;

        _fitBtn.Location = new Point(right - _fitBtn.Width, centerButtonY);
        right = _fitBtn.Left - spacing;

        _zoomLabel.Location = new Point(right - _zoomLabel.Width, centerButtonY);
        right = _zoomLabel.Left - spacing;

        _zoomInBtn.Location = new Point(right - _zoomInBtn.Width, centerButtonY);
        right = _zoomInBtn.Left - spacing;

        _zoomSlider.Location = new Point(right - _zoomSlider.Width, sliderY);
        right = _zoomSlider.Left - spacing;

        _zoomOutBtn.Location = new Point(right - _zoomOutBtn.Width, centerButtonY);
        right = _zoomOutBtn.Left - spacing;

        _prevBtn.Location = new Point(Scale(8), centerButtonY);
        _nextBtn.Location = new Point(_prevBtn.Right + spacing, centerButtonY);

        int infoX = _nextBtn.Right + Scale(8);
        int infoWidth = Math.Max(Scale(100), right - infoX - Scale(8));
        _infoContainer.Location = new Point(infoX, 0);
        _infoContainer.Size = new Size(infoWidth, _controlPanel.ClientSize.Height);
        LayoutInfoControls();
    }

    private Button CreateButton(string text, int width)
    {
        return new Button
        {
            Text = text,
            Size = new Size(width, ControlButtonHeight),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = ForeColor_Dark,
            Font = new Font("Segoe UI", 8),
            Cursor = Cursors.Hand,
            FlatAppearance = { BorderSize = 0 }
        };
    }

    private void LayoutInfoControls()
    {
        if (_infoContainer.Width <= 0 || _infoContainer.Height <= 0)
            return;

        int left = Scale(8);
        _fileNameLabel.Location = new Point(left, Scale(4));
        _indexLabel.Location = new Point(_fileNameLabel.Right + Scale(8), _fileNameLabel.Top + Scale(1));

        int tagsY = _fileNameLabel.Bottom + Scale(2);
        int tagsHeight = Math.Max(Scale(12), _infoContainer.Height - tagsY - Scale(4));
        _tagsPanel.Location = new Point(left, tagsY);
        _tagsPanel.Size = new Size(Math.Max(Scale(40), _infoContainer.Width - left * 2), tagsHeight);
    }

}
