using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    private void LoadCurrentImage()
    {
        if (_currentIndex < 0 || _currentIndex >= _imagePaths.Count) return;

        CancellationTokenSource? previousLoad = _imageLoadCts;
        previousLoad?.Cancel();

        var loadCts = new CancellationTokenSource();
        _imageLoadCts = loadCts;
        int requestId = ++_imageLoadRequestId;
        string path = _imagePaths[_currentIndex];

        CancelOverlayDrag(invalidate: false);
        SetFileNameDisplay(Path.GetFileName(path));
        _indexLabel.Text = $"{_currentIndex + 1} / {_imagePaths.Count}";
        _titleLabel.Text = $"Speed Explorer - {Path.GetFileName(path)}";

        UpdateTags(path);
        EnsureImageFolderWatcher(path);
        UpdateManualOcrUiState();
        UpdateCancelCurrentJobButton();
        UpdateAiActionControlsState();
        _pictureBox.Invalidate();

        _ = LoadImageInBackgroundAsync(path, requestId, loadCts);
    }

    private async Task LoadImageInBackgroundAsync(string path, int requestId, CancellationTokenSource loadCts)
    {
        ImageViewerImageLoadResult? loadedImage = null;
        try
        {
            loadedImage = await Task.Run(
                () => ImageViewerImageLoader.Load(path, loadCts.Token),
                loadCts.Token);

            if (!IsCurrentImageLoad(path, requestId))
            {
                DisposeLoadedImage(loadedImage);
                loadedImage = null;
                return;
            }

            ResetImageAnnotationsIfNeeded(path);
            ReplaceCurrentImage(loadedImage);
            loadedImage = null; // Ownership has moved to the viewer.

            FitToWindow(allowUpscale: false);
            TryApplySavedOcrForCurrentImage(allowStatusUpdate: true);
            UpdateSavedCacheUiState();
            UpdateManualOcrUiState();
            UpdateCancelCurrentJobButton();
            UpdateAiActionControlsState();
            RefreshAiStatusLabel();
            _pictureBox.Invalidate();
        }
        catch (OperationCanceledException)
        {
            DisposeLoadedImage(loadedImage);
        }
        catch (SixLabors.ImageSharp.UnknownImageFormatException)
        {
            DisposeLoadedImage(loadedImage);
            if (IsCurrentImageLoad(path, requestId))
            {
                SetFileNameDisplay("Error: Format not supported");
                _pictureBox.Invalidate();
            }
        }
        catch (Exception ex)
        {
            DisposeLoadedImage(loadedImage);
            if (IsCurrentImageLoad(path, requestId))
            {
                SetFileNameDisplay($"Error: {ex.Message}");
                _pictureBox.Invalidate();
            }
        }
        finally
        {
            if (ReferenceEquals(_imageLoadCts, loadCts))
                _imageLoadCts = null;
            loadCts.Dispose();
        }
    }

    private bool IsCurrentImageLoad(string path, int requestId)
        => !IsDisposed &&
           !Disposing &&
           requestId == _imageLoadRequestId &&
           string.Equals(GetCurrentImagePath(), path, StringComparison.OrdinalIgnoreCase);

    private static void DisposeLoadedImage(ImageViewerImageLoadResult? loadedImage)
    {
        loadedImage?.Animation?.Dispose();
        loadedImage?.Bitmap?.Dispose();
    }

    private void ResetImageAnnotationsIfNeeded(string path)
    {
        _rotationQuarterTurns = 0;
        if (string.Equals(_ocrImagePath, path, StringComparison.OrdinalIgnoreCase))
            return;

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

    private void ReplaceCurrentImage(ImageViewerImageLoadResult loadedImage)
    {
        _animationTimer.Stop();
        _animationClock.Stop();
        _animationFrameDeadlineMs = 0;
        _animationFrameIndex = 0;

        AnimatedImageSequence? oldAnimation = _currentAnimation;
        Image? oldBitmap = oldAnimation == null ? _currentImage : null;

        _currentAnimation = loadedImage.Animation;
        if (loadedImage.IsAnimated)
        {
            _currentImage = _currentAnimation!.GetFrame(_animationFrameIndex);
            StartAnimationIfNeeded();
        }
        else
        {
            _currentImage = loadedImage.Bitmap;
        }

        if (oldAnimation != null)
            _ = Task.Run(oldAnimation.Dispose);
        else if (oldBitmap != null)
            _ = Task.Run(oldBitmap.Dispose);
    }
}
