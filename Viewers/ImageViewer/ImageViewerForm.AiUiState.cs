using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    private void ToggleAiPanel()
    {
        _aiPanel.Visible = !_aiPanel.Visible;
        _settings.ImageViewerAiPanelVisible = _aiPanel.Visible;
        _settings.Save();
        ApplyAiPanelToggleVisualState();
        _contentPanel.PerformLayout();
        LayoutControls();
        _pictureBox.Invalidate();
    }

    private void ApplyAiPanelToggleVisualState()
    {
        _aiToggleBtn.BackColor = _aiPanel.Visible ? Color.FromArgb(78, 78, 78) : Color.FromArgb(60, 60, 60);
        _aiToggleBtn.ForeColor = _aiPanel.Visible ? Color.White : ForeColor_Dark;
    }

    private void ToggleManualOcrDrawMode()
    {
        _manualOcrDrawMode = !_manualOcrDrawMode;
        _isDrawingManualOcrRegion = false;
        UpdateManualOcrUiState();
        _pictureBox.Invalidate();
        RefreshAiStatusLabel(_manualOcrDrawMode ? "Draw OCR boxes with the mouse" : null);
    }

    private void ClearPendingManualOcrRegions(bool updateStatus = true)
    {
        _pendingManualOcrRegions.Clear();
        _isDrawingManualOcrRegion = false;
        UpdateManualOcrUiState();
        _pictureBox.Invalidate();

        if (updateStatus)
            RefreshAiStatusLabel("Cleared pending manual OCR boxes");
    }

    private void UpdateManualOcrUiState()
    {
        bool canEdit = _currentImage != null && !IsCurrentImageActivelyProcessing();
        _drawOcrBoxBtn.Enabled = canEdit;
        _clearManualOcrBoxesBtn.Enabled = canEdit && _pendingManualOcrRegions.Count > 0;
        _drawOcrBoxBtn.BackColor = _manualOcrDrawMode ? Color.FromArgb(78, 78, 78) : Color.FromArgb(60, 60, 60);
        _drawOcrBoxBtn.ForeColor = _manualOcrDrawMode ? Color.White : ForeColor_Dark;
        _pictureBox.Cursor = _manualOcrDrawMode ? Cursors.Cross : Cursors.Default;
    }

    private void SetAiBusy(bool busy, string statusText)
    {
        _aiBusy = busy;
        UpdateAiActionControlsState();
        _targetLanguageBox.Enabled = _currentImage != null;
        _sourceLanguageHintBox.Enabled = _currentImage != null;
        _ocrHintBox.Enabled = _currentImage != null;
        _translationContextHintBox.Enabled = _currentImage != null;
        _ocrReasoningCheck.Enabled = _currentImage != null;
        _translationReasoningCheck.Enabled = _currentImage != null;
        _abortBtn.Visible = busy;
        UpdateCancelCurrentJobButton();
        _overlayToggle.Enabled = true;
        _showSavedOcrCheck.Enabled = _currentImage != null;
        _copyResultBtn.Enabled = true;
        _aiStatusLabel.Text = statusText;
        if (busy)
            _aiOutputBox.Cursor = Cursors.WaitCursor;
        else
            _aiOutputBox.Cursor = Cursors.Default;
        UpdateManualOcrUiState();
        UpdateSavedCacheUiState();
    }

    private void UpdateCancelCurrentJobButton()
    {
        bool canCancel = TryGetCancelableAiJobForCurrentImage(out _);
        _cancelCurrentJobBtn.Visible = canCancel;
        _cancelCurrentJobBtn.Enabled = canCancel;
    }

    private bool HasQueuedAiWork()
        => _activeAiJob != null || _queuedAiJobs.Count > 0;

    private bool IsCurrentImageActivelyProcessing()
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath))
            return false;

        if (_activeAiJob != null &&
            string.Equals(_activeAiJob.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(_activeTagImagePath) &&
            string.Equals(_activeTagImagePath, imagePath, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsCurrentImageOverlayJobPending()
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath))
            return false;

        return (_activeAiJob != null &&
                string.Equals(_activeAiJob.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase)) ||
            GetQueuedAiJobsForImage(imagePath) > 0;
    }

    private void UpdateAiActionControlsState()
    {
        bool currentImageActive = IsCurrentImageActivelyProcessing();
        _ocrBtn.Enabled = _currentImage != null && !currentImageActive;
        _translateBtn.Enabled = _currentImage != null && !currentImageActive;
        _tagBtn.Enabled = _currentImage != null && !currentImageActive && !_aiBusy && _tagCts == null;
    }

    private int GetQueuedAiJobsForImage(string imagePath)
        => _queuedAiJobCountsByImage.TryGetValue(imagePath, out int count) ? count : 0;

    private bool TryGetCancelableAiJobForCurrentImage(out ImageAiJob? job)
    {
        job = null;
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath))
            return false;

        if (_activeAiJob != null &&
            string.Equals(_activeAiJob.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
        {
            job = _activeAiJob;
            return true;
        }

        job = _queuedAiJobs.FirstOrDefault(queued =>
            string.Equals(queued.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase));
        return job != null;
    }

    private void IncrementQueuedAiJobsForImage(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        _queuedAiJobCountsByImage[imagePath] = GetQueuedAiJobsForImage(imagePath) + 1;
    }

    private void DecrementQueuedAiJobsForImage(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        int count = GetQueuedAiJobsForImage(imagePath);
        if (count <= 1)
            _queuedAiJobCountsByImage.Remove(imagePath);
        else
            _queuedAiJobCountsByImage[imagePath] = count - 1;
    }

    private void RegisterQueuedManualRegions(ImageAiJob job)
    {
        if (job.ManualSnippets.Count == 0 || string.IsNullOrWhiteSpace(job.ImagePath))
            return;

        if (!_queuedManualRegionsByImage.TryGetValue(job.ImagePath, out var regions))
        {
            regions = new List<RectangleF>();
            _queuedManualRegionsByImage[job.ImagePath] = regions;
        }

        regions.AddRange(job.ManualSnippets.Select(s => s.NormalizedRect));
    }

    private void UnregisterQueuedManualRegions(ImageAiJob job)
    {
        if (job.ManualSnippets.Count == 0 || string.IsNullOrWhiteSpace(job.ImagePath))
            return;

        if (!_queuedManualRegionsByImage.TryGetValue(job.ImagePath, out var regions) || regions.Count == 0)
            return;

        foreach (var snippet in job.ManualSnippets)
        {
            int index = regions.FindIndex(r =>
                Math.Abs(r.X - snippet.NormalizedRect.X) < 0.0001f &&
                Math.Abs(r.Y - snippet.NormalizedRect.Y) < 0.0001f &&
                Math.Abs(r.Width - snippet.NormalizedRect.Width) < 0.0001f &&
                Math.Abs(r.Height - snippet.NormalizedRect.Height) < 0.0001f);
            if (index >= 0)
                regions.RemoveAt(index);
        }

        if (regions.Count == 0)
            _queuedManualRegionsByImage.Remove(job.ImagePath);
    }

    private static bool RectanglesRoughlyEqual(RectangleF a, RectangleF b)
        => Math.Abs(a.X - b.X) < 0.0001f &&
           Math.Abs(a.Y - b.Y) < 0.0001f &&
           Math.Abs(a.Width - b.Width) < 0.0001f &&
           Math.Abs(a.Height - b.Height) < 0.0001f;

    private static void AddRectIfMissing(List<RectangleF> regions, RectangleF rect)
    {
        if (!regions.Any(existing => RectanglesRoughlyEqual(existing, rect)))
            regions.Add(rect);
    }

    private void AddPendingManualRegionIfMissing(RectangleF rect)
    {
        if (!_pendingManualOcrRegions.Any(existing => RectanglesRoughlyEqual(existing.NormalizedRect, rect)))
            _pendingManualOcrRegions.Add(new ManualOcrRegion { NormalizedRect = rect });
    }

    private bool HasQueuedManualRegions(ImageAiJob job)
    {
        if (job.ManualSnippets.Count == 0 || string.IsNullOrWhiteSpace(job.ImagePath))
            return false;

        if (!_queuedManualRegionsByImage.TryGetValue(job.ImagePath, out var regions) || regions.Count == 0)
            return false;

        return job.ManualSnippets.Any(snippet => regions.Any(region => RectanglesRoughlyEqual(region, snippet.NormalizedRect)));
    }

    private void RestoreManualRegionsFromAbortedJob(ImageAiJob job)
    {
        if (!HasQueuedManualRegions(job))
            return;

        string? currentImagePath = GetCurrentImagePath();
        bool isCurrentImage = string.Equals(currentImagePath, job.ImagePath, StringComparison.OrdinalIgnoreCase);
        List<RectangleF>? storedRegions = null;
        if (!isCurrentImage)
        {
            if (!_restorableManualRegionsByImage.TryGetValue(job.ImagePath, out storedRegions))
            {
                storedRegions = new List<RectangleF>();
                _restorableManualRegionsByImage[job.ImagePath] = storedRegions;
            }
        }

        foreach (var snippet in job.ManualSnippets)
        {
            if (isCurrentImage)
                AddPendingManualRegionIfMissing(snippet.NormalizedRect);
            else
                AddRectIfMissing(storedRegions!, snippet.NormalizedRect);
        }
    }

    private void RestorePendingManualRegionsForCurrentImage()
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (!_restorableManualRegionsByImage.TryGetValue(imagePath, out var restoredRegions) || restoredRegions.Count == 0)
            return;

        foreach (var rect in restoredRegions)
            AddPendingManualRegionIfMissing(rect);

        _restorableManualRegionsByImage.Remove(imagePath);
    }

    private void RefreshAiStatusLabel(string? overrideStatus = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideStatus))
        {
            _aiStatusLabel.Text = overrideStatus;
            return;
        }

        if (_manualOcrDrawMode)
        {
            _aiStatusLabel.Text = "Draw OCR boxes with the mouse";
            return;
        }

        if (_pendingManualOcrRegions.Count > 0)
        {
            _aiStatusLabel.Text = $"{_pendingManualOcrRegions.Count} manual OCR box(es) queued";
            return;
        }

        string? imagePath = GetCurrentImagePath();
        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            if (_activeAiJob != null &&
                string.Equals(_activeAiJob.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
            {
                string action = _activeAiJob.WithTranslation ? "translate" : "OCR";
                _aiStatusLabel.Text = _activeAiJob.ManualSnippets.Count > 0
                    ? $"Processing queued manual {action}..."
                    : $"Processing queued {action}...";
                return;
            }

            int queuedForImage = GetQueuedAiJobsForImage(imagePath);
            if (queuedForImage > 0)
            {
                _aiStatusLabel.Text = queuedForImage == 1
                    ? "1 AI job queued for this image"
                    : $"{queuedForImage} AI jobs queued for this image";
                return;
            }
        }

        if (HasQueuedAiWork())
        {
            int pending = _queuedAiJobs.Count + (_activeAiJob != null ? 1 : 0);
            _aiStatusLabel.Text = pending == 1 ? "1 AI job in progress" : $"{pending} AI jobs in progress";
            return;
        }

        _aiStatusLabel.Text = "AI ready";
    }

    private async Task<string?> EnsureVisionModelAsync()
    {
        _llmService.ApiUrl = LlmService.GetCompletionsApiUrl(_settings.LlmApiUrl, null);
        return await _llmService.ResolveModelForTaskAsync(LlmUsageKind.Assistant, LlmTaskKind.Vision, this);
    }

}
