using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    private void PictureBox_Paint(object? sender, PaintEventArgs e)
    {
        if (_currentImage == null) return;

        bool isUpscaling = _zoomLevel > 1.0f;
        e.Graphics.InterpolationMode = isUpscaling ? InterpolationMode.HighQualityBilinear : InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = isUpscaling ? PixelOffsetMode.None : PixelOffsetMode.HighQuality;
        e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
        e.Graphics.SmoothingMode = SmoothingMode.HighQuality;

        float imgWidth = _currentImage.Width * _zoomLevel;
        float imgHeight = _currentImage.Height * _zoomLevel;

        float x = (_pictureBox.Width - imgWidth) / 2f + _panOffset.X;
        float y = (_pictureBox.Height - imgHeight) / 2f + _panOffset.Y;

        var imageRect = new RectangleF(x, y, imgWidth, imgHeight);
        e.Graphics.DrawImage(_currentImage, imageRect);
        DrawOverlayBlocks(e.Graphics, imageRect);
        DrawPendingManualOcrRegions(e.Graphics, imageRect);
    }

    private void DrawOverlayBlocks(Graphics g, RectangleF imageRect)
    {
        if (!_overlayToggle.Checked || _overlayBlocks.Count == 0)
            return;

        var priorHint = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        try
        {
            using var badgeBrush = new SolidBrush(Color.FromArgb(220, 20, 20, 20));
            using var badgeBorder = new Pen(Color.FromArgb(220, 125, 198, 255), 1f);
            float badgeFontPx = Math.Clamp(9f * _zoomLevel, 8f, 16f);
            using var badgeFont = new Font("Segoe UI", badgeFontPx, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textFormat = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Near,
                Trimming = StringTrimming.Word
            };

            const float textInsetX = 4f;
            const float textInsetY = 3f;
            const float minTextFontPx = 8f;
            const float minExactTextFontPx = 4f;
            const float maxTextFontPx = 34f;
            const float modelFontScale = 1.25f;
            const float maxGrowWidthFactor = 2.40f;
            const float maxGrowHeightFactor = 5.00f;
            int maxShrinkSteps = _overlayBlocks.Count > 120 ? 16 : 52;
            int maxWidenSteps = _overlayBlocks.Count > 120 ? 5 : 12;
            int maxFinalShrinkSteps = _overlayBlocks.Count > 120 ? 12 : 36;
            var placedRects = new List<RectangleF>(_overlayBlocks.Count);

            for (int i = 0; i < _overlayBlocks.Count; i++)
            {
                var block = _overlayBlocks[i];
                OverlayStyleDefaults style = GetEffectiveOverlayStyle(block);
                bool exactBox = true;
                float x = imageRect.X + (block.NormalizedRect.X * imageRect.Width);
                float y = imageRect.Y + (block.NormalizedRect.Y * imageRect.Height);
                float w = block.NormalizedRect.Width * imageRect.Width;
                float h = block.NormalizedRect.Height * imageRect.Height;

                if (w < 2f || h < 2f)
                    continue;

                var rect = new RectangleF(x, y, w, h);
                var drawRect = rect;
                string? text = string.IsNullOrWhiteSpace(block.DisplayText) ? null : block.DisplayText.Trim();
                RectangleF textRect = RectangleF.Empty;
                float textFontPx = minTextFontPx;

                if (!string.IsNullOrWhiteSpace(text))
                {
                    textRect = RectangleF.Inflate(rect, -textInsetX, -textInsetY);
                    if (textRect.Width >= 8f && textRect.Height >= 8f)
                    {
                        float modelFontPx = block.NormalizedFontSize > 0f ? block.NormalizedFontSize * imageRect.Height : 0f;
                        float autoFontPx = textRect.Height * 0.42f;
                        float baseFont = modelFontPx > 0f
                            ? Math.Clamp(modelFontPx * modelFontScale, minTextFontPx, maxTextFontPx)
                            : Math.Clamp(autoFontPx, minTextFontPx, maxTextFontPx);
                        textFontPx = Math.Min(baseFont, Math.Max(minTextFontPx, textRect.Height * 0.80f));

                        if (exactBox)
                        {
                            textFontPx = FitTextFontInsideFixedOverlay(
                                g,
                                text,
                                textFontPx,
                                minExactTextFontPx,
                                textRect,
                                textFormat);
                        }
                        else
                        {
                            float readableWidth = MeasureLongestTextTokenWidth(g, text, textFontPx) + 4f;
                            if (readableWidth > textRect.Width)
                            {
                                float maxReadableWidth = Math.Max(
                                    textRect.Width,
                                    Math.Min(imageRect.Right - textRect.X - 1f, rect.Width * maxGrowWidthFactor));
                                textRect.Width = Math.Min(readableWidth, maxReadableWidth);
                                drawRect = RectangleF.Inflate(textRect, textInsetX, textInsetY);
                            }

                            SizeF measured = MeasureTextForOverlay(g, text, textFontPx, textRect.Width, textFormat);

                            // If OCR gave a huge source box relative to text, shrink box to content first.
                            float compactTextW = Math.Clamp(measured.Width + 4f, 8f, textRect.Width);
                            float compactTextH = Math.Clamp(measured.Height + 4f, 8f, textRect.Height);
                            bool sourceBoxTooWide = textRect.Width > compactTextW * 1.35f;
                            bool sourceBoxTooTall = textRect.Height > compactTextH * 1.50f;
                            if (sourceBoxTooWide || sourceBoxTooTall)
                            {
                                textRect = new RectangleF(
                                    textRect.X,
                                    textRect.Y,
                                    sourceBoxTooWide ? compactTextW : textRect.Width,
                                    sourceBoxTooTall ? compactTextH : textRect.Height);
                                drawRect = RectangleF.Inflate(textRect, textInsetX, textInsetY);
                                measured = MeasureTextForOverlay(g, text, textFontPx, textRect.Width, textFormat);
                            }

                            // First shrink text toward min size.
                            int shrinkSteps = 0;
                            while (measured.Height > textRect.Height && textFontPx > minTextFontPx + 0.01f && shrinkSteps < maxShrinkSteps)
                            {
                                textFontPx = Math.Max(minTextFontPx, textFontPx - 0.75f);
                                measured = MeasureTextForOverlay(g, text, textFontPx, textRect.Width, textFormat);
                                shrinkSteps++;
                            }

                            // If still overflowing, widen text area to reduce wrapping.
                            float sourceTextWidth = Math.Max(8f, rect.Width - (textInsetX * 2f));
                            float maxTextWidth = Math.Min(
                                imageRect.Right - (textRect.X + 1f),
                                Math.Max(textRect.Width, sourceTextWidth * maxGrowWidthFactor));
                            int widenSteps = 0;
                            while (measured.Height > textRect.Height && textRect.Width < maxTextWidth - 0.5f && widenSteps < maxWidenSteps)
                            {
                                textRect.Width = Math.Min(maxTextWidth, textRect.Width * 1.20f);
                                measured = MeasureTextForOverlay(g, text, textFontPx, textRect.Width, textFormat);
                                widenSteps++;
                            }

                            // If text still does not fit, expand the box height.
                            if (measured.Height > textRect.Height)
                            {
                                float sourceTextHeight = Math.Max(8f, rect.Height - (textInsetY * 2f));
                                float maxTextHeight = Math.Min(
                                    imageRect.Bottom - (textRect.Y + 1f),
                                    Math.Max(textRect.Height, sourceTextHeight * maxGrowHeightFactor));
                                textRect.Height = Math.Min(maxTextHeight, measured.Height + 2f);
                            }

                            var desiredDrawRect = RectangleF.Union(drawRect, RectangleF.Inflate(textRect, textInsetX, textInsetY));
                            drawRect = ShiftRectIntoBounds(desiredDrawRect, imageRect);
                            textRect = RectangleF.Inflate(drawRect, -textInsetX, -textInsetY);

                            // One more safety pass after potential clamping/shift.
                            measured = MeasureTextForOverlay(g, text, textFontPx, Math.Max(1f, textRect.Width), textFormat);
                            int finalShrinkSteps = 0;
                            while (measured.Height > textRect.Height && textFontPx > minTextFontPx + 0.01f && finalShrinkSteps < maxFinalShrinkSteps)
                            {
                                textFontPx = Math.Max(minTextFontPx, textFontPx - 0.75f);
                                measured = MeasureTextForOverlay(g, text, textFontPx, Math.Max(1f, textRect.Width), textFormat);
                                finalShrinkSteps++;
                            }

                            if (measured.Height > textRect.Height)
                            {
                                float availableTextHeight = Math.Max(8f, imageRect.Bottom - textRect.Y - 1f);
                                textRect.Height = Math.Min(availableTextHeight, measured.Height + 2f);
                                var expandedRect = RectangleF.Inflate(textRect, textInsetX, textInsetY);
                                drawRect = ShiftRectIntoBounds(expandedRect, imageRect);
                                textRect = RectangleF.Inflate(drawRect, -textInsetX, -textInsetY);
                            }
                        }
                    }
                    else
                    {
                        text = null;
                    }
                }

                drawRect = exactBox
                    ? ShiftRectIntoBounds(drawRect, imageRect)
                    : ResolveOverlayCollision(drawRect, imageRect, placedRects);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    textRect = RectangleF.Inflate(drawRect, -textInsetX, -textInsetY);
                }

                Color fillColor = style.BoxFillColorArgb.HasValue
                    ? Color.FromArgb(style.BoxFillColorArgb.Value)
                    : DefaultOverlayFillColor;
                Color borderColor = style.BoxBorderColorArgb.HasValue
                    ? Color.FromArgb(style.BoxBorderColorArgb.Value)
                    : DefaultOverlayBorderColor;
                Color textColor = style.TextColorArgb.HasValue
                    ? Color.FromArgb(style.TextColorArgb.Value)
                    : DefaultOverlayTextColor;
                Color textOutlineColor = style.TextOutlineColorArgb.HasValue
                    ? Color.FromArgb(style.TextOutlineColorArgb.Value)
                    : DefaultOverlayTextOutlineColor;
                using var fillBrush = style.BoxFillVisible == false ? null : new SolidBrush(fillColor);
                using var borderPen = style.BoxBorderVisible == false ? null : new Pen(borderColor, 1.2f);
                using var textBrush = new SolidBrush(textColor);

                if (fillBrush != null)
                    g.FillRectangle(fillBrush, drawRect);
                if (borderPen != null)
                    g.DrawRectangle(borderPen, drawRect.X, drawRect.Y, drawRect.Width, drawRect.Height);

                if (IsCurrentImageOverlayJobPending())
                {
                    string badgeText = (i + 1).ToString();
                    var badgeSize = g.MeasureString(badgeText, badgeFont);
                    var badgeRect = new RectangleF(
                        drawRect.X,
                        Math.Max(imageRect.Top, drawRect.Y - badgeSize.Height - 2f),
                        badgeSize.Width + 6f,
                        badgeSize.Height + 2f);

                    g.FillRectangle(badgeBrush, badgeRect);
                    g.DrawRectangle(badgeBorder, badgeRect.X, badgeRect.Y, badgeRect.Width, badgeRect.Height);
                    g.DrawString(badgeText, badgeFont, textBrush, badgeRect.X + 3f, badgeRect.Y + 1f);
                }

                if (!string.IsNullOrWhiteSpace(text) && textRect.Width > 4f && textRect.Height > 4f)
                {
                    using var textFont = new Font("Segoe UI", textFontPx, FontStyle.Bold, GraphicsUnit.Pixel);
                    DrawOverlayText(
                        g,
                        text,
                        textFont,
                        textBrush,
                        textRect,
                        style.TextAlignment ?? StringAlignment.Near,
                        style.TextVerticalAlignment ?? StringAlignment.Near,
                        style.TextOutlineVisible == true,
                        textOutlineColor);
                }

                placedRects.Add(drawRect);
            }
        }
        finally
        {
            g.TextRenderingHint = priorHint;
        }
    }

    private void DrawPendingManualOcrRegions(Graphics g, RectangleF imageRect)
    {
        string? imagePath = GetCurrentImagePath();
        List<RectangleF>? queuedRegions = null;
        bool hasQueuedRegions =
            !string.IsNullOrWhiteSpace(imagePath) &&
            _queuedManualRegionsByImage.TryGetValue(imagePath, out queuedRegions) &&
            queuedRegions.Count > 0;

        if (_pendingManualOcrRegions.Count == 0 && !_isDrawingManualOcrRegion && !hasQueuedRegions)
            return;

        using var pendingFill = new SolidBrush(Color.FromArgb(90, 76, 29, 149));
        using var pendingBorder = new Pen(Color.FromArgb(240, 193, 155, 255), 1.4f)
        {
            DashStyle = DashStyle.Dash
        };
        using var queuedFill = new SolidBrush(Color.FromArgb(70, 204, 133, 32));
        using var queuedBorder = new Pen(Color.FromArgb(240, 255, 196, 92), 1.4f)
        {
            DashStyle = DashStyle.Dash
        };
        using var previewFill = new SolidBrush(Color.FromArgb(75, 255, 255, 255));
        using var labelBrush = new SolidBrush(Color.FromArgb(230, 20, 20, 20));
        using var labelTextBrush = new SolidBrush(Color.White);
        using var labelFont = new Font("Segoe UI", Math.Clamp(9f * _zoomLevel, 8f, 16f), FontStyle.Bold, GraphicsUnit.Pixel);

        for (int i = 0; i < _pendingManualOcrRegions.Count; i++)
        {
            var region = _pendingManualOcrRegions[i];
            var rect = new RectangleF(
                imageRect.X + (region.NormalizedRect.X * imageRect.Width),
                imageRect.Y + (region.NormalizedRect.Y * imageRect.Height),
                region.NormalizedRect.Width * imageRect.Width,
                region.NormalizedRect.Height * imageRect.Height);

            if (rect.Width < 2f || rect.Height < 2f)
                continue;

            g.FillRectangle(pendingFill, rect);
            g.DrawRectangle(pendingBorder, rect.X, rect.Y, rect.Width, rect.Height);

            string label = $"Manual {i + 1}";
            var labelSize = g.MeasureString(label, labelFont);
            var labelRect = new RectangleF(
                rect.X,
                Math.Max(imageRect.Y, rect.Y - labelSize.Height - 4f),
                labelSize.Width + 8f,
                labelSize.Height + 2f);
            g.FillRectangle(labelBrush, labelRect);
            g.DrawString(label, labelFont, labelTextBrush, labelRect.X + 4f, labelRect.Y + 1f);
        }

        if (hasQueuedRegions && queuedRegions != null)
        {
            for (int i = 0; i < queuedRegions.Count; i++)
            {
                var region = queuedRegions[i];
                var rect = new RectangleF(
                    imageRect.X + (region.X * imageRect.Width),
                    imageRect.Y + (region.Y * imageRect.Height),
                    region.Width * imageRect.Width,
                    region.Height * imageRect.Height);

                if (rect.Width < 2f || rect.Height < 2f)
                    continue;

                g.FillRectangle(queuedFill, rect);
                g.DrawRectangle(queuedBorder, rect.X, rect.Y, rect.Width, rect.Height);

                string label = $"Queued {i + 1}";
                var labelSize = g.MeasureString(label, labelFont);
                var labelRect = new RectangleF(
                    rect.X,
                    Math.Max(imageRect.Y, rect.Y - labelSize.Height - 4f),
                    labelSize.Width + 8f,
                    labelSize.Height + 2f);
                g.FillRectangle(labelBrush, labelRect);
                g.DrawString(label, labelFont, labelTextBrush, labelRect.X + 4f, labelRect.Y + 1f);
            }
        }

        if (_isDrawingManualOcrRegion &&
            TryGetNormalizedManualSelectionRect(_manualOcrDragStart, _manualOcrDragCurrent, out var dragRect))
        {
            var rect = new RectangleF(
                imageRect.X + (dragRect.X * imageRect.Width),
                imageRect.Y + (dragRect.Y * imageRect.Height),
                dragRect.Width * imageRect.Width,
                dragRect.Height * imageRect.Height);
            g.FillRectangle(previewFill, rect);
            g.DrawRectangle(pendingBorder, rect.X, rect.Y, rect.Width, rect.Height);
        }
    }

}
