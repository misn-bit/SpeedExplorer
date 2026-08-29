using System;
using System.Collections.Generic;
using System.Drawing;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    private bool TryGetCurrentImageDisplayRect(out RectangleF imageRect)
    {
        imageRect = RectangleF.Empty;
        if (_currentImage == null)
            return false;

        float imgWidth = _currentImage.Width * _zoomLevel;
        float imgHeight = _currentImage.Height * _zoomLevel;
        float x = (_pictureBox.Width - imgWidth) / 2f + _panOffset.X;
        float y = (_pictureBox.Height - imgHeight) / 2f + _panOffset.Y;
        imageRect = new RectangleF(x, y, imgWidth, imgHeight);
        return imageRect.Width > 0f && imageRect.Height > 0f;
    }

    private bool TryGetNormalizedManualSelectionRect(Point start, Point end, out RectangleF normalizedRect)
    {
        normalizedRect = RectangleF.Empty;
        if (!TryGetCurrentImageDisplayRect(out var imageRect))
            return false;

        float left = Math.Min(start.X, end.X);
        float top = Math.Min(start.Y, end.Y);
        float right = Math.Max(start.X, end.X);
        float bottom = Math.Max(start.Y, end.Y);
        var selection = RectangleF.FromLTRB(left, top, right, bottom);
        var clipped = RectangleF.Intersect(selection, imageRect);
        if (clipped.Width < 4f || clipped.Height < 4f)
            return false;

        normalizedRect = ClampNormalizedRect(
            (clipped.X - imageRect.X) / imageRect.Width,
            (clipped.Y - imageRect.Y) / imageRect.Height,
            clipped.Width / imageRect.Width,
            clipped.Height / imageRect.Height);
        return normalizedRect.Width > 0.0025f && normalizedRect.Height > 0.0025f;
    }

    private static RectangleF RotateNormalizedRectCounterClockwise(RectangleF rect)
        => ImageViewerOverlayGeometry.RotateCounterClockwise(rect);

    private static RectangleF UnrotateNormalizedRect(RectangleF rect, int clockwiseQuarterTurns)
        => ImageViewerOverlayGeometry.Unrotate(rect, clockwiseQuarterTurns);

    private static Rectangle NormalizeRectToPixels(RectangleF normalizedRect, Size imageSize)
        => ImageViewerOverlayGeometry.NormalizeToPixels(normalizedRect, imageSize);

    private List<OverlayTextBlock> BuildPendingManualOverlayBlocks()
    {
        var blocks = new List<OverlayTextBlock>(_pendingManualOcrRegions.Count);
        int sourceIndexBase = _overlayBlocks.Count;
        for (int i = 0; i < _pendingManualOcrRegions.Count; i++)
        {
            blocks.Add(new OverlayTextBlock
            {
                SourceIndex = sourceIndexBase + i,
                SourceText = "",
                DisplayText = "Manual OCR",
                NormalizedRect = _pendingManualOcrRegions[i].NormalizedRect,
                NormalizedFontSize = 0f,
                IsManualBox = true,
                IsPendingManualBox = true
            });
        }
        return blocks;
    }

}
