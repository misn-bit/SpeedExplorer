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
        => ClampNormalizedRect(rect.Y, 1f - (rect.X + rect.Width), rect.Height, rect.Width);

    private static RectangleF UnrotateNormalizedRect(RectangleF rect, int clockwiseQuarterTurns)
    {
        var result = rect;
        int turns = ((clockwiseQuarterTurns % 4) + 4) % 4;
        for (int i = 0; i < turns; i++)
            result = RotateNormalizedRectCounterClockwise(result);
        return result;
    }

    private static Rectangle NormalizeRectToPixels(RectangleF normalizedRect, Size imageSize)
    {
        int left = Math.Clamp((int)Math.Floor(normalizedRect.X * imageSize.Width), 0, Math.Max(0, imageSize.Width - 1));
        int top = Math.Clamp((int)Math.Floor(normalizedRect.Y * imageSize.Height), 0, Math.Max(0, imageSize.Height - 1));
        int right = Math.Clamp((int)Math.Ceiling((normalizedRect.X + normalizedRect.Width) * imageSize.Width), left + 1, imageSize.Width);
        int bottom = Math.Clamp((int)Math.Ceiling((normalizedRect.Y + normalizedRect.Height) * imageSize.Height), top + 1, imageSize.Height);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

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
