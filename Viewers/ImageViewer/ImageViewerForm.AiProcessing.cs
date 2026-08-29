using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    private void EnqueueAiJob(ImageAiJob job)
    {
        _queuedAiJobs.Add(job);
        IncrementQueuedAiJobsForImage(job.ImagePath);
        RegisterQueuedManualRegions(job);
        SetAiBusy(true, BuildQueuedJobStatus(job));
        UpdateSavedCacheUiState();
        _pictureBox.Invalidate();
        _ = ProcessQueuedAiJobsAsync();
    }

    private ImageAiJob TakeNextQueuedAiJob(string? preferredImagePath)
    {
        int index = 0;
        if (!string.IsNullOrWhiteSpace(preferredImagePath))
        {
            int preferredIndex = _queuedAiJobs.FindIndex(job =>
                string.Equals(job.ImagePath, preferredImagePath, StringComparison.OrdinalIgnoreCase));
            if (preferredIndex >= 0)
                index = preferredIndex;
        }

        var job = _queuedAiJobs[index];
        _queuedAiJobs.RemoveAt(index);
        return job;
    }

    private string BuildQueuedJobStatus(ImageAiJob job)
    {
        string action = job.WithTranslation ? "translation" : "OCR";
        if (job.WithTranslation && job.UseMaximumEffortManualTranslation)
        {
            if (job.ManualSnippets.Count > 0)
                return $"Queued max-effort manual {action} for {Path.GetFileName(job.ImagePath)}";
            return $"Queued max-effort {action} for {Path.GetFileName(job.ImagePath)}";
        }

        if (job.ManualSnippets.Count > 0)
        {
            return $"Queued manual {action} for {Path.GetFileName(job.ImagePath)}";
        }
        return $"Queued {action} for {Path.GetFileName(job.ImagePath)}";
    }

    private async Task ProcessQueuedAiJobsAsync()
    {
        if (_activeAiJob != null || _aiCts != null)
            return;

        _aiCts = new CancellationTokenSource();
        string? preferredImagePath = null;
        try
        {
            while (_queuedAiJobs.Count > 0)
            {
                if (!string.IsNullOrWhiteSpace(preferredImagePath) &&
                    !_queuedAiJobs.Any(job => string.Equals(job.ImagePath, preferredImagePath, StringComparison.OrdinalIgnoreCase)))
                {
                    preferredImagePath = null;
                }

                var job = TakeNextQueuedAiJob(preferredImagePath);
                DecrementQueuedAiJobsForImage(job.ImagePath);
                _activeAiJob = job;
                preferredImagePath = job.ImagePath;

                string processingStatus = job.ManualSnippets.Count > 0
                    ? (job.WithTranslation
                        ? (job.UseMaximumEffortManualTranslation ? "Processing queued max-effort manual translation..." : "Processing queued manual translation...")
                        : "Processing queued manual OCR...")
                    : (job.WithTranslation
                        ? (job.UseMaximumEffortManualTranslation ? "Processing queued max-effort translation..." : "Processing queued translation...")
                        : "Processing queued OCR...");
                SetAiBusy(true, processingStatus);

                ImageAiJobResult result;
                try
                {
                    result = await ExecuteAiJobAsync(
                        job,
                        _aiCts.Token,
                        progressResult =>
                        {
                            if (progressResult.Ocr != null && job.ManualSnippets.Count > 0)
                            {
                                UnregisterQueuedManualRegions(job);
                                _pictureBox.Invalidate();
                            }

                            bool appliedProgress = ApplyAiJobResultIfCurrent(job, progressResult);
                            RefreshAiStatusLabel(appliedProgress ? progressResult.StatusText : null);
                        });
                }
                catch (OperationCanceledException)
                {
                    bool cancelOnlyThisJob = _cancelActiveAiJobOnly;
                    _cancelActiveAiJobOnly = false;
                    UnregisterQueuedManualRegions(job);
                    CleanupManualOcrSnippets(job.ManualSnippets);
                    _activeAiJob = null;
                    _pictureBox.Invalidate();

                    if (cancelOnlyThisJob)
                    {
                        RefreshAiStatusLabel("Cancelled current AI job");
                        _aiCts?.Dispose();
                        _aiCts = _queuedAiJobs.Count > 0 ? new CancellationTokenSource() : null;
                        if (_aiCts == null)
                            break;
                        continue;
                    }

                    RefreshAiStatusLabel("Operation aborted");
                    break;
                }
                catch (Exception ex)
                {
                    UnregisterQueuedManualRegions(job);
                    CleanupManualOcrSnippets(job.ManualSnippets);
                    LlmDebugLogger.LogError($"Queued image viewer AI job failed: {ex}");
                    result = new ImageAiJobResult
                    {
                        ImagePath = job.ImagePath,
                        StatusText = $"AI error: {ex.Message}",
                        ErrorText = ex.Message
                    };
                }

                UnregisterQueuedManualRegions(job);
                CleanupManualOcrSnippets(job.ManualSnippets);
                bool appliedToCurrent = ApplyAiJobResultIfCurrent(job, result);
                RefreshAiStatusLabel(appliedToCurrent ? result.StatusText : null);
                _activeAiJob = null;
                UpdateCancelCurrentJobButton();
                _pictureBox.Invalidate();
            }
        }
        finally
        {
            _activeAiJob = null;
            _cancelActiveAiJobOnly = false;
            _aiCts?.Dispose();
            _aiCts = null;
            string finalStatus = _aiStatusLabel.Text;
            SetAiBusy(false, finalStatus);
        }
    }

    private async Task<ImageAiJobResult> ExecuteAiJobAsync(
        ImageAiJob job,
        CancellationToken cancellationToken,
        Action<ImageAiJobResult>? progressCallback = null)
    {
        string imagePath = job.ImagePath;
        string? model = job.ModelId;

        if (job.ManualSnippets.Count > 0)
        {
            var baseOcr = GetBestBaseOcrForImage(imagePath);
            var existingTranslation = GetBestSavedTranslationForImage(imagePath);
            var (manualBlocks, detectedLanguage) = await ExtractManualOcrBlocksAsync(job.ManualSnippets, model ?? "", job.UseOcrReasoning, job.SourceLanguageHint, job.OcrHint, cancellationToken);
            if (manualBlocks.Count == 0)
            {
                return new ImageAiJobResult
                {
                    ImagePath = imagePath,
                    StatusText = "Manual OCR found no text",
                    ErrorText = "No text was found inside the selected manual OCR boxes."
                };
            }

            var mergedOcr = MergeManualBlocksIntoOcr(baseOcr, manualBlocks, detectedLanguage);
            SaveOcrResultToCache(imagePath, model, mergedOcr);
            if (job.WithTranslation)
            {
                progressCallback?.Invoke(new ImageAiJobResult
                {
                    ImagePath = imagePath,
                    Ocr = mergedOcr,
                    StatusText = $"Added {manualBlocks.Count} manual OCR box(es); translating..."
                });
            }

            if (!job.WithTranslation)
            {
                return new ImageAiJobResult
                {
                    ImagePath = imagePath,
                    Ocr = mergedOcr,
                    StatusText = $"Added {manualBlocks.Count} manual OCR box(es)"
                };
            }

            var mergedTranslation = await BuildMergedManualTranslationAsync(
                imagePath,
                mergedOcr,
                existingTranslation,
                manualBlocks,
                job.TargetLanguage,
                job.SourceLanguageHint,
                job.TranslationContextHint,
                job.UseMaximumEffortManualTranslation,
                job.UseTranslationReasoning,
                model,
                cancellationToken);
            if (mergedTranslation == null)
            {
                return new ImageAiJobResult
                {
                    ImagePath = imagePath,
                    Ocr = mergedOcr,
                    StatusText = "Translation failed",
                    ErrorText = "Translation failed"
                };
            }

            SaveTranslationToCache(imagePath, model, mergedOcr, mergedTranslation);
            return new ImageAiJobResult
            {
                ImagePath = imagePath,
                Ocr = mergedOcr,
                Translation = mergedTranslation,
                ShowSavedTranslation = true,
                StatusText = $"Added and translated {manualBlocks.Count} manual OCR box(es)"
            };
        }

        LlmImageTextResult? ocr = null;
        bool usingSavedOcr = false;

        if (job.WithTranslation && TryLoadSavedOcrEnvelope(imagePath, out var savedEnvelope) && savedEnvelope?.Result != null)
        {
            ocr = CloneOcrResult(savedEnvelope.Result);
            if (!job.UseMaximumEffortManualTranslation &&
                TryBuildSavedTranslation(savedEnvelope, out var savedTranslation) &&
                savedTranslation != null &&
                string.Equals(
                    NormalizeLanguageKey(savedTranslation.TargetLanguage),
                    NormalizeLanguageKey(job.TargetLanguage),
                    StringComparison.Ordinal))
            {
                return new ImageAiJobResult
                {
                    ImagePath = imagePath,
                    Ocr = ocr,
                    Translation = savedTranslation,
                    FromSavedCache = true,
                    ShowSavedTranslation = true,
                    StatusText = $"Loaded saved translation ({savedTranslation.TargetLanguage})"
                };
            }

            usingSavedOcr = true;
        }

        if (!usingSavedOcr)
        {
            ocr = await _llmService.ExtractImageTextAsync(imagePath, model, cancellationToken, useReasoning: job.UseOcrReasoning, sourceLanguageHint: job.SourceLanguageHint, ocrHint: job.OcrHint);
            if (ocr == null)
            {
                return new ImageAiJobResult
                {
                    ImagePath = imagePath,
                    StatusText = "OCR failed",
                    ErrorText = "Failed to extract text from the image."
                };
            }

            SaveOcrResultToCache(imagePath, model, ocr);
            if (job.WithTranslation)
            {
                progressCallback?.Invoke(new ImageAiJobResult
                {
                    ImagePath = imagePath,
                    Ocr = ocr,
                    StatusText = $"OCR regenerated ({ocr.Blocks.Count} blocks); translating..."
                });
            }

            if (!job.WithTranslation)
            {
                return new ImageAiJobResult
                {
                    ImagePath = imagePath,
                    Ocr = ocr,
                    StatusText = $"OCR regenerated ({ocr.Blocks.Count} blocks)"
                };
            }
        }

        if (ocr == null)
        {
            return new ImageAiJobResult
            {
                ImagePath = imagePath,
                StatusText = "OCR failed",
                ErrorText = "OCR failed"
            };
        }

        var sourceBlocks = ocr.Blocks.Select(b => b.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (sourceBlocks.Count == 0 && !string.IsNullOrWhiteSpace(ocr.FullText))
            sourceBlocks.Add(ocr.FullText);

        var translation = job.UseMaximumEffortManualTranslation
            ? await _llmService.TranslateTextBlocksWithContextImageAsync(
                sourceBlocks,
                job.TargetLanguage,
                imagePath,
                GetTranslationSourceLanguageHint(job.SourceLanguageHint, ocr.DetectedLanguage),
                job.TranslationContextHint,
                model,
                cancellationToken,
                useReasoning: job.UseTranslationReasoning)
            : await _llmService.TranslateTextBlocksAsync(
                sourceBlocks,
                job.TargetLanguage,
                GetTranslationSourceLanguageHint(job.SourceLanguageHint, ocr.DetectedLanguage),
                job.TranslationContextHint,
                model,
                cancellationToken,
                useReasoning: job.UseTranslationReasoning);
        if (translation == null)
        {
            return new ImageAiJobResult
            {
                ImagePath = imagePath,
                Ocr = ocr,
                StatusText = "Translation failed",
                ErrorText = "Translation failed"
            };
        }

        SaveTranslationToCache(imagePath, model, ocr, translation);
        return new ImageAiJobResult
        {
            ImagePath = imagePath,
            Ocr = ocr,
            Translation = translation,
            ShowSavedTranslation = true,
            StatusText = $"Translated to {translation.TargetLanguage}"
        };
    }

    private bool ApplyAiJobResultIfCurrent(ImageAiJob job, ImageAiJobResult result)
    {
        if (!string.Equals(GetCurrentImagePath(), result.ImagePath, StringComparison.OrdinalIgnoreCase))
        {
            UpdateSavedCacheUiState();
            return false;
        }

        CancelOverlayDrag(invalidate: false);

        if (!string.IsNullOrWhiteSpace(result.ErrorText))
        {
            if (!string.IsNullOrWhiteSpace(result.ErrorText))
                _aiOutputBox.Text = result.ErrorText;
            UpdateSavedCacheUiState();
            return true;
        }

        if (result.Ocr == null)
        {
            UpdateSavedCacheUiState();
            return true;
        }

        ApplyLoadedOcrToViewer(result.ImagePath, result.Ocr, result.FromSavedCache);
        _savedTranslationForCurrentImage = result.Translation;
        _lastTranslations = result.Translation?.Translations?.ToList() ?? new List<string>();

        if (result.Translation != null)
        {
            ApplyTranslationsToOverlay(_lastTranslations);
            _aiOutputBox.Text = RenderTranslatedResult(result.Ocr, result.Translation);
            if (result.FromSavedCache)
                _aiOutputBox.AppendText(Environment.NewLine + Environment.NewLine + "[Loaded from OCR_output cache]");
            SetShowSavedTranslationChecked(result.ShowSavedTranslation, updatePreference: result.ShowSavedTranslation);
            _currentOverlayFromSavedCache = result.FromSavedCache;
        }
        else
        {
            SetShowSavedTranslationChecked(false);
            _currentOverlayFromSavedCache = result.FromSavedCache;
        }

        UpdateSavedCacheUiState();
        return true;
    }

    private async Task RunViewerTaggingAsync()
    {
        if (_aiBusy)
            return;

        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return;

        try
        {
            _activeTagImagePath = imagePath;
            SetAiBusy(true, "Resolving model...");
            string? model = await EnsureVisionModelAsync();
            if (string.IsNullOrWhiteSpace(model))
            {
                SetAiBusy(false, "Model selection cancelled");
                return;
            }

            SetAiBusy(true, "Generating tags...");
            _tagCts = new CancellationTokenSource();
            var tags = await _llmService.GetImageTagsAsync(
                "Analyze this image and return concise descriptive tags only. Prefer 8 to 20 tags.",
                imagePath,
                model,
                _tagCts.Token);

            if (tags.Count == 0)
            {
                SetAiBusy(false, "No tags generated");
                _aiOutputBox.Text = "No tags were returned for this image.";
                return;
            }

            var normalized = tags
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            TagManager.Instance.UpdateTagsBatch(new[] { imagePath }, normalized, Enumerable.Empty<string>());
            UpdateTags(imagePath);

            _aiOutputBox.Text = "Applied tags:" + Environment.NewLine + string.Join(", ", normalized);
            SetAiBusy(false, $"Applied {normalized.Count} tags");
        }
        catch (OperationCanceledException)
        {
            SetAiBusy(false, "Operation aborted");
        }
        catch (Exception ex)
        {
            SetAiBusy(false, $"Tagging error: {ex.Message}");
            LlmDebugLogger.LogError($"Image viewer tagging failed: {ex}");
        }
        finally
        {
            _tagCts?.Dispose();
            _tagCts = null;
            _activeTagImagePath = null;
            UpdateAiActionControlsState();
            UpdateManualOcrUiState();
            UpdateSavedCacheUiState();
        }
    }

    private void AbortAi()
    {
        try
        {
            _cancelActiveAiJobOnly = false;
            if (_activeAiJob != null)
                RestoreManualRegionsFromAbortedJob(_activeAiJob);

            _aiCts?.Cancel();
            _tagCts?.Cancel();

            while (_queuedAiJobs.Count > 0)
            {
                var job = _queuedAiJobs[0];
                _queuedAiJobs.RemoveAt(0);
                RestoreManualRegionsFromAbortedJob(job);
                DecrementQueuedAiJobsForImage(job.ImagePath);
                UnregisterQueuedManualRegions(job);
                CleanupManualOcrSnippets(job.ManualSnippets);
            }

            UpdateManualOcrUiState();
            UpdateCancelCurrentJobButton();
            RefreshAiStatusLabel("Aborting...");
            _pictureBox.Invalidate();
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Failed to abort AI: {ex.Message}");
        }
    }

    private void CancelAiJobForCurrentImage()
    {
        try
        {
            string? imagePath = GetCurrentImagePath();
            if (string.IsNullOrWhiteSpace(imagePath))
                return;

            if (_activeAiJob != null &&
                string.Equals(_activeAiJob.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
            {
                RestoreManualRegionsFromAbortedJob(_activeAiJob);
                _cancelActiveAiJobOnly = true;
                _aiCts?.Cancel();
                RefreshAiStatusLabel("Cancelling current AI job...");
                UpdateCancelCurrentJobButton();
                _pictureBox.Invalidate();
                return;
            }

            int queuedIndex = _queuedAiJobs.FindIndex(job =>
                string.Equals(job.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase));
            if (queuedIndex < 0)
                return;

            var queuedJob = _queuedAiJobs[queuedIndex];
            _queuedAiJobs.RemoveAt(queuedIndex);
            RestoreManualRegionsFromAbortedJob(queuedJob);
            DecrementQueuedAiJobsForImage(queuedJob.ImagePath);
            UnregisterQueuedManualRegions(queuedJob);
            CleanupManualOcrSnippets(queuedJob.ManualSnippets);

            UpdateManualOcrUiState();
            UpdateCancelCurrentJobButton();
            SetAiBusy(HasQueuedAiWork(), "Cancelled queued AI job");
            _pictureBox.Invalidate();
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Failed to cancel current image AI job: {ex.Message}");
        }
    }

}
