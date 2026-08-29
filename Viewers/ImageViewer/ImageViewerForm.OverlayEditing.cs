using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    private void EditOverlayDefaults(bool perImage)
    {
        if (perImage && string.IsNullOrWhiteSpace(GetCurrentImagePath()))
            return;

        OverlayStyleDefaults current = perImage
            ? _currentImageOverlayDefaults?.Clone() ?? new OverlayStyleDefaults()
            : GetGlobalOverlayDefaults();
        using var dialog = new OverlayStyleDefaultsDialog(
            current,
            perImage ? "Image Overlay Defaults" : "Global Overlay Defaults");
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        OverlayStyleDefaults updated = dialog.Settings;
        if (perImage)
            SaveOverlayDefaultsForCurrentImage(updated);
        else
            SaveGlobalOverlayDefaults(updated);
    }

    private void SaveGlobalOverlayDefaults(OverlayStyleDefaults style)
    {
        _settings.ImageViewerOverlayDefaultTextColorArgb = style.TextColorArgb;
        _settings.ImageViewerOverlayDefaultTextOutlineColorArgb = style.TextOutlineColorArgb;
        _settings.ImageViewerOverlayDefaultTextAlignment = FromStringAlignment(style.TextAlignment);
        _settings.ImageViewerOverlayDefaultTextVerticalAlignment = FromStringAlignment(style.TextVerticalAlignment);
        _settings.ImageViewerOverlayDefaultTextOutlineVisible = style.TextOutlineVisible;
        _settings.ImageViewerOverlayDefaultBoxFillColorArgb = style.BoxFillColorArgb;
        _settings.ImageViewerOverlayDefaultBoxFillVisible = style.BoxFillVisible;
        _settings.ImageViewerOverlayDefaultBoxBorderColorArgb = style.BoxBorderColorArgb;
        _settings.ImageViewerOverlayDefaultBoxBorderVisible = style.BoxBorderVisible;
        _settings.Save();
        _pictureBox.Invalidate();
    }

    private void SaveOverlayDefaultsForCurrentImage(OverlayStyleDefaults style)
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (!TryGetExistingOcrCachePath(imagePath, out string cachePath))
        {
            if (_lastOcrResult == null)
            {
                RefreshAiStatusLabel("Run OCR before saving per-image overlay defaults");
                return;
            }

            SaveOcrResultToCache(imagePath, _settings.LlmModelName, _lastOcrResult);
            if (!TryGetExistingOcrCachePath(imagePath, out cachePath))
            {
                RefreshAiStatusLabel("Could not create the image OCR cache");
                return;
            }
        }

        if (!TryLoadSavedOcrEnvelope(imagePath, out var envelope) || envelope?.Result == null)
        {
            RefreshAiStatusLabel("Could not load the image OCR cache");
            return;
        }

        envelope.OverlayDefaults = style.IsEmpty ? null : style.Clone();
        File.WriteAllText(cachePath, SerializeOcrCacheEnvelopeForDisk(envelope));
        _currentImageOverlayDefaults = envelope.OverlayDefaults?.Clone();
        _pictureBox.Invalidate();
        RefreshAiStatusLabel("Saved image overlay defaults");
    }

    private int HitTestOverlayBlock(Point point)
    {
        if (!_overlayToggle.Checked || _overlayBlocks.Count == 0 || !TryGetCurrentImageDisplayRect(out var imageRect))
            return -1;

        for (int i = _overlayBlocks.Count - 1; i >= 0; i--)
        {
            var block = _overlayBlocks[i];
            var rect = new RectangleF(
                imageRect.X + (block.NormalizedRect.X * imageRect.Width),
                imageRect.Y + (block.NormalizedRect.Y * imageRect.Height),
                block.NormalizedRect.Width * imageRect.Width,
                block.NormalizedRect.Height * imageRect.Height);
            rect.Inflate(4f, 4f);
            if (rect.Contains(point))
                return i;
        }

        return -1;
    }

    private bool TryHitTestOverlayManipulation(Point point, out int blockIndex, out OverlayDragMode mode)
    {
        blockIndex = -1;
        mode = OverlayDragMode.None;

        if (!_overlayToggle.Checked || _overlayBlocks.Count == 0 || !TryGetCurrentImageDisplayRect(out var imageRect))
            return false;

        int edge = Math.Max(Scale(6), 4);
        for (int i = _overlayBlocks.Count - 1; i >= 0; i--)
        {
            var rect = GetOverlayBlockScreenRect(_overlayBlocks[i], imageRect);
            var hitRect = rect;
            hitRect.Inflate(edge, edge);
            if (!hitRect.Contains(point))
                continue;

            bool nearLeft = Math.Abs(point.X - rect.Left) <= edge;
            bool nearRight = Math.Abs(point.X - rect.Right) <= edge;
            bool nearTop = Math.Abs(point.Y - rect.Top) <= edge;
            bool nearBottom = Math.Abs(point.Y - rect.Bottom) <= edge;

            mode =
                nearLeft && nearTop ? OverlayDragMode.ResizeTopLeft :
                nearRight && nearTop ? OverlayDragMode.ResizeTopRight :
                nearLeft && nearBottom ? OverlayDragMode.ResizeBottomLeft :
                nearRight && nearBottom ? OverlayDragMode.ResizeBottomRight :
                nearLeft ? OverlayDragMode.ResizeLeft :
                nearRight ? OverlayDragMode.ResizeRight :
                nearTop ? OverlayDragMode.ResizeTop :
                nearBottom ? OverlayDragMode.ResizeBottom :
                OverlayDragMode.Move;

            blockIndex = i;
            return true;
        }

        return false;
    }

    private static RectangleF GetOverlayBlockScreenRect(OverlayTextBlock block, RectangleF imageRect)
        => new(
            imageRect.X + (block.NormalizedRect.X * imageRect.Width),
            imageRect.Y + (block.NormalizedRect.Y * imageRect.Height),
            block.NormalizedRect.Width * imageRect.Width,
            block.NormalizedRect.Height * imageRect.Height);

    private static Cursor GetOverlayDragCursor(OverlayDragMode mode)
        => mode switch
        {
            OverlayDragMode.Move => Cursors.SizeAll,
            OverlayDragMode.ResizeLeft or OverlayDragMode.ResizeRight => Cursors.SizeWE,
            OverlayDragMode.ResizeTop or OverlayDragMode.ResizeBottom => Cursors.SizeNS,
            OverlayDragMode.ResizeTopLeft or OverlayDragMode.ResizeBottomRight => Cursors.SizeNWSE,
            OverlayDragMode.ResizeTopRight or OverlayDragMode.ResizeBottomLeft => Cursors.SizeNESW,
            _ => Cursors.Default
        };

    private void EditContextOverlayBlock()
    {
        if (_contextOverlayBlockIndex < 0 || _contextOverlayBlockIndex >= _overlayBlocks.Count)
            return;

        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        var block = _overlayBlocks[_contextOverlayBlockIndex];
        int sourceIndex = block.SourceIndex;
        string originalSourceText = block.SourceText;
        string originalDisplayText = block.DisplayText;
        RectangleF originalRect = block.NormalizedRect;
        float originalFontSize = block.NormalizedFontSize;
        int? originalTextColorArgb = block.TextColorArgb;
        int? originalTextOutlineColorArgb = block.TextOutlineColorArgb;
        StringAlignment? originalTextAlignment = block.TextAlignment;
        StringAlignment? originalTextVerticalAlignment = block.TextVerticalAlignment;
        bool? originalTextOutlineVisible = block.TextOutlineVisible;
        int? originalBoxFillColorArgb = block.BoxFillColorArgb;
        int? originalBoxBorderColorArgb = block.BoxBorderColorArgb;
        bool? originalBoxFillVisible = block.BoxFillVisible;
        bool? originalBoxBorderVisible = block.BoxBorderVisible;
        bool originalHasUserOverride = block.HasUserOverride;
        string translationText = block.SourceIndex >= 0 && block.SourceIndex < _lastTranslations.Count
            ? _lastTranslations[block.SourceIndex]
            : "";
        bool preserveExplicitLineBreaks = block.HasUserOverride;

        using var dialog = new OverlayBlockEditDialog(
            preserveExplicitLineBreaks
                ? NormalizeEditedOverlayDisplayText(block.SourceText)
                : NormalizeOverlayDisplayText(block.SourceText),
            preserveExplicitLineBreaks
                ? NormalizeEditedOverlayDisplayText(translationText)
                : NormalizeOverlayDisplayText(translationText),
            block.NormalizedRect,
            block.NormalizedFontSize,
            block.TextColorArgb,
            block.TextOutlineColorArgb,
            block.TextAlignment,
            block.TextVerticalAlignment,
            block.TextOutlineVisible,
            block.BoxFillColorArgb,
            block.BoxBorderColorArgb,
            block.BoxFillVisible,
            block.BoxBorderVisible);

        dialog.PreviewChanged += (_, _) =>
        {
            PreviewOverlayBlockEdit(imagePath, sourceIndex, BuildOverlayBlockEditResult(dialog, trimText: false));
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            RestoreOverlayBlockPreview(
                imagePath,
                sourceIndex,
                originalSourceText,
                originalDisplayText,
                originalRect,
                originalFontSize,
                originalTextColorArgb,
                originalTextOutlineColorArgb,
                originalTextAlignment,
                originalTextVerticalAlignment,
                originalTextOutlineVisible,
                originalBoxFillColorArgb,
                originalBoxBorderColorArgb,
                originalBoxFillVisible,
                originalBoxBorderVisible,
                originalHasUserOverride);
            return;
        }

        ApplyOverlayBlockEdit(imagePath, sourceIndex, BuildOverlayBlockEditResult(dialog, trimText: true));
    }

    private static OverlayBlockEditResult BuildOverlayBlockEditResult(OverlayBlockEditDialog dialog, bool trimText)
    {
        string ocrText = trimText ? dialog.OcrText.Trim() : dialog.OcrText;
        string translationText = trimText ? dialog.TranslationText.Trim() : dialog.TranslationText;
        return new OverlayBlockEditResult
        {
            OcrText = ocrText,
            TranslationText = translationText,
            NormalizedRect = ClampNormalizedRect(
                dialog.NormalizedRect.X,
                dialog.NormalizedRect.Y,
                dialog.NormalizedRect.Width,
                dialog.NormalizedRect.Height),
            NormalizedFontSize = Math.Clamp(dialog.NormalizedFontSize, 0f, 0.5f),
            TextColorArgb = dialog.TextColorArgb,
            TextOutlineColorArgb = dialog.TextOutlineColorArgb,
            TextAlignment = dialog.TextAlignment,
            TextVerticalAlignment = dialog.TextVerticalAlignment,
            TextOutlineVisible = dialog.TextOutlineVisible,
            BoxFillColorArgb = dialog.BoxFillColorArgb,
            BoxBorderColorArgb = dialog.BoxBorderColorArgb,
            BoxFillVisible = dialog.BoxFillVisible,
            BoxBorderVisible = dialog.BoxBorderVisible,
            StyleSettingsChanged = dialog.StyleSettingsChanged
        };
    }

    private void PreviewOverlayBlockEdit(string imagePath, int sourceIndex, OverlayBlockEditResult edit)
    {
        if (!string.Equals(GetCurrentImagePath(), imagePath, StringComparison.OrdinalIgnoreCase))
            return;

        var block = _overlayBlocks.FirstOrDefault(b => b.SourceIndex == sourceIndex);
        if (block == null)
            return;

        block.SourceText = edit.OcrText;
        block.NormalizedRect = edit.NormalizedRect;
        block.NormalizedFontSize = edit.NormalizedFontSize;
        if (edit.StyleSettingsChanged)
        {
            block.TextColorArgb = edit.TextColorArgb;
            block.TextOutlineColorArgb = edit.TextOutlineColorArgb;
            block.TextAlignment = edit.TextAlignment;
            block.TextVerticalAlignment = edit.TextVerticalAlignment;
            block.TextOutlineVisible = edit.TextOutlineVisible;
            block.BoxFillColorArgb = edit.BoxFillColorArgb;
            block.BoxBorderColorArgb = edit.BoxBorderColorArgb;
            block.BoxFillVisible = edit.BoxFillVisible;
            block.BoxBorderVisible = edit.BoxBorderVisible;
        }
        block.HasUserOverride = true;
        string displayText =
            _showSavedTranslationCheck.Checked &&
            !string.IsNullOrWhiteSpace(edit.TranslationText)
                ? edit.TranslationText
                : edit.OcrText;
        block.DisplayText = NormalizeEditedOverlayDisplayText(displayText);
        _pictureBox.Invalidate();
    }

    private void RestoreOverlayBlockPreview(
        string imagePath,
        int sourceIndex,
        string sourceText,
        string displayText,
        RectangleF rect,
        float fontSize,
        int? textColorArgb,
        int? textOutlineColorArgb,
        StringAlignment? textAlignment,
        StringAlignment? textVerticalAlignment,
        bool? textOutlineVisible,
        int? boxFillColorArgb,
        int? boxBorderColorArgb,
        bool? boxFillVisible,
        bool? boxBorderVisible,
        bool hasUserOverride)
    {
        if (!string.Equals(GetCurrentImagePath(), imagePath, StringComparison.OrdinalIgnoreCase))
            return;

        var block = _overlayBlocks.FirstOrDefault(b => b.SourceIndex == sourceIndex);
        if (block == null)
            return;

        block.SourceText = sourceText;
        block.DisplayText = displayText;
        block.NormalizedRect = rect;
        block.NormalizedFontSize = fontSize;
        block.TextColorArgb = textColorArgb;
        block.TextOutlineColorArgb = textOutlineColorArgb;
        block.TextAlignment = textAlignment;
        block.TextVerticalAlignment = textVerticalAlignment;
        block.TextOutlineVisible = textOutlineVisible;
        block.BoxFillColorArgb = boxFillColorArgb;
        block.BoxBorderColorArgb = boxBorderColorArgb;
        block.BoxFillVisible = boxFillVisible;
        block.BoxBorderVisible = boxBorderVisible;
        block.HasUserOverride = hasUserOverride;
        _pictureBox.Invalidate();
    }

    private void ApplyOverlayBlockEdit(string imagePath, int sourceIndex, OverlayBlockEditResult edit)
    {
        if (sourceIndex < 0)
            return;

        if (!TryGetExistingOcrCachePath(imagePath, out string cachePath) && _lastOcrResult != null)
        {
            SaveOcrResultToCache(imagePath, _settings.LlmModelName, _lastOcrResult);
            TryGetExistingOcrCachePath(imagePath, out cachePath);
        }

        if (string.IsNullOrWhiteSpace(cachePath) ||
            !TryLoadSavedOcrEnvelope(imagePath, out var envelope) ||
            envelope?.Result == null)
        {
            RefreshAiStatusLabel("No saved OCR cache to edit");
            return;
        }

        envelope.OverlayOverrides ??= new List<OcrOverlayBlockOverride>();
        envelope.Result.Blocks ??= new List<LlmImageTextBlock>();
        if (sourceIndex >= envelope.Result.Blocks.Count)
            return;

        envelope.Result.Blocks[sourceIndex].Text = edit.OcrText;
        envelope.Result.FullText = ComposeFullTextFromBlocks(envelope.Result.Blocks);

        if (edit.TranslationText != null)
        {
            envelope.TranslationLines ??= new List<string>();
            while (envelope.TranslationLines.Count <= sourceIndex)
                envelope.TranslationLines.Add(string.Empty);
            envelope.TranslationLines[sourceIndex] = edit.TranslationText;
            envelope.TranslationFullText = string.Join(
                Environment.NewLine,
                envelope.TranslationLines.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()));
        }

        var existing = envelope.OverlayOverrides.FirstOrDefault(o => o.SourceIndex == sourceIndex);
        if (existing == null)
        {
            existing = new OcrOverlayBlockOverride { SourceIndex = sourceIndex };
            envelope.OverlayOverrides.Add(existing);
        }

        existing.Text = edit.OcrText;
        if (edit.TranslationText != null)
            existing.TranslationText = edit.TranslationText;
        existing.X = edit.NormalizedRect.X;
        existing.Y = edit.NormalizedRect.Y;
        existing.W = edit.NormalizedRect.Width;
        existing.H = edit.NormalizedRect.Height;
        existing.FontSize = edit.NormalizedFontSize;
        if (edit.StyleSettingsChanged)
        {
            existing.TextColorArgb = edit.TextColorArgb;
            existing.TextOutlineColorArgb = edit.TextOutlineColorArgb;
            existing.TextAlignment = edit.TextAlignment;
            existing.TextVerticalAlignment = edit.TextVerticalAlignment;
            existing.TextOutlineVisible = edit.TextOutlineVisible;
            existing.BoxFillColorArgb = edit.BoxFillColorArgb;
            existing.BoxBorderColorArgb = edit.BoxBorderColorArgb;
            existing.BoxFillVisible = edit.BoxFillVisible;
            existing.BoxBorderVisible = edit.BoxBorderVisible;
        }

        File.WriteAllText(cachePath, SerializeOcrCacheEnvelopeForDisk(envelope));

        _lastOcrResult = CloneOcrResult(envelope.Result);
        _savedTranslationForCurrentImage = TryBuildSavedTranslation(envelope, out var savedTranslation)
            ? savedTranslation
            : null;
        _lastTranslations = _savedTranslationForCurrentImage?.Translations?.ToList() ?? new List<string>();

        SetOverlayFromOcrResult(_lastOcrResult, _showSavedTranslationCheck.Checked ? _lastTranslations : null);
        ApplyCachedOverlayOverridesForCurrentImage();
        _aiOutputBox.Text = _savedTranslationForCurrentImage != null && _showSavedTranslationCheck.Checked
            ? RenderTranslatedResult(_lastOcrResult, _savedTranslationForCurrentImage)
            : RenderOcrResult(_lastOcrResult);
        RefreshAiStatusLabel("Saved OCR box edit");
        UpdateSavedCacheUiState();
    }

    private void SaveOverlayBlockDragEdit()
    {
        if (_overlayDragBlockIndex < 0 || _overlayDragBlockIndex >= _overlayBlocks.Count)
            return;

        string? imagePath = _overlayDragImagePath;
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (!string.Equals(GetCurrentImagePath(), imagePath, StringComparison.OrdinalIgnoreCase))
            return;

        var block = _overlayBlocks[_overlayDragBlockIndex];
        string? translationText = null;
        if (block.SourceIndex >= 0 && block.SourceIndex < _lastTranslations.Count)
        {
            translationText = _lastTranslations[block.SourceIndex];
        }
        else if (TryLoadSavedOcrEnvelope(imagePath, out var savedEnvelope) &&
                 savedEnvelope?.TranslationLines != null &&
                 block.SourceIndex >= 0 &&
                 block.SourceIndex < savedEnvelope.TranslationLines.Count)
        {
            // Dragging must not turn an incomplete in-memory translation list into an
            // empty cache entry. This also protects against older caches created before
            // translation placeholders were preserved.
            translationText = savedEnvelope.TranslationLines[block.SourceIndex] ?? "";
        }
        else
        {
            // Leave it null so ApplyOverlayBlockEdit preserves the cache value when the
            // in-memory translation list cannot identify this block.
        }

        Func<string, string> normalizeForPersistence = _overlayDragStartHadUserOverride
            ? NormalizeEditedOverlayDisplayText
            : NormalizeOverlayDisplayText;

        ApplyOverlayBlockEdit(imagePath, block.SourceIndex, new OverlayBlockEditResult
        {
            OcrText = normalizeForPersistence(block.SourceText),
            TranslationText = translationText == null
                ? null
                : normalizeForPersistence(translationText),
            NormalizedRect = block.NormalizedRect,
            NormalizedFontSize = block.NormalizedFontSize
        });
    }

    private void CancelOverlayDrag(bool invalidate = true)
    {
        if (_overlayDragMode == OverlayDragMode.None &&
            _overlayDragBlockIndex < 0 &&
            string.IsNullOrWhiteSpace(_overlayDragImagePath))
        {
            return;
        }

        _overlayDragMode = OverlayDragMode.None;
        _overlayDragBlockIndex = -1;
        _overlayDragImagePath = null;
        _overlayDragStartHadUserOverride = false;
        _overlayDragChanged = false;
        _pictureBox.Cursor = Cursors.Default;
        if (invalidate)
            _pictureBox.Invalidate();
    }

}
