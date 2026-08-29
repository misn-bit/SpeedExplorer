using System;
using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    private void PictureBox_MouseDown(object? sender, MouseEventArgs e)
    {
        FocusViewerForHotkeys();

        if (e.Button == MouseButtons.Left)
        {
            DateTime now = DateTime.UtcNow;
            TimeSpan sinceLastRelease = now - _lastPictureBoxLeftMouseUpUtc;
            bool isSecondClick =
                sinceLastRelease.TotalMilliseconds >= 0 &&
                sinceLastRelease.TotalMilliseconds <= SystemInformation.DoubleClickTime &&
                Math.Abs(e.X - _lastPictureBoxLeftMouseUpPoint.X) <= SystemInformation.DoubleClickSize.Width &&
                Math.Abs(e.Y - _lastPictureBoxLeftMouseUpPoint.Y) <= SystemInformation.DoubleClickSize.Height;
            _pictureBoxSecondClickDownUtc = isSecondClick ? now : DateTime.MinValue;
        }

        if (e.Button == MouseButtons.Right)
        {
            _contextOverlayBlockIndex = HitTestOverlayBlock(e.Location);
            return;
        }

        if (_manualOcrDrawMode && e.Button == MouseButtons.Left)
        {
            if (TryGetCurrentImageDisplayRect(out var imageRect) && imageRect.Contains(e.Location))
            {
                _isDrawingManualOcrRegion = true;
                _manualOcrDragStart = e.Location;
                _manualOcrDragCurrent = e.Location;
                _pictureBox.Cursor = Cursors.Cross;
                _pictureBox.Invalidate();
            }
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            if (!IsCurrentImageActivelyProcessing() &&
                TryHitTestOverlayManipulation(e.Location, out int blockIndex, out var dragMode))
            {
                _overlayDragMode = dragMode;
                _overlayDragBlockIndex = blockIndex;
                _overlayDragImagePath = GetCurrentImagePath();
                _overlayDragStartPoint = e.Location;
                _overlayDragStartRect = _overlayBlocks[blockIndex].NormalizedRect;
                _overlayDragStartHadUserOverride = _overlayBlocks[blockIndex].HasUserOverride;
                _overlayDragChanged = false;
                _pictureBox.Cursor = GetOverlayDragCursor(dragMode);
                return;
            }

            _isPanning = true;
            _lastMousePos = e.Location;
            _pictureBox.Cursor = Cursors.SizeAll;
        }
    }

    private void PictureBox_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_overlayDragMode != OverlayDragMode.None)
        {
            UpdateOverlayDrag(e.Location);
            return;
        }

        if (_isDrawingManualOcrRegion)
        {
            _manualOcrDragCurrent = e.Location;
            _pictureBox.Invalidate();
            return;
        }

        if (_isPanning)
        {
            _panOffset.X += e.X - _lastMousePos.X;
            _panOffset.Y += e.Y - _lastMousePos.Y;
            _lastMousePos = e.Location;
            _pictureBox.Invalidate();
        }
        else if (_manualOcrDrawMode)
        {
            _pictureBox.Cursor = Cursors.Cross;
        }
        else if (!IsCurrentImageActivelyProcessing() &&
            TryHitTestOverlayManipulation(e.Location, out _, out var hoverMode))
        {
            _pictureBox.Cursor = GetOverlayDragCursor(hoverMode);
        }
        else
        {
            _pictureBox.Cursor = Cursors.Default;
        }
    }

    private void PictureBox_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _lastPictureBoxLeftMouseUpUtc = DateTime.UtcNow;
            _lastPictureBoxLeftMouseUpPoint = e.Location;
        }

        if (_overlayDragMode != OverlayDragMode.None && e.Button == MouseButtons.Left)
        {
            bool save = _overlayDragChanged;
            string? dragImagePath = _overlayDragImagePath;
            _overlayDragMode = OverlayDragMode.None;
            _overlayDragChanged = false;
            _pictureBox.Cursor = Cursors.Default;

            if (save && string.Equals(GetCurrentImagePath(), dragImagePath, StringComparison.OrdinalIgnoreCase))
                SaveOverlayBlockDragEdit();

            _overlayDragBlockIndex = -1;
            _overlayDragImagePath = null;
            _overlayDragStartHadUserOverride = false;
            _pictureBox.Invalidate();
            return;
        }

        if (_isDrawingManualOcrRegion && e.Button == MouseButtons.Left)
        {
            _isDrawingManualOcrRegion = false;
            if (TryGetNormalizedManualSelectionRect(_manualOcrDragStart, e.Location, out var normalizedRect))
            {
                _pendingManualOcrRegions.Add(new ManualOcrRegion { NormalizedRect = normalizedRect });
                RefreshAiStatusLabel();
            }

            UpdateManualOcrUiState();
            _pictureBox.Invalidate();
            return;
        }

        _isPanning = false;
        UpdateManualOcrUiState();
    }

    private void PictureBox_MouseDoubleClick(object? sender, MouseEventArgs e)
    {
        DateTime secondClickDown = _pictureBoxSecondClickDownUtc;
        _pictureBoxSecondClickDownUtc = DateTime.MinValue;

        if (e.Button != MouseButtons.Left || secondClickDown == DateTime.MinValue)
            return;

        // MouseDoubleClick is delivered after MouseUp. Do not treat a long-held second
        // press as a double-click just because Windows eventually raised the event.
        if ((DateTime.UtcNow - secondClickDown).TotalMilliseconds <= SystemInformation.DoubleClickTime)
            ToggleFullscreen();
    }

    private void UpdateOverlayDrag(Point currentPoint)
    {
        if (_overlayDragBlockIndex < 0 ||
            _overlayDragBlockIndex >= _overlayBlocks.Count ||
            !string.Equals(GetCurrentImagePath(), _overlayDragImagePath, StringComparison.OrdinalIgnoreCase) ||
            !TryGetCurrentImageDisplayRect(out var imageRect) ||
            imageRect.Width <= 1f ||
            imageRect.Height <= 1f)
        {
            CancelOverlayDrag();
            return;
        }

        float dx = (currentPoint.X - _overlayDragStartPoint.X) / imageRect.Width;
        float dy = (currentPoint.Y - _overlayDragStartPoint.Y) / imageRect.Height;
        var rect = _overlayDragStartRect;
        float minW = Math.Max(0.005f, 8f / imageRect.Width);
        float minH = Math.Max(0.005f, 8f / imageRect.Height);

        switch (_overlayDragMode)
        {
            case OverlayDragMode.Move:
                rect.X = Math.Clamp(rect.X + dx, 0f, Math.Max(0f, 1f - rect.Width));
                rect.Y = Math.Clamp(rect.Y + dy, 0f, Math.Max(0f, 1f - rect.Height));
                break;
            case OverlayDragMode.ResizeLeft:
                rect = ResizeOverlayRect(rect, left: dx, minW: minW, minH: minH);
                break;
            case OverlayDragMode.ResizeRight:
                rect = ResizeOverlayRect(rect, right: dx, minW: minW, minH: minH);
                break;
            case OverlayDragMode.ResizeTop:
                rect = ResizeOverlayRect(rect, top: dy, minW: minW, minH: minH);
                break;
            case OverlayDragMode.ResizeBottom:
                rect = ResizeOverlayRect(rect, bottom: dy, minW: minW, minH: minH);
                break;
            case OverlayDragMode.ResizeTopLeft:
                rect = ResizeOverlayRect(rect, left: dx, top: dy, minW: minW, minH: minH);
                break;
            case OverlayDragMode.ResizeTopRight:
                rect = ResizeOverlayRect(rect, right: dx, top: dy, minW: minW, minH: minH);
                break;
            case OverlayDragMode.ResizeBottomLeft:
                rect = ResizeOverlayRect(rect, left: dx, bottom: dy, minW: minW, minH: minH);
                break;
            case OverlayDragMode.ResizeBottomRight:
                rect = ResizeOverlayRect(rect, right: dx, bottom: dy, minW: minW, minH: minH);
                break;
        }

        rect = ClampNormalizedRect(rect.X, rect.Y, rect.Width, rect.Height);
        if (rect.Width < minW || rect.Height < minH)
            return;

        _overlayBlocks[_overlayDragBlockIndex].NormalizedRect = rect;
        _overlayBlocks[_overlayDragBlockIndex].HasUserOverride = true;
        _overlayDragChanged = true;
        _pictureBox.Invalidate();
    }

    private static RectangleF ResizeOverlayRect(
        RectangleF rect,
        float left = 0f,
        float right = 0f,
        float top = 0f,
        float bottom = 0f,
        float minW = 0.005f,
        float minH = 0.005f)
    {
        float x1 = rect.Left + left;
        float x2 = rect.Right + right;
        float y1 = rect.Top + top;
        float y2 = rect.Bottom + bottom;

        x1 = Math.Clamp(x1, 0f, 1f);
        x2 = Math.Clamp(x2, 0f, 1f);
        y1 = Math.Clamp(y1, 0f, 1f);
        y2 = Math.Clamp(y2, 0f, 1f);

        if (x2 - x1 < minW)
        {
            if (Math.Abs(left) > 0f)
                x1 = Math.Max(0f, x2 - minW);
            else
                x2 = Math.Min(1f, x1 + minW);
        }

        if (y2 - y1 < minH)
        {
            if (Math.Abs(top) > 0f)
                y1 = Math.Max(0f, y2 - minH);
            else
                y2 = Math.Min(1f, y1 + minH);
        }

        return new RectangleF(x1, y1, Math.Max(minW, x2 - x1), Math.Max(minH, y2 - y1));
    }

    private void PictureBox_MouseWheel(object? sender, MouseEventArgs e)
    {
        if (_overlayDragMode != OverlayDragMode.None)
            return;

        if ((ModifierKeys & Keys.Control) == Keys.Control)
        {
            AdjustZoom(e.Delta > 0 ? 0.1f : -0.1f, e.Location);
            return;
        }

        if (e.Delta > 0)
            ShowPrevious();
        else if (e.Delta < 0)
            ShowNext();
    }

    private void AdjustZoom(float delta)
    {
        AdjustZoom(delta, null);
    }

    private void AdjustZoom(float delta, Point? anchorPoint)
    {
        if (_currentImage == null)
            return;

        _autoFitEnabled = false;
        float newZoom = Math.Clamp(_zoomLevel + delta, 0.1f, 5.0f);
        ApplyZoom(newZoom, anchorPoint);
    }

    private void ApplyZoom(float newZoom, Point? anchorPoint)
    {
        if (_currentImage == null)
            return;

        float oldZoom = _zoomLevel;
        if (Math.Abs(newZoom - oldZoom) < 0.0001f)
            return;

        bool keepCentered = IsImageFullyVisibleAtZoom(oldZoom) && IsImageFullyVisibleAtZoom(newZoom);
        _zoomLevel = newZoom;
        SetZoomSliderValue((int)(_zoomLevel * 100));

        if (keepCentered)
        {
            _panOffset = Point.Empty;
            _pictureBox.Invalidate();
            return;
        }

        Point pivot = anchorPoint ?? new Point(_pictureBox.Width / 2, _pictureBox.Height / 2);
        float oldImgW = _currentImage.Width * oldZoom;
        float oldImgH = _currentImage.Height * oldZoom;
        float oldX = (_pictureBox.Width - oldImgW) / 2f + _panOffset.X;
        float oldY = (_pictureBox.Height - oldImgH) / 2f + _panOffset.Y;
        float mouseRelX = pivot.X - oldX;
        float mouseRelY = pivot.Y - oldY;

        float scaleFactor = newZoom / oldZoom;
        float newMouseRelX = mouseRelX * scaleFactor;
        float newMouseRelY = mouseRelY * scaleFactor;
        float expectedNewX = pivot.X - newMouseRelX;
        float expectedNewY = pivot.Y - newMouseRelY;
        float newImgW = _currentImage.Width * newZoom;
        float newImgH = _currentImage.Height * newZoom;
        _panOffset.X = (int)(expectedNewX - (_pictureBox.Width - newImgW) / 2f);
        _panOffset.Y = (int)(expectedNewY - (_pictureBox.Height - newImgH) / 2f);

        if (IsImageFullyVisibleAtZoom(newZoom))
            _panOffset = Point.Empty;

        _pictureBox.Invalidate();
    }

    private bool IsImageFullyVisibleAtZoom(float zoom)
    {
        if (_currentImage == null)
            return false;

        float imgWidth = _currentImage.Width * zoom;
        float imgHeight = _currentImage.Height * zoom;
        return imgWidth <= _pictureBox.Width && imgHeight <= _pictureBox.Height;
    }

    private void FitToWindow(bool allowUpscale = true)
    {
        ApplyFitToWindow(useSmallerDimension: false, allowUpscale);
    }

    private void FitToWindowBySmallerDimension(bool allowUpscale = true)
    {
        ApplyFitToWindow(useSmallerDimension: true, allowUpscale);
    }

    private void ApplyFitToWindow(bool useSmallerDimension, bool allowUpscale)
    {
        if (_currentImage == null)
            return;

        var scaleX = (float)_pictureBox.Width / _currentImage.Width;
        var scaleY = (float)_pictureBox.Height / _currentImage.Height;
        float fitScale = useSmallerDimension ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
        if (!allowUpscale)
            fitScale = Math.Min(1.0f, fitScale);
        _zoomLevel = Math.Clamp(fitScale, 0.1f, 5.0f);
        SetZoomSliderValue((int)(_zoomLevel * 100));
        _panOffset = Point.Empty;
        _pictureBox.Invalidate();
        _autoFitEnabled = true;
        _autoFitBySmallerDimension = useSmallerDimension;
    }

    private void ActualSize()
    {
        _autoFitEnabled = false;
        _autoFitBySmallerDimension = false;
        _zoomLevel = 1.0f;
        SetZoomSliderValue(100);
        _panOffset = Point.Empty;
        _pictureBox.Invalidate();
    }

    private void ShowPrevious()
    {
        if (_overlayDragMode != OverlayDragMode.None)
            return;

        if (_currentIndex > 0)
        {
            _currentIndex--;
            LoadCurrentImage();
        }
    }

    private void ShowNext()
    {
        if (_overlayDragMode != OverlayDragMode.None)
            return;

        if (_currentIndex < _imagePaths.Count - 1)
        {
            _currentIndex++;
            LoadCurrentImage();
        }
    }

    private void ToggleOverlayBoxes()
    {
        _overlayToggle.Checked = !_overlayToggle.Checked;
        if (!_aiBusy)
            _aiStatusLabel.Text = _overlayToggle.Checked ? "OCR boxes shown" : "OCR boxes hidden";
    }

    private void ToggleSavedTranslation()
    {
        if (!_showSavedOcrCheck.Checked)
        {
            if (!_aiBusy)
                _aiStatusLabel.Text = "Enable saved OCR display first";
            return;
        }

        _showSavedTranslationCheck.Checked = !_showSavedTranslationCheck.Checked;
    }

}
