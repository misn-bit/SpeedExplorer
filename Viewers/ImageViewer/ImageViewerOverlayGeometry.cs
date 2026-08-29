using System;
using System.Drawing;

namespace SpeedExplorer;

internal static class ImageViewerOverlayGeometry
{
    private const float PixelRoundingEpsilon = 0.00001f;

    public static RectangleF ClampNormalizedRect(float x, float y, float w, float h)
    {
        float nx = Math.Clamp(x, 0f, 1f);
        float ny = Math.Clamp(y, 0f, 1f);
        float nw = Math.Clamp(w, 0f, 1f);
        float nh = Math.Clamp(h, 0f, 1f);

        if (nx + nw > 1f)
            nw = 1f - nx;
        if (ny + nh > 1f)
            nh = 1f - ny;

        return new RectangleF(nx, ny, Math.Max(0f, nw), Math.Max(0f, nh));
    }

    public static RectangleF RotateClockwise(RectangleF rect)
        => ClampNormalizedRect(1f - (rect.Y + rect.Height), rect.X, rect.Height, rect.Width);

    public static RectangleF RotateCounterClockwise(RectangleF rect)
        => ClampNormalizedRect(rect.Y, 1f - (rect.X + rect.Width), rect.Height, rect.Width);

    public static RectangleF Unrotate(RectangleF rect, int clockwiseQuarterTurns)
    {
        var result = rect;
        int turns = ((clockwiseQuarterTurns % 4) + 4) % 4;
        for (int i = 0; i < turns; i++)
            result = RotateCounterClockwise(result);
        return result;
    }

    public static Rectangle NormalizeToPixels(RectangleF normalizedRect, Size imageSize)
    {
        int left = Math.Clamp((int)Math.Floor(normalizedRect.X * imageSize.Width + PixelRoundingEpsilon), 0, Math.Max(0, imageSize.Width - 1));
        int top = Math.Clamp((int)Math.Floor(normalizedRect.Y * imageSize.Height + PixelRoundingEpsilon), 0, Math.Max(0, imageSize.Height - 1));
        int right = Math.Clamp((int)Math.Ceiling((normalizedRect.X + normalizedRect.Width) * imageSize.Width - PixelRoundingEpsilon), left + 1, imageSize.Width);
        int bottom = Math.Clamp((int)Math.Ceiling((normalizedRect.Y + normalizedRect.Height) * imageSize.Height - PixelRoundingEpsilon), top + 1, imageSize.Height);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }
}
