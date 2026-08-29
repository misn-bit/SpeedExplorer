using System;
using System.Collections.Generic;
using System.IO;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    private void LoadCurrentImage()
    {
        if (_currentIndex < 0 || _currentIndex >= _imagePaths.Count) return;

        CancelOverlayDrag(invalidate: false);
        var path = _imagePaths[_currentIndex];
        _rotationQuarterTurns = 0;
        if (!string.Equals(_ocrImagePath, path, StringComparison.OrdinalIgnoreCase))
        {
            _ocrImagePath = null;
            _lastOcrResult = null;
            _savedTranslationForCurrentImage = null;
            _lastTranslations = new List<string>();
            _currentImageOverlayDefaults = null;
            _overlayBlocks.Clear();
            _pendingManualOcrRegions.Clear();
            _manualOcrDrawMode = false;
            _isDrawingManualOcrRegion = false;
            _aiOutputBox.Clear();
            _currentOverlayFromSavedCache = false;
            RestorePendingManualRegionsForCurrentImage();
            RefreshAiStatusLabel();
        }

        ClearAnimationState();
        try
        {
            var loadedImage = ImageViewerImageLoader.Load(path);
            _currentAnimation = loadedImage.Animation;
            if (loadedImage.IsAnimated)
            {
                _animationFrameIndex = 0;
                _currentImage = _currentAnimation!.GetFrame(_animationFrameIndex);
                StartAnimationIfNeeded();
            }
            else
            {
                _currentImage = loadedImage.Bitmap;
            }

            _fileNameLabel.Text = Path.GetFileName(path);
            _indexLabel.Text = $"{_currentIndex + 1} / {_imagePaths.Count}";
            _titleLabel.Text = $"Speed Explorer - {Path.GetFileName(path)}";

            UpdateTags(path);
            EnsureImageFolderWatcher(path);
            FitToWindow(allowUpscale: false);
            TryApplySavedOcrForCurrentImage(allowStatusUpdate: true);
            UpdateSavedCacheUiState();
            UpdateManualOcrUiState();
            UpdateCancelCurrentJobButton();
            UpdateAiActionControlsState();
            RefreshAiStatusLabel();
        }
        catch (SixLabors.ImageSharp.UnknownImageFormatException)
        {
            _currentImage = null;
            _fileNameLabel.Text = "Error: Format not supported";
        }
        catch (Exception ex)
        {
            _currentImage = null;
            _fileNameLabel.Text = $"Error: {ex.Message}";
        }
        _pictureBox.Invalidate();
        UpdateManualOcrUiState();
        UpdateCancelCurrentJobButton();
        UpdateAiActionControlsState();
    }

}
