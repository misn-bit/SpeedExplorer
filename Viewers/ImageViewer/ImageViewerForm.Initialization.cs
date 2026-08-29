using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    public ImageViewerForm(List<string> imagePaths, int startIndex, ImageViewerSortOptions? sortOptions = null)
    {
        _imagePaths = imagePaths;
        _sortOptions = sortOptions;
        _currentIndex = Math.Clamp(startIndex, 0, imagePaths.Count - 1);
        _animationTimer = new System.Windows.Forms.Timer();
        _animationTimer.Tick += AnimationTimer_Tick;
        _imageFolderRefreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _imageFolderRefreshTimer.Tick += ImageFolderRefreshTimer_Tick;

        InitializeViewerChrome();

        InitializeAiPanel();

        InitializeControlPanel();

        // Add controls
        _controlPanel.Controls.AddRange(new Control[] { _prevBtn, _nextBtn, _infoContainer, _zoomOutBtn, _zoomSlider, _zoomInBtn, _zoomLabel, _fitBtn, _actualBtn, _rotateBtn, _fullscreenBtn, _aiToggleBtn });
        _controlPanel.Resize += (s, e) => LayoutControls();

        Controls.Add(_contentPanel);
        Controls.Add(_controlPanel);
        Controls.Add(_titleBar);

        // Custom Paint for Border
        Paint += (s, e) =>
        {
            if (_isFullscreen)
                return;

            using var p = new Pen(Color.FromArgb(60, 60, 60));
            e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        };

        LoadCurrentImage();
        ApplyAiPanelToggleVisualState();
        LayoutControls();
        ApplySavedWindowState();
    }

}
