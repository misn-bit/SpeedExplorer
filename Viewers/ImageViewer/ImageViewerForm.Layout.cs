using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    private void UpdateLayoutForDpi()
    {
        if (_controlPanel == null || _titleBar == null)
            return;

        Padding = (WindowState == FormWindowState.Maximized || _isFullscreen)
            ? Padding.Empty
            : WindowFramePadding;
        MinimumSize = new Size(Scale(400), Scale(300));

        _titleBar.Height = TitleBarHeight;
        _titleBar.Padding = Scale(new Padding(8, 0, 0, 0));
        _titleLabel.Height = TitleBarHeight;
        var titleButtonSize = new Size(Scale(46), TitleBarHeight);
        _minimizeBtn.Size = titleButtonSize;
        _maximizeBtn.Size = titleButtonSize;
        _closeBtn.Size = titleButtonSize;
        LayoutTitleBar();

        _controlPanel.Height = ControlPanelHeight;
        _controlPanel.Padding = Scale(new Padding(8, 4, 8, 4));
        _prevBtn.Size = new Size(Scale(50), ControlButtonHeight);
        _nextBtn.Size = new Size(Scale(50), ControlButtonHeight);
        _zoomOutBtn.Size = new Size(Scale(35), ControlButtonHeight);
        _zoomInBtn.Size = new Size(Scale(35), ControlButtonHeight);
        _fitBtn.Size = new Size(Scale(50), ControlButtonHeight);
        _actualBtn.Size = new Size(Scale(50), ControlButtonHeight);
        _rotateBtn.Size = new Size(Scale(38), ControlButtonHeight);
        _fullscreenBtn.Size = new Size(Scale(40), ControlButtonHeight);
        _aiToggleBtn.Size = new Size(Scale(42), ControlButtonHeight);
        _zoomLabel.Size = new Size(Scale(48), ControlButtonHeight);
        _zoomSlider.Width = Scale(150);
        UpdateAiPanelLayoutForDpi();
        foreach (Control tag in _tagsPanel.Controls)
        {
            tag.Padding = Scale(new Padding(5, 1, 5, 1));
            tag.Margin = Scale(new Padding(0, 1, 4, 0));
        }

        LayoutControls();
        if (_autoFitEnabled)
        {
            if (_autoFitBySmallerDimension)
                FitToWindowBySmallerDimension(allowUpscale: false);
            else
                FitToWindow(allowUpscale: false);
        }
        _pictureBox.Invalidate();
    }

    private void UpdateAiPanelLayoutForDpi()
    {
        if (_aiPanel == null || _aiPanel.IsDisposed)
            return;

        _aiPanel.Width = Scale(360);
        _aiPanel.Padding = Scale(new Padding(8));

        _aiActionRow.Height = Scale(30);
        SetAiButtonSize(_ocrBtn, 58);
        SetAiButtonSize(_translateBtn, 82);
        SetAiButtonSize(_drawOcrBoxBtn, 102);
        SetAiButtonSize(_clearManualOcrBoxesBtn, 82);
        SetAiButtonSize(_tagBtn, 58);

        _aiLangRow.Height = Scale(28);
        _aiSourceHintRow.Height = Scale(28);
        _aiOcrHintRow.Height = Scale(46);
        _aiContextHintRow.Height = Scale(46);
        LayoutAiHintRow(_aiLangRow, _targetLanguageLabel, _targetLanguageBox, 248);
        LayoutAiHintRow(_aiSourceHintRow, _sourceLanguageLabel, _sourceLanguageHintBox, 248);
        LayoutAiHintRow(_aiOcrHintRow, _ocrHintLabel, _ocrHintBox, 248, 38);
        LayoutAiHintRow(_aiContextHintRow, _contextHintLabel, _translationContextHintBox, 248, 38);

        _aiManualModeRow.Height = Scale(24);
        _aiReasoningRow.Height = Scale(24);
        _aiToolsRow.Height = Scale(28);
        _aiSavedToggleRow.Height = Scale(24);
        _aiSavedActionRow.Height = Scale(28);
        _manualMaxEffortCheck.Margin = new Padding(0, Scale(3), 0, 0);
        _ocrReasoningCheck.Margin = new Padding(0, Scale(3), Scale(12), 0);
        _translationReasoningCheck.Margin = new Padding(0, Scale(3), 0, 0);
        _overlayToggle.Margin = Scale(new Padding(0, 5, 8, 0));
        _showSavedOcrCheck.Margin = new Padding(0, Scale(3), Scale(10), 0);
        _showSavedTranslationCheck.Margin = new Padding(0, Scale(3), 0, 0);

        SetAiButtonSize(_copyResultBtn, 56);
        SetAiButtonSize(_abortBtn, 56);
        SetAiButtonSize(_cancelCurrentJobBtn, 78);
        SetAiButtonSize(_openSavedOcrFileBtn, 54);
        SetAiButtonSize(_deleteSavedTranslationBtn, 118);
        SetAiButtonSize(_clearOverlayBtn, 104);
        _aiStatusLabel.Height = Scale(20);
        _aiStatusLabel.Padding = new Padding(0, 0, 0, Scale(2));
    }

    private void LayoutAiHintRow(Panel row, Label label, TextBox input, int logicalWidth, int logicalHeight = 0)
    {
        label.Location = new Point(Scale(2), Scale(6));
        input.Location = new Point(Scale(86), Scale(3));
        input.Width = Scale(logicalWidth);
        if (logicalHeight > 0)
            input.Height = Scale(logicalHeight);
        row.PerformLayout();
    }

    private void SetAiButtonSize(Button button, int logicalWidth)
        => button.Size = new Size(Scale(logicalWidth), ControlButtonHeight);

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
