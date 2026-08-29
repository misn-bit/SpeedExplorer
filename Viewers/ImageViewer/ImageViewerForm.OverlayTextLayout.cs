using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    private static SizeF MeasureTextForOverlay(Graphics g, string text, float fontPx, float maxWidth, StringFormat format)
    {
        using var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
        return MeasureOverlayTextLayout(g, text, font, Math.Max(1f, maxWidth)).Size;
    }

    private static float MeasureLongestTextTokenWidth(Graphics g, string text, float fontPx)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0f;

        using var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
        float width = 0f;
        foreach (var token in text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var size = g.MeasureString(token, font);
            if (size.Width > width)
                width = size.Width;
        }

        return width;
    }

    private static float FitTextFontInsideFixedOverlay(
        Graphics g,
        string text,
        float startFontPx,
        float minFontPx,
        RectangleF textRect,
        StringFormat format)
    {
        if (string.IsNullOrWhiteSpace(text) || textRect.Width <= 1f || textRect.Height <= 1f)
            return Math.Max(1f, minFontPx);

        float fontPx = Math.Max(minFontPx, startFontPx);
        for (int i = 0; i < 80; i++)
        {
            using var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
            var layout = MeasureOverlayTextLayout(g, text, font, textRect.Width);
            SizeF measured = layout.Size;
            if (measured.Height <= textRect.Height + 0.5f &&
                measured.Width <= textRect.Width + 0.5f)
            {
                return fontPx;
            }

            if (fontPx <= minFontPx + 0.01f)
                return minFontPx;

            fontPx = Math.Max(minFontPx, fontPx - 0.75f);
        }

        return fontPx;
    }

    private static void DrawOverlayText(
        Graphics g,
        string text,
        Font font,
        Brush brush,
        RectangleF textRect,
        StringAlignment alignment,
        StringAlignment verticalAlignment,
        bool outlineVisible,
        Color outlineColor)
    {
        var layout = MeasureOverlayTextLayout(g, text, font, textRect.Width);
        if (layout.Lines.Count == 0)
            return;

        var state = g.Save();
        try
        {
            g.SetClip(textRect);
            using var lineFormat = CreateOverlayLineFormat(alignment);
            float extraHeight = Math.Max(0f, textRect.Height - layout.Size.Height);
            float y = verticalAlignment switch
            {
                StringAlignment.Center => textRect.Y + (extraHeight / 2f),
                StringAlignment.Far => textRect.Y + extraHeight,
                _ => textRect.Y
            };
            foreach (string line in layout.Lines)
            {
                if (y > textRect.Bottom)
                    break;

                var lineRect = new RectangleF(textRect.X, y, textRect.Width, layout.LineHeight);
                if (outlineVisible)
                {
                    using var path = new GraphicsPath();
                    path.AddString(line, font.FontFamily, (int)font.Style, font.Size, lineRect, lineFormat);
                    using var outlinePen = new Pen(outlineColor, Math.Max(1f, font.Size * 0.075f))
                    {
                        LineJoin = LineJoin.Round
                    };
                    g.DrawPath(outlinePen, path);
                    g.FillPath(brush, path);
                }
                else
                {
                    g.DrawString(line, font, brush, lineRect, lineFormat);
                }
                y += layout.LineHeight;
            }
        }
        finally
        {
            g.Restore(state);
        }
    }

    private static (List<string> Lines, SizeF Size, float LineHeight) MeasureOverlayTextLayout(
        Graphics g,
        string text,
        Font font,
        float maxWidth)
    {
        var lines = WrapOverlayText(g, text, font, maxWidth);
        float lineHeight = Math.Max(1f, font.GetHeight(g) * 1.05f);
        float width = 0f;
        foreach (string line in lines)
            width = Math.Max(width, MeasureOverlayLineWidth(g, font, line));

        return (lines, new SizeF(width, lines.Count * lineHeight), lineHeight);
    }

    private static List<string> WrapOverlayText(Graphics g, string text, Font font, float maxWidth)
        => ImageViewerOverlayTextWrapper.Wrap(
            text,
            maxWidth,
            line => MeasureOverlayLineWidth(g, font, line));

    private static float MeasureOverlayLineWidth(Graphics g, Font font, string line)
    {
        if (string.IsNullOrEmpty(line))
            return 0f;

        using var format = CreateOverlayLineFormat();
        return g.MeasureString(line, font, PointF.Empty, format).Width;
    }

    private static StringFormat CreateOverlayLineFormat(StringAlignment alignment = StringAlignment.Near)
    {
        var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.Alignment = alignment;
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
        format.Trimming = StringTrimming.None;
        return format;
    }

    private static RectangleF ShiftRectIntoBounds(RectangleF rect, RectangleF bounds)
    {
        float width = Math.Min(rect.Width, bounds.Width);
        float height = Math.Min(rect.Height, bounds.Height);
        float x = rect.X;
        float y = rect.Y;

        if (x < bounds.X)
            x = bounds.X;
        if (y < bounds.Y)
            y = bounds.Y;

        if (x + width > bounds.Right)
            x = bounds.Right - width;
        if (y + height > bounds.Bottom)
            y = bounds.Bottom - height;

        return new RectangleF(x, y, width, height);
    }

    private static RectangleF ResolveOverlayCollision(RectangleF rect, RectangleF bounds, List<RectangleF> placedRects)
    {
        var baseRect = ShiftRectIntoBounds(rect, bounds);
        if (!HasHeavyOverlayOverlap(baseRect, placedRects))
            return baseRect;

        float step = Math.Clamp(Math.Min(baseRect.Width, baseRect.Height) * 0.16f, 6f, 20f);
        var directions = new (float dx, float dy)[]
        {
            (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f),
            (1f, 1f), (-1f, 1f), (1f, -1f), (-1f, -1f),
            (2f, 1f), (-2f, 1f), (2f, -1f), (-2f, -1f),
            (1f, 2f), (-1f, 2f), (1f, -2f), (-1f, -2f)
        };

        RectangleF bestRect = baseRect;
        float bestPenalty = ComputeTotalOverlayOverlapPenalty(baseRect, placedRects);

        for (int ring = 1; ring <= 8; ring++)
        {
            foreach (var (dx, dy) in directions)
            {
                var shifted = new RectangleF(
                    baseRect.X + (dx * step * ring),
                    baseRect.Y + (dy * step * ring),
                    baseRect.Width,
                    baseRect.Height);
                shifted = ShiftRectIntoBounds(shifted, bounds);
                float penalty = ComputeTotalOverlayOverlapPenalty(shifted, placedRects);
                if (penalty < bestPenalty)
                {
                    bestPenalty = penalty;
                    bestRect = shifted;
                }

                if (!HasHeavyOverlayOverlap(shifted, placedRects))
                    return shifted;
            }
        }

        // If we are constrained by image bounds, gradually shrink as a last resort to reduce overlap.
        RectangleF shrinkCandidate = bestRect;
        for (int i = 0; i < 4; i++)
        {
            float newWidth = Math.Max(bounds.Width * 0.04f, shrinkCandidate.Width * 0.92f);
            float newHeight = Math.Max(bounds.Height * 0.04f, shrinkCandidate.Height * 0.92f);
            float cx = shrinkCandidate.X + (shrinkCandidate.Width * 0.5f);
            float cy = shrinkCandidate.Y + (shrinkCandidate.Height * 0.5f);
            var shrunk = new RectangleF(
                cx - (newWidth * 0.5f),
                cy - (newHeight * 0.5f),
                newWidth,
                newHeight);
            shrunk = ShiftRectIntoBounds(shrunk, bounds);

            float penalty = ComputeTotalOverlayOverlapPenalty(shrunk, placedRects);
            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                bestRect = shrunk;
            }

            if (!HasHeavyOverlayOverlap(shrunk, placedRects))
                return shrunk;

            shrinkCandidate = shrunk;
        }

        return bestRect;
    }

    private static bool HasHeavyOverlayOverlap(RectangleF candidate, List<RectangleF> placedRects)
    {
        if (placedRects.Count == 0)
            return false;

        float candidateArea = Math.Max(1f, candidate.Width * candidate.Height);
        int start = Math.Max(0, placedRects.Count - 80);

        for (int i = start; i < placedRects.Count; i++)
        {
            var other = placedRects[i];
            float overlapW = Math.Min(candidate.Right, other.Right) - Math.Max(candidate.Left, other.Left);
            if (overlapW <= 0f)
                continue;

            float overlapH = Math.Min(candidate.Bottom, other.Bottom) - Math.Max(candidate.Top, other.Top);
            if (overlapH <= 0f)
                continue;

            float overlapArea = overlapW * overlapH;
            float otherArea = Math.Max(1f, other.Width * other.Height);
            float overlapRatio = overlapArea / Math.Min(candidateArea, otherArea);
            if (overlapRatio >= 0.34f)
                return true;
        }

        return false;
    }

    private static float ComputeTotalOverlayOverlapPenalty(RectangleF candidate, List<RectangleF> placedRects)
    {
        if (placedRects.Count == 0)
            return 0f;

        float candidateArea = Math.Max(1f, candidate.Width * candidate.Height);
        int start = Math.Max(0, placedRects.Count - 100);
        float total = 0f;

        for (int i = start; i < placedRects.Count; i++)
        {
            var other = placedRects[i];
            float overlapW = Math.Min(candidate.Right, other.Right) - Math.Max(candidate.Left, other.Left);
            if (overlapW <= 0f)
                continue;

            float overlapH = Math.Min(candidate.Bottom, other.Bottom) - Math.Max(candidate.Top, other.Top);
            if (overlapH <= 0f)
                continue;

            float overlapArea = overlapW * overlapH;
            float otherArea = Math.Max(1f, other.Width * other.Height);
            float overlapRatio = overlapArea / Math.Min(candidateArea, otherArea);
            total += overlapRatio * overlapRatio;
        }

        return total;
    }

}
