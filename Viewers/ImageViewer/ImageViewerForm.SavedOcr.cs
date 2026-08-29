using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    private void DeleteSavedOcrForCurrentImage()
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        int deleted = 0;
        foreach (var path in EnumerateOcrCacheCandidates(imagePath))
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                LlmDebugLogger.LogError($"Failed to delete saved OCR file '{path}': {ex.Message}");
            }
        }

        if (deleted == 0)
        {
            if (!_aiBusy)
                _aiStatusLabel.Text = "No saved OCR file to delete";
            UpdateSavedCacheUiState();
            return;
        }

        _overlayBlocks.Clear();
        _lastTranslations = new List<string>();
        _savedTranslationForCurrentImage = null;
        _ocrImagePath = null;
        _lastOcrResult = null;
        _aiOutputBox.Clear();
        _pictureBox.Invalidate();
        _currentOverlayFromSavedCache = false;

        if (!_aiBusy)
            _aiStatusLabel.Text = "Deleted saved OCR";

        UpdateSavedCacheUiState();
    }

    private void DeleteSavedTranslationForCurrentImage()
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (!TryGetExistingOcrCachePath(imagePath, out string cachePath) ||
            !TryLoadSavedOcrEnvelope(imagePath, out var envelope) ||
            envelope == null ||
            !TryBuildSavedTranslation(envelope, out _))
        {
            if (!_aiBusy)
                _aiStatusLabel.Text = "No saved translation to delete";
            UpdateSavedCacheUiState();
            return;
        }

        try
        {
            envelope.TranslationTargetLanguage = "";
            envelope.TranslationSourceLanguage = "";
            envelope.TranslationModelId = "";
            envelope.TranslationFullText = "";
            envelope.TranslationLines = new List<string>();
            envelope.TranslationSavedUtcTicks = 0;

            File.WriteAllText(cachePath, SerializeOcrCacheEnvelopeForDisk(envelope));
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Failed to delete saved translation from '{cachePath}': {ex.Message}");
            if (!_aiBusy)
                _aiStatusLabel.Text = "Failed to delete saved translation";
            UpdateSavedCacheUiState();
            return;
        }

        _savedTranslationForCurrentImage = null;
        _lastTranslations = new List<string>();
        SetShowSavedTranslationChecked(false, updatePreference: true);

        if (_lastOcrResult != null && string.Equals(_ocrImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
        {
            SetOverlayFromOcrResult(_lastOcrResult, null);
            _aiOutputBox.Text = RenderOcrResult(_lastOcrResult);
            _aiOutputBox.AppendText(Environment.NewLine + Environment.NewLine + "[Translation deleted from OCR_output cache]");
        }

        _pictureBox.Invalidate();
        if (!_aiBusy)
            _aiStatusLabel.Text = "Deleted saved translation";

        UpdateSavedCacheUiState();
    }

    private void OpenSavedOcrFileForCurrentImage()
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (!TryGetExistingOcrCachePath(imagePath, out string cachePath))
        {
            if (!_aiBusy)
                _aiStatusLabel.Text = "No saved OCR file";
            return;
        }

        string selectArg = $"/select,\"{cachePath}\"";
        string? cacheDirectory = Path.GetDirectoryName(cachePath);
        if (string.IsNullOrWhiteSpace(cacheDirectory) || !Directory.Exists(cacheDirectory))
        {
            if (!_aiBusy)
                _aiStatusLabel.Text = "Saved OCR folder not found";
            return;
        }

        try
        {
            var existingMain = Application.OpenForms
                .OfType<MainForm>()
                .LastOrDefault(f => !f.IsDisposed);
            if (existingMain != null)
            {
                if (existingMain.WindowState == FormWindowState.Minimized)
                    existingMain.WindowState = FormWindowState.Normal;
                existingMain.Show();
                existingMain.Activate();
                existingMain.BringToFront();
                existingMain.HandleExternalPath(selectArg);
                if (!_aiBusy)
                    _aiStatusLabel.Text = $"Opened and selected: {Path.GetFileName(cachePath)}";
                return;
            }
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogExecution($"Direct open/select via existing MainForm failed: {ex.Message}", false);
        }

        try
        {
            string exePath = Application.ExecutablePath;
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                var appPsi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = selectArg,
                    UseShellExecute = true
                };
                Process.Start(appPsi);
                if (!_aiBusy)
                    _aiStatusLabel.Text = $"Opened and selected: {Path.GetFileName(cachePath)}";
                return;
            }
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogExecution($"Open/select via app executable failed: {ex.Message}", false);
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = cacheDirectory,
                Verb = "open",
                UseShellExecute = true
            };
            Process.Start(psi);
            if (!_aiBusy)
                _aiStatusLabel.Text = $"Opened saved OCR folder: {Path.GetFileName(cachePath)}";
            return;
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogExecution($"Open saved OCR folder via shell failed, falling back to Explorer select: {ex.Message}", false);
        }

        try
        {
            var fallbackPsi = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{cachePath}\"",
                UseShellExecute = true
            };
            Process.Start(fallbackPsi);
            if (!_aiBusy)
                _aiStatusLabel.Text = $"Opened saved OCR file: {Path.GetFileName(cachePath)}";
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Failed to open saved OCR path '{cachePath}': {ex.Message}");
            if (!_aiBusy)
                _aiStatusLabel.Text = "Failed to open saved OCR location";
        }
    }

    private void TryApplySavedOcrForCurrentImage(bool allowStatusUpdate)
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return;

        if (!TryLoadSavedOcrEnvelope(imagePath, out var envelope) || envelope?.Result == null)
        {
            _savedTranslationForCurrentImage = null;
            if (_showSavedOcrCheck.Checked && _currentOverlayFromSavedCache)
            {
                _overlayBlocks.Clear();
                _lastTranslations = new List<string>();
                _ocrImagePath = null;
                _lastOcrResult = null;
                _aiOutputBox.Clear();
                _pictureBox.Invalidate();
                _currentOverlayFromSavedCache = false;
            }
            if (allowStatusUpdate && !_aiBusy && _showSavedOcrCheck.Checked)
                _aiStatusLabel.Text = "No saved OCR for this image";
            UpdateSavedCacheUiState();
            return;
        }

        _savedTranslationForCurrentImage = TryBuildSavedTranslation(envelope, out var savedTranslation) ? savedTranslation : null;

        if (!_showSavedOcrCheck.Checked)
        {
            _currentOverlayFromSavedCache = false;
            if (allowStatusUpdate && !_aiBusy)
            {
                _aiStatusLabel.Text = _savedTranslationForCurrentImage == null
                    ? "Saved OCR available"
                    : $"Saved OCR + translation available ({_savedTranslationForCurrentImage.TargetLanguage})";
            }
            UpdateSavedCacheUiState();
            return;
        }

        _ocrImagePath = imagePath;
        _lastOcrResult = envelope.Result;
        _lastTranslations = new List<string>();
        _currentOverlayFromSavedCache = true;
        SetOverlayFromOcrResult(_lastOcrResult, null);
        string cacheLabel = TryGetExistingOcrCachePath(imagePath, out string existingCachePath)
            ? Path.GetFileName(existingCachePath)
            : Path.GetFileName(GetOcrCachePath(imagePath));
        _aiOutputBox.Text = RenderOcrResult(_lastOcrResult);
        _aiOutputBox.AppendText(Environment.NewLine + Environment.NewLine + $"[Loaded from OCR_output cache: {cacheLabel}]");

        if (_savedTranslationForCurrentImage != null && _showSavedTranslationCheck.Checked)
        {
            _lastTranslations = _savedTranslationForCurrentImage.Translations?.ToList() ?? new List<string>();
            ApplyTranslationsToOverlay(_lastTranslations);
            _aiOutputBox.Text = RenderTranslatedResult(_lastOcrResult, _savedTranslationForCurrentImage);
            _aiOutputBox.AppendText(Environment.NewLine + Environment.NewLine + "[Loaded from OCR_output cache]");

            if (allowStatusUpdate && !_aiBusy)
                _aiStatusLabel.Text = $"Loaded OCR + translation ({_savedTranslationForCurrentImage.TargetLanguage})";
        }
        else if (allowStatusUpdate && !_aiBusy)
        {
            _aiStatusLabel.Text = _savedTranslationForCurrentImage == null
                ? "Loaded OCR from cache"
                : $"Loaded OCR from cache (saved translation: {_savedTranslationForCurrentImage.TargetLanguage})";
        }

        UpdateSavedCacheUiState();
    }

    private void ApplySavedTranslationToggleForCurrentImage()
    {
        if (_suppressSavedTranslationToggleEvent)
            return;
        if (!_showSavedOcrCheck.Checked)
        {
            UpdateSavedCacheUiState();
            return;
        }

        if (_showSavedTranslationCheck.Checked)
        {
            if (_lastOcrResult == null)
            {
                TryApplySavedOcrForCurrentImage(allowStatusUpdate: true);
                UpdateSavedCacheUiState();
                return;
            }

            if (_savedTranslationForCurrentImage == null)
            {
                string? imagePath = GetCurrentImagePath();
                if (!string.IsNullOrWhiteSpace(imagePath))
                    TryLoadSavedTranslationResult(imagePath, out _savedTranslationForCurrentImage);
            }

            if (_savedTranslationForCurrentImage == null)
            {
                _aiStatusLabel.Text = "No saved translation for this image";
                UpdateSavedCacheUiState();
                return;
            }

            _lastTranslations = _savedTranslationForCurrentImage.Translations?.ToList() ?? new List<string>();
            ApplyTranslationsToOverlay(_lastTranslations);
            _aiOutputBox.Text = RenderTranslatedResult(_lastOcrResult, _savedTranslationForCurrentImage);
            _aiOutputBox.AppendText(Environment.NewLine + Environment.NewLine + "[Loaded from OCR_output cache]");
            _currentOverlayFromSavedCache = true;
            _aiStatusLabel.Text = $"Showing saved translation ({_savedTranslationForCurrentImage.TargetLanguage})";
            UpdateSavedCacheUiState();
            return;
        }

        if (_lastOcrResult == null)
        {
            UpdateSavedCacheUiState();
            return;
        }

        _lastTranslations = new List<string>();
        SetOverlayFromOcrResult(_lastOcrResult, null);
        _aiOutputBox.Text = RenderOcrResult(_lastOcrResult);
        _aiOutputBox.AppendText(Environment.NewLine + Environment.NewLine + "[Loaded from OCR_output cache]");
        _currentOverlayFromSavedCache = true;
        _aiStatusLabel.Text = "Showing saved OCR text";
        UpdateSavedCacheUiState();
    }

}
