using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    private List<ManualOcrSnippet> CaptureManualOcrSnippetsForQueue()
    {
        var snippets = new List<ManualOcrSnippet>(_pendingManualOcrRegions.Count);
        foreach (var region in _pendingManualOcrRegions)
        {
            using var snippet = CreateManualOcrSnippetBitmap(region.NormalizedRect);
            if (snippet == null)
                continue;

            string tempPath = Path.Combine(Path.GetTempPath(), $"speedexplorer-ocr-{Guid.NewGuid():N}.png");
            snippet.Save(tempPath, ImageFormat.Png);
            snippets.Add(new ManualOcrSnippet
            {
                NormalizedRect = UnrotateNormalizedRect(region.NormalizedRect, _rotationQuarterTurns),
                TempPath = tempPath
            });
        }

        return snippets;
    }

    private static void CleanupManualOcrSnippets(IEnumerable<ManualOcrSnippet>? snippets)
    {
        if (snippets == null)
            return;

        foreach (var snippet in snippets)
        {
            if (snippet == null || string.IsNullOrWhiteSpace(snippet.TempPath))
                continue;

            try
            {
                if (File.Exists(snippet.TempPath))
                    File.Delete(snippet.TempPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to delete manual OCR temp snippet '{snippet.TempPath}': {ex.Message}");
            }
        }
    }

    private Bitmap? CreateManualOcrSnippetBitmap(RectangleF normalizedRect)
    {
        if (_currentImage == null)
            return null;

        var pixelRect = NormalizeRectToPixels(normalizedRect, _currentImage.Size);
        if (pixelRect.Width < 1 || pixelRect.Height < 1)
            return null;

        var snippet = new Bitmap(pixelRect.Width, pixelRect.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(snippet);
        g.Clear(Color.Transparent);
        g.DrawImage(_currentImage, new Rectangle(0, 0, snippet.Width, snippet.Height), pixelRect, GraphicsUnit.Pixel);
        return snippet;
    }

    private async Task<(List<LlmImageTextBlock> Blocks, string DetectedLanguage)> ExtractManualOcrBlocksAsync(
        IReadOnlyList<ManualOcrSnippet> snippets,
        string model,
        bool useOcrReasoning,
        string sourceLanguageHint,
        string ocrHint,
        CancellationToken cancellationToken)
    {
        var blocks = new List<LlmImageTextBlock>(snippets.Count);
        string detectedLanguage = "";

        for (int i = 0; i < snippets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string text = (await _llmService.ExtractSnippetTextAsync(snippets[i].TempPath, model, cancellationToken, useReasoning: useOcrReasoning, sourceLanguageHint: sourceLanguageHint, ocrHint: ocrHint))?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(text))
                continue;

            blocks.Add(new LlmImageTextBlock
            {
                Text = text,
                X = snippets[i].NormalizedRect.X,
                Y = snippets[i].NormalizedRect.Y,
                W = snippets[i].NormalizedRect.Width,
                H = snippets[i].NormalizedRect.Height,
                FontSize = 0f
            });
        }

        return (blocks, detectedLanguage);
    }

    private static LlmImageTextResult MergeManualBlocksIntoOcr(
        LlmImageTextResult baseOcr,
        IReadOnlyList<LlmImageTextBlock> manualBlocks,
        string detectedLanguage)
    {
        var merged = CloneOcrResult(baseOcr);
        merged.Blocks ??= new List<LlmImageTextBlock>();
        merged.Blocks.AddRange(manualBlocks);
        merged.FullText = ComposeFullTextFromBlocks(merged.Blocks);
        if (string.IsNullOrWhiteSpace(merged.DetectedLanguage) && !string.IsNullOrWhiteSpace(detectedLanguage))
            merged.DetectedLanguage = detectedLanguage;
        return merged;
    }

    private async Task<LlmTextTranslationResult?> BuildMergedManualTranslationAsync(
        string imagePath,
        LlmImageTextResult mergedOcr,
        LlmTextTranslationResult? existingTranslation,
        IReadOnlyList<LlmImageTextBlock> manualBlocks,
        string targetLanguage,
        string sourceLanguageHint,
        string translationContextHint,
        bool useMaximumEffortManualTranslation,
        bool useTranslationReasoning,
        string? model,
        CancellationToken cancellationToken)
    {
        var manualTexts = manualBlocks
            .Select(b => b.Text?.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Cast<string>()
            .ToList();

        var allTexts = mergedOcr.Blocks
            .Select(b => b.Text?.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Cast<string>()
            .ToList();
        if (allTexts.Count == 0 && !string.IsNullOrWhiteSpace(mergedOcr.FullText))
            allTexts.Add(mergedOcr.FullText.Trim());

        if (useMaximumEffortManualTranslation && allTexts.Count > 0)
        {
            return await _llmService.TranslateTextBlocksWithContextImageAsync(
                allTexts,
                targetLanguage,
                imagePath,
                GetTranslationSourceLanguageHint(sourceLanguageHint, mergedOcr.DetectedLanguage),
                translationContextHint,
                model,
                cancellationToken,
                useReasoning: useTranslationReasoning);
        }

        bool canAppendToSavedTranslation =
            existingTranslation != null &&
            string.Equals(NormalizeLanguageKey(existingTranslation.TargetLanguage), NormalizeLanguageKey(targetLanguage), StringComparison.Ordinal) &&
            existingTranslation.Translations != null &&
            existingTranslation.Translations.Count == Math.Max(0, mergedOcr.Blocks.Count - manualTexts.Count);

        if (canAppendToSavedTranslation && manualTexts.Count > 0)
        {
            var translatedManual = await TranslateManualBlocksAsync(manualTexts, targetLanguage, translationContextHint, model, useTranslationReasoning, cancellationToken);
            if (translatedManual == null)
                return null;

            var mergedLines = existingTranslation!.Translations!.ToList();
            mergedLines.AddRange(translatedManual);
            return new LlmTextTranslationResult
            {
                TargetLanguage = existingTranslation.TargetLanguage,
                Translations = mergedLines,
                TranslatedFullText = string.Join(Environment.NewLine, mergedLines.Where(t => !string.IsNullOrWhiteSpace(t)))
            };
        }

        var translatedAll = await TranslateManualBlocksAsync(allTexts, targetLanguage, translationContextHint, model, useTranslationReasoning, cancellationToken);
        if (translatedAll == null)
            return null;

        return new LlmTextTranslationResult
        {
            TargetLanguage = targetLanguage,
            Translations = translatedAll,
            TranslatedFullText = string.Join(Environment.NewLine, translatedAll.Where(t => !string.IsNullOrWhiteSpace(t)))
        };
    }

    private async Task<List<string>?> TranslateManualBlocksAsync(
        IReadOnlyList<string> sourceBlocks,
        string targetLanguage,
        string translationContextHint,
        string? model,
        bool useTranslationReasoning,
        CancellationToken cancellationToken)
    {
        var translations = new List<string>(sourceBlocks.Count);
        for (int i = 0; i < sourceBlocks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source = sourceBlocks[i]?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(source))
            {
                translations.Add(string.Empty);
                continue;
            }

            string? translated = await _llmService.TranslateSimpleTextAsync(source, targetLanguage, model, cancellationToken, useReasoning: useTranslationReasoning, contextHint: translationContextHint);
            if (translated == null)
                return null;

            translations.Add(translated.Trim());
        }

        return translations;
    }

    private static string? GetTranslationSourceLanguageHint(string? userHint, string? detectedLanguage)
    {
        if (!string.IsNullOrWhiteSpace(userHint))
            return userHint.Trim();
        if (!string.IsNullOrWhiteSpace(detectedLanguage))
            return detectedLanguage.Trim();
        return null;
    }

    private async Task RunViewerOcrAsync(bool withTranslation)
    {
        if (IsCurrentImageActivelyProcessing())
            return;

        if (_tagCts != null)
        {
            RefreshAiStatusLabel("Wait for tagging to finish before queueing OCR or translation");
            return;
        }

        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return;

        string targetLanguage = string.IsNullOrWhiteSpace(_targetLanguageBox.Text) ? "English" : _targetLanguageBox.Text.Trim();
        List<ManualOcrSnippet>? manualSnippets = null;

        try
        {
            string actionName = withTranslation ? "translation" : "OCR";
            SetAiBusy(HasQueuedAiWork(), $"Resolving model for queued {actionName}...");
            string? model = await EnsureVisionModelAsync();
            if (string.IsNullOrWhiteSpace(model))
            {
                SetAiBusy(HasQueuedAiWork(), "Model selection cancelled");
                return;
            }

            if (_pendingManualOcrRegions.Count > 0)
            {
                manualSnippets = CaptureManualOcrSnippetsForQueue();
                if (manualSnippets.Count == 0)
                {
                    RefreshAiStatusLabel("No text snippets were captured from the manual OCR boxes");
                    return;
                }

                ClearPendingManualOcrRegions(updateStatus: false);
            }

            var job = new ImageAiJob
            {
                ImagePath = imagePath,
                WithTranslation = withTranslation,
                UseMaximumEffortManualTranslation = withTranslation && _manualMaxEffortCheck.Checked,
                UseOcrReasoning = _ocrReasoningCheck.Checked,
                UseTranslationReasoning = _translationReasoningCheck.Checked,
                TargetLanguage = targetLanguage,
                SourceLanguageHint = _sourceLanguageHintBox.Text.Trim(),
                OcrHint = _ocrHintBox.Text.Trim(),
                TranslationContextHint = withTranslation ? _translationContextHintBox.Text.Trim() : "",
                ModelId = model,
                ManualSnippets = manualSnippets ?? new List<ManualOcrSnippet>()
            };

            EnqueueAiJob(job);
        }
        catch (Exception ex)
        {
            CleanupManualOcrSnippets(manualSnippets);
            SetAiBusy(HasQueuedAiWork(), $"AI queue error: {ex.Message}");
            LlmDebugLogger.LogError($"Failed to enqueue image viewer OCR/translate job: {ex}");
        }
    }

}
