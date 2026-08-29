using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    private static RectangleF ClampNormalizedRect(float x, float y, float w, float h)
    {
        float nx = Math.Clamp(x, 0f, 1f);
        float ny = Math.Clamp(y, 0f, 1f);
        float nw = Math.Clamp(w, 0f, 1f);
        float nh = Math.Clamp(h, 0f, 1f);

        if (nx + nw > 1f)
            nw = 1f - nx;
        if (ny + nh > 1f)
            nh = 1f - ny;
        if (nw < 0f) nw = 0f;
        if (nh < 0f) nh = 0f;

        return new RectangleF(nx, ny, nw, nh);
    }

    private static RectangleF RotateNormalizedRectClockwise(RectangleF rect)
        => ClampNormalizedRect(1f - (rect.Y + rect.Height), rect.X, rect.Height, rect.Width);

    private void ApplyCurrentRotationToOverlayBlocks()
    {
        if (_rotationQuarterTurns == 0 || _overlayBlocks.Count == 0)
            return;

        for (int turn = 0; turn < _rotationQuarterTurns; turn++)
        {
            for (int i = 0; i < _overlayBlocks.Count; i++)
                _overlayBlocks[i].NormalizedRect = RotateNormalizedRectClockwise(_overlayBlocks[i].NormalizedRect);
        }
    }

    private void RotateImageClockwise()
    {
        if (_currentImage == null)
            return;

        if (_currentAnimation != null)
        {
            _currentAnimation.RotateClockwise();
            _currentImage = _currentAnimation.GetFrame(_animationFrameIndex);
        }
        else
        {
            _currentImage.RotateFlip(RotateFlipType.Rotate90FlipNone);
        }

        _rotationQuarterTurns = (_rotationQuarterTurns + 1) % 4;
        for (int i = 0; i < _overlayBlocks.Count; i++)
            _overlayBlocks[i].NormalizedRect = RotateNormalizedRectClockwise(_overlayBlocks[i].NormalizedRect);

        if (_autoFitEnabled)
        {
            if (_autoFitBySmallerDimension)
                FitToWindowBySmallerDimension(allowUpscale: false);
            else
                FitToWindow(allowUpscale: false);
        }
        else
        {
            _pictureBox.Invalidate();
        }

        if (!_aiBusy)
            _aiStatusLabel.Text = "Rotated clockwise";
    }

    private void SetOverlayFromOcrResult(LlmImageTextResult ocr, IReadOnlyList<string>? translatedLines)
    {
        _overlayBlocks.Clear();

        bool hasPixelCoordinates = ocr.Blocks.Any(b => b.X > 10.0f || b.Y > 10.0f || b.W > 10.0f || b.H > 10.0f);
        float minX = hasPixelCoordinates ? ocr.Blocks.Min(b => b.X) : 0f;
        float minY = hasPixelCoordinates ? ocr.Blocks.Min(b => b.Y) : 0f;
        float maxRight = hasPixelCoordinates ? ocr.Blocks.Max(b => b.X + b.W) : 1f;
        float maxBottom = hasPixelCoordinates ? ocr.Blocks.Max(b => b.Y + b.H) : 1f;

        float sourceW = _currentImage?.Width ?? 0f;
        float sourceH = _currentImage?.Height ?? 0f;

        float denomW = hasPixelCoordinates ? Math.Max(sourceW, maxRight) : 1f;
        float denomH = hasPixelCoordinates ? Math.Max(sourceH, maxBottom) : 1f;
        if (denomW <= 1f) denomW = Math.Max(1f, maxRight);
        if (denomH <= 1f) denomH = Math.Max(1f, maxBottom);

        // Some models return coordinates in a cropped/top-left canvas; stretch to extents when coverage is clearly compressed.
        float extentW = Math.Max(1f, maxRight - minX);
        float extentH = Math.Max(1f, maxBottom - minY);
        float coverW = sourceW > 1f ? (maxRight / sourceW) : 1f;
        float coverH = sourceH > 1f ? (maxBottom / sourceH) : 1f;
        bool stretchX = hasPixelCoordinates && sourceW > 1f && coverW < 0.90f;
        bool stretchY = hasPixelCoordinates && sourceH > 1f && coverH < 0.90f;

        for (int i = 0; i < ocr.Blocks.Count; i++)
        {
            var block = ocr.Blocks[i];
            float x;
            float y;
            float w;
            float h;

            if (hasPixelCoordinates)
            {
                x = stretchX ? ((block.X - minX) / extentW) : (block.X / denomW);
                y = stretchY ? ((block.Y - minY) / extentH) : (block.Y / denomH);
                w = stretchX ? (block.W / extentW) : (block.W / denomW);
                h = stretchY ? (block.H / extentH) : (block.H / denomH);
            }
            else
            {
                x = block.X;
                y = block.Y;
                w = block.W;
                h = block.H;
            }

            var rect = ClampNormalizedRect(x, y, w, h);
            if (rect.Width <= 0f || rect.Height <= 0f)
                continue;

            float normalizedFontSize = 0f;
            if (block.FontSize > 0f)
            {
                if (hasPixelCoordinates)
                {
                    float fontDenom = stretchY ? extentH : denomH;
                    if (fontDenom > 1f)
                        normalizedFontSize = block.FontSize / fontDenom;
                }
                else if (block.FontSize <= 1f)
                {
                    normalizedFontSize = block.FontSize;
                }
                else if (sourceH > 1f)
                {
                    normalizedFontSize = block.FontSize / sourceH;
                }

                normalizedFontSize = Math.Clamp(normalizedFontSize, 0f, 0.5f);
            }

            string translated = translatedLines != null && i < translatedLines.Count && !string.IsNullOrWhiteSpace(translatedLines[i])
                ? NormalizeOverlayDisplayText(StripOrderedPrefix(translatedLines[i]))
                : NormalizeOverlayDisplayText(block.Text);

            _overlayBlocks.Add(new OverlayTextBlock
            {
                SourceIndex = i,
                SourceText = block.Text,
                DisplayText = translated,
                NormalizedRect = rect,
                NormalizedFontSize = normalizedFontSize
            });
        }

        var reduced = ReduceOverlayBlocksConservatively(_overlayBlocks);
        if (reduced.Count != _overlayBlocks.Count)
        {
            _overlayBlocks.Clear();
            _overlayBlocks.AddRange(reduced);
        }

        ApplyCurrentRotationToOverlayBlocks();

        _pictureBox.Invalidate();
    }

    private void ApplyTranslationsToOverlay(IReadOnlyList<string> translatedLines)
    {
        if (_overlayBlocks.Count == 0)
            return;

        for (int i = 0; i < _overlayBlocks.Count; i++)
        {
            int sourceIndex = _overlayBlocks[i].SourceIndex;
            if (sourceIndex >= 0 && sourceIndex < translatedLines.Count && !string.IsNullOrWhiteSpace(translatedLines[sourceIndex]))
                _overlayBlocks[i].DisplayText = NormalizeOverlayDisplayText(StripOrderedPrefix(translatedLines[sourceIndex]));
        }

        ApplyCachedOverlayOverridesForCurrentImage(invalidate: false);
        _pictureBox.Invalidate();
    }

    private void ApplyCachedOverlayOverridesForCurrentImage(bool invalidate = true)
    {
        string? imagePath = GetCurrentImagePath();
        _currentImageOverlayDefaults = null;
        if (string.IsNullOrWhiteSpace(imagePath) ||
            !TryLoadSavedOcrEnvelope(imagePath, out var envelope) ||
            envelope == null)
        {
            return;
        }

        _currentImageOverlayDefaults = envelope.OverlayDefaults?.Clone();
        if (envelope.OverlayOverrides == null || envelope.OverlayOverrides.Count == 0)
        {
            if (invalidate)
                _pictureBox.Invalidate();
            return;
        }

        bool showingTranslation = _showSavedTranslationCheck.Checked && _savedTranslationForCurrentImage != null;
        foreach (var block in _overlayBlocks)
        {
            var ov = envelope.OverlayOverrides.LastOrDefault(o => o.SourceIndex == block.SourceIndex);
            if (ov == null)
                continue;

            block.HasUserOverride = true;
            if (ov.W > 0f && ov.H > 0f)
                block.NormalizedRect = ClampNormalizedRect(ov.X, ov.Y, ov.W, ov.H);
            if (ov.FontSize > 0f)
                block.NormalizedFontSize = Math.Clamp(ov.FontSize, 0f, 0.5f);
            if (ov.TextColorArgb != null)
                block.TextColorArgb = ov.TextColorArgb;
            if (ov.TextOutlineColorArgb != null)
                block.TextOutlineColorArgb = ov.TextOutlineColorArgb;
            if (ov.TextAlignment != null)
                block.TextAlignment = ov.TextAlignment;
            if (ov.TextVerticalAlignment != null)
                block.TextVerticalAlignment = ov.TextVerticalAlignment;
            if (ov.TextOutlineVisible != null)
                block.TextOutlineVisible = ov.TextOutlineVisible;
            if (ov.BoxFillColorArgb != null)
                block.BoxFillColorArgb = ov.BoxFillColorArgb;
            if (ov.BoxBorderColorArgb != null)
                block.BoxBorderColorArgb = ov.BoxBorderColorArgb;
            if (ov.BoxFillVisible != null)
                block.BoxFillVisible = ov.BoxFillVisible;
            if (ov.BoxBorderVisible != null)
                block.BoxBorderVisible = ov.BoxBorderVisible;
            if (!string.IsNullOrWhiteSpace(ov.Text))
                block.SourceText = ov.Text!;

            string? displayOverride = showingTranslation ? ov.TranslationText : ov.Text;
            if (!string.IsNullOrWhiteSpace(displayOverride))
                block.DisplayText = NormalizeEditedOverlayDisplayText(displayOverride!);
        }

        if (invalidate)
            _pictureBox.Invalidate();
    }

    private OverlayStyleDefaults GetGlobalOverlayDefaults()
        => new()
        {
            TextColorArgb = _settings.ImageViewerOverlayDefaultTextColorArgb,
            TextOutlineColorArgb = _settings.ImageViewerOverlayDefaultTextOutlineColorArgb,
            TextAlignment = ToStringAlignment(_settings.ImageViewerOverlayDefaultTextAlignment),
            TextVerticalAlignment = ToStringAlignment(_settings.ImageViewerOverlayDefaultTextVerticalAlignment),
            TextOutlineVisible = _settings.ImageViewerOverlayDefaultTextOutlineVisible,
            BoxFillColorArgb = _settings.ImageViewerOverlayDefaultBoxFillColorArgb,
            BoxFillVisible = _settings.ImageViewerOverlayDefaultBoxFillVisible,
            BoxBorderColorArgb = _settings.ImageViewerOverlayDefaultBoxBorderColorArgb,
            BoxBorderVisible = _settings.ImageViewerOverlayDefaultBoxBorderVisible
        };

    private OverlayStyleDefaults GetEffectiveOverlayStyle(OverlayTextBlock block)
    {
        OverlayStyleDefaults global = GetGlobalOverlayDefaults();
        OverlayStyleDefaults? image = _currentImageOverlayDefaults;
        return new OverlayStyleDefaults
        {
            TextColorArgb = block.TextColorArgb ?? image?.TextColorArgb ?? global.TextColorArgb,
            TextOutlineColorArgb = block.TextOutlineColorArgb ?? image?.TextOutlineColorArgb ?? global.TextOutlineColorArgb,
            TextAlignment = block.TextAlignment ?? image?.TextAlignment ?? global.TextAlignment,
            TextVerticalAlignment = block.TextVerticalAlignment ?? image?.TextVerticalAlignment ?? global.TextVerticalAlignment,
            TextOutlineVisible = block.TextOutlineVisible ?? image?.TextOutlineVisible ?? global.TextOutlineVisible,
            BoxFillColorArgb = block.BoxFillColorArgb ?? image?.BoxFillColorArgb ?? global.BoxFillColorArgb,
            BoxFillVisible = block.BoxFillVisible ?? image?.BoxFillVisible ?? global.BoxFillVisible,
            BoxBorderColorArgb = block.BoxBorderColorArgb ?? image?.BoxBorderColorArgb ?? global.BoxBorderColorArgb,
            BoxBorderVisible = block.BoxBorderVisible ?? image?.BoxBorderVisible ?? global.BoxBorderVisible
        };
    }

    private static StringAlignment? ToStringAlignment(int? value)
        => value switch
        {
            0 => StringAlignment.Near,
            1 => StringAlignment.Center,
            2 => StringAlignment.Far,
            _ => null
        };

    private static int? FromStringAlignment(StringAlignment? value)
        => value switch
        {
            StringAlignment.Near => 0,
            StringAlignment.Center => 1,
            StringAlignment.Far => 2,
            _ => null
        };

    private static List<OverlayTextBlock> ReduceOverlayBlocksConservatively(List<OverlayTextBlock> input)
    {
        const int maxBlocks = 1000;
        if (input.Count <= 1)
            return input.ToList();

        var output = new List<OverlayTextBlock>(Math.Min(input.Count, maxBlocks));
        for (int i = 0; i < input.Count; i++)
        {
            var candidate = input[i];
            if (string.IsNullOrWhiteSpace(candidate.DisplayText))
                continue;

            float area = Math.Max(0f, candidate.NormalizedRect.Width * candidate.NormalizedRect.Height);
            if (area < 0.000001f)
                continue;

            string candidateNorm = NormalizeOverlayText(string.IsNullOrWhiteSpace(candidate.SourceText) ? candidate.DisplayText : candidate.SourceText);
            bool duplicate = false;
            int start = Math.Max(0, output.Count - 120);
            for (int j = output.Count - 1; j >= start; j--)
            {
                var prior = output[j];
                float overlap = ComputeRectOverlapRatio(candidate.NormalizedRect, prior.NormalizedRect);
                if (overlap < 0.50f)
                    continue;

                string priorNorm = NormalizeOverlayText(string.IsNullOrWhiteSpace(prior.SourceText) ? prior.DisplayText : prior.SourceText);
                bool sameText = candidateNorm.Length > 0 && candidateNorm == priorNorm;
                float candidateArea = Math.Max(0.0000001f, candidate.NormalizedRect.Width * candidate.NormalizedRect.Height);
                float priorArea = Math.Max(0.0000001f, prior.NormalizedRect.Width * prior.NormalizedRect.Height);
                float areaRatio = Math.Min(candidateArea, priorArea) / Math.Max(candidateArea, priorArea);

                if ((sameText && overlap >= 0.55f) || (overlap >= 0.985f && areaRatio >= 0.92f))
                {
                    duplicate = true;
                    break;
                }
            }

            if (duplicate)
                continue;

            output.Add(candidate);
            if (output.Count >= maxBlocks)
                break;
        }

        return output;
    }

    private static string NormalizeOverlayText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var sb = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    private static float ComputeRectOverlapRatio(RectangleF a, RectangleF b)
    {
        float overlapW = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        if (overlapW <= 0f)
            return 0f;

        float overlapH = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        if (overlapH <= 0f)
            return 0f;

        float overlapArea = overlapW * overlapH;
        float minArea = Math.Max(0.0000001f, Math.Min(a.Width * a.Height, b.Width * b.Height));
        return overlapArea / minArea;
    }

    private static string StripOrderedPrefix(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        string trimmed = text.Trim();
        int i = 0;
        while (i < trimmed.Length && char.IsDigit(trimmed[i]))
            i++;

        if (i > 0 && i < trimmed.Length)
        {
            char marker = trimmed[i];
            if (marker == '.' || marker == ')' || marker == ':' || marker == '-')
            {
                if (marker == ':' && i + 1 < trimmed.Length && !char.IsWhiteSpace(trimmed[i + 1]))
                    return trimmed;

                i++;
                while (i < trimmed.Length && char.IsWhiteSpace(trimmed[i]))
                    i++;
                if (i < trimmed.Length)
                    return trimmed.Substring(i);

                // If stripping the prefix leaves nothing, return the original text (e.g. for "1.")
                return trimmed;
            }
        }

        return trimmed;
    }

    private static string NormalizeOverlayDisplayText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        string normalized = DecodeEscapedLineBreaks(text)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Trim();
        if (normalized.IndexOf('\n') < 0)
            return normalized;

        var parts = normalized
            .Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (parts.Count <= 1)
            return parts.Count == 1 ? parts[0] : "";

        bool likelyVertical = IsLikelyVerticalText(parts);
        var sb = new StringBuilder(parts[0]);
        for (int i = 1; i < parts.Count; i++)
        {
            string next = parts[i];
            char prevLast = GetLastNonWhitespace(sb);
            char nextFirst = GetFirstNonWhitespace(next);

            if (prevLast == '-' && char.IsLetterOrDigit(nextFirst))
            {
                if (sb.Length > 0)
                    sb.Length--;
                sb.Append(next);
                continue;
            }

            if (likelyVertical || ShouldJoinWithoutSpace(prevLast, nextFirst))
            {
                sb.Append(next);
            }
            else
            {
                sb.Append(' ');
                sb.Append(next);
            }
        }

        return sb.ToString().Trim();
    }

    private static string NormalizeEditedOverlayDisplayText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        return DecodeEscapedLineBreaks(text)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    private static string DecodeEscapedLineBreaks(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return text
            .Replace("\\r\\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\n", StringComparison.Ordinal);
    }

    private static bool IsLikelyVerticalText(List<string> lines)
    {
        if (lines.Count < 3)
            return false;

        int shortLines = 0;
        int cjkLines = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (line.Length <= 2)
                shortLines++;
            if (line.Any(IsCjkChar))
                cjkLines++;
        }

        return shortLines >= (int)Math.Ceiling(lines.Count * 0.70f) || cjkLines >= (int)Math.Ceiling(lines.Count * 0.70f);
    }

    private static bool ShouldJoinWithoutSpace(char left, char right)
    {
        if (left == '\0' || right == '\0')
            return false;

        if (IsCjkChar(left) || IsCjkChar(right))
            return true;

        if ("([{«“\"'".IndexOf(left) >= 0)
            return true;
        if (")]},.!?:;»”\"'".IndexOf(right) >= 0)
            return true;

        return false;
    }

    private static char GetFirstNonWhitespace(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
                return text[i];
        }
        return '\0';
    }

    private static char GetLastNonWhitespace(StringBuilder text)
    {
        for (int i = text.Length - 1; i >= 0; i--)
        {
            if (!char.IsWhiteSpace(text[i]))
                return text[i];
        }
        return '\0';
    }

    private static bool IsCjkChar(char ch)
    {
        return ch is >= '\u3040' and <= '\u30FF'   // Hiragana + Katakana
            or >= '\u3400' and <= '\u4DBF'         // CJK Extension A
            or >= '\u4E00' and <= '\u9FFF'         // CJK Unified Ideographs
            or >= '\uF900' and <= '\uFAFF'         // CJK Compatibility Ideographs
            or >= '\uAC00' and <= '\uD7AF';        // Hangul syllables
    }

    private static string RenderOcrResult(LlmImageTextResult ocr)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(ocr.DetectedLanguage))
            sb.AppendLine($"Detected language: {ocr.DetectedLanguage}");
        sb.AppendLine($"Blocks: {ocr.Blocks.Count}");
        sb.AppendLine();
        sb.AppendLine("Extracted text:");
        sb.AppendLine(string.IsNullOrWhiteSpace(ocr.FullText) ? "(no text)" : ocr.FullText.Trim());

        if (ocr.Blocks.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Blocks:");
            for (int i = 0; i < ocr.Blocks.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {ocr.Blocks[i].Text}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string RenderTranslatedResult(LlmImageTextResult ocr, LlmTextTranslationResult translation)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Detected language: {(string.IsNullOrWhiteSpace(ocr.DetectedLanguage) ? "unknown" : ocr.DetectedLanguage)}");
        sb.AppendLine($"Target language: {translation.TargetLanguage}");
        sb.AppendLine();
        sb.AppendLine("Translated text:");
        sb.AppendLine(string.IsNullOrWhiteSpace(translation.TranslatedFullText) ? "(empty)" : translation.TranslatedFullText.Trim());

        if (translation.Translations.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Block mapping:");
            int count = Math.Max(ocr.Blocks.Count, translation.Translations.Count);
            for (int i = 0; i < count; i++)
            {
                string src = i < ocr.Blocks.Count ? ocr.Blocks[i].Text : "";
                string dst = i < translation.Translations.Count ? translation.Translations[i] : "";
                sb.AppendLine($"{i + 1}. {src}");
                sb.AppendLine($"   -> {dst}");
            }
        }

        return sb.ToString().TrimEnd();
    }

}
