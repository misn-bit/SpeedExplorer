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
        int gap = Scale(8);

        Size indexSize = TextRenderer.MeasureText(_indexLabel.Text, _indexLabel.Font);
        int maxNameWidth = _infoContainer.Width - left * 2 - gap - indexSize.Width;
        string displayName = Ellipsize(_fileNameFullText, _fileNameLabel.Font, maxNameWidth);
        if (_fileNameLabel.Text != displayName)
            _fileNameLabel.Text = displayName;

        Size nameSize = TextRenderer.MeasureText(displayName, _fileNameLabel.Font);
        int nameWidth = Math.Max(Scale(10), nameSize.Width);
        int rowHeight = Math.Max(nameSize.Height, indexSize.Height);

        _fileNameLabel.Size = new Size(nameWidth, rowHeight);
        _fileNameLabel.Location = new Point(left, Scale(2));
        _indexLabel.Size = new Size(indexSize.Width, rowHeight);
        _indexLabel.Location = new Point(left + nameWidth + gap, _fileNameLabel.Top + Scale(1));

        int tagsY = _fileNameLabel.Bottom + Scale(1);
        int tagsHeight = Math.Max(Scale(12), _infoContainer.Height - tagsY - Scale(2));
        _tagsPanel.Location = new Point(left, tagsY);
        _tagsPanel.Size = new Size(Math.Max(Scale(40), _infoContainer.Width - left * 2), tagsHeight);
    }

    private void SetFileNameDisplay(string name)
    {
        _fileNameFullText = name;
        _nameTooltip.SetToolTip(_fileNameLabel, name);
        _fileNameLabel.Text = name;
    }

    private static string Ellipsize(string text, Font font, int maxWidth)
    {
        if (string.IsNullOrEmpty(text) || TextRenderer.MeasureText(text, font).Width <= maxWidth)
            return text;

        const string ellipsis = "…";
        int ellipsisWidth = TextRenderer.MeasureText(ellipsis, font).Width;
        if (maxWidth <= ellipsisWidth)
            return string.Empty;

        int low = 0;
        int high = text.Length;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (TextRenderer.MeasureText(text.Substring(0, mid) + ellipsis, font).Width <= maxWidth)
                low = mid;
            else
                high = mid - 1;
        }

        return text.Substring(0, low) + ellipsis;
    }

}
