using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    private static string GetOcrOutputDirectory()
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OCR_output");

    private static string ComputeNormalizedPathHash(string imagePath)
    {
        string normalized = Path.GetFullPath(imagePath).Trim().ToLowerInvariant();
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hashBytes);
    }

    private static string SanitizeFileComponent(string value, int maxLen = 64)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "image";

        var sb = new StringBuilder(value.Length);
        foreach (char ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')
                sb.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch == '.')
                sb.Append('_');
        }

        string sanitized = sb.ToString().Trim('_');
        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "image";
        if (sanitized.Length > maxLen)
            sanitized = sanitized.Substring(0, maxLen);
        return sanitized;
    }

    private static string GetOcrCachePath(string imagePath)
    {
        string hash = ComputeNormalizedPathHash(imagePath);
        string imageName = SanitizeFileComponent(Path.GetFileNameWithoutExtension(imagePath));
        string shortHash = hash.Length > 12 ? hash.Substring(0, 12) : hash;
        return Path.Combine(GetOcrOutputDirectory(), $"{imageName}__{shortHash}.json");
    }

    private static string GetLegacyOcrCachePath(string imagePath)
    {
        string hash = ComputeNormalizedPathHash(imagePath);
        return Path.Combine(GetOcrOutputDirectory(), $"{hash}.json");
    }

    private static IEnumerable<string> EnumerateOcrCacheCandidates(string imagePath)
    {
        yield return GetOcrCachePath(imagePath);
        yield return GetLegacyOcrCachePath(imagePath);
    }

    private static bool TryGetExistingOcrCachePath(string imagePath, out string cachePath)
    {
        foreach (var candidate in EnumerateOcrCacheCandidates(imagePath))
        {
            if (File.Exists(candidate))
            {
                cachePath = candidate;
                return true;
            }
        }

        cachePath = GetOcrCachePath(imagePath);
        return false;
    }

    private static bool TryGetSourceStamp(string imagePath, out long length, out long lastWriteUtcTicks)
    {
        length = 0;
        lastWriteUtcTicks = 0;

        try
        {
            var info = new FileInfo(imagePath);
            if (!info.Exists)
                return false;

            length = info.Length;
            lastWriteUtcTicks = info.LastWriteTimeUtc.Ticks;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static OcrCacheEnvelope? TryReadOcrEnvelopeUnchecked(string cachePath)
        => ImageViewerOcrCacheSerializer.TryRead(cachePath, OcrCacheJsonSeparator);

    private static string SerializeOcrCacheEnvelopeForDisk(OcrCacheEnvelope envelope)
        => ImageViewerOcrCacheSerializer.Serialize(envelope, OcrCacheJsonSeparator);

    private static bool TryLoadSavedOcrEnvelope(string imagePath, out OcrCacheEnvelope? envelope)
    {
        envelope = null;

        if (!TryGetSourceStamp(imagePath, out long srcLength, out long srcTicks))
            return false;

        if (!TryGetExistingOcrCachePath(imagePath, out string cachePath))
            return false;

        var loaded = TryReadOcrEnvelopeUnchecked(cachePath);
        if (loaded?.Result == null)
            return false;

        if (loaded.SourceLength != srcLength || loaded.SourceLastWriteUtcTicks != srcTicks)
            return false;

        if (string.IsNullOrWhiteSpace(loaded.Result.FullText) && (loaded.Result.Blocks == null || loaded.Result.Blocks.Count == 0))
            return false;

        loaded.Result.Blocks ??= new List<LlmImageTextBlock>();
        loaded.TranslationLines ??= new List<string>();
        envelope = loaded;
        return true;
    }

    private static bool TryBuildSavedTranslation(OcrCacheEnvelope envelope, out LlmTextTranslationResult? translation)
    {
        translation = null;
        var lines = envelope.TranslationLines ?? new List<string>();
        bool hasLines = lines.Any(t => !string.IsNullOrWhiteSpace(t));
        bool hasFull = !string.IsNullOrWhiteSpace(envelope.TranslationFullText);
        if (!hasLines && !hasFull)
            return false;

        // Keep empty entries so translation index N continues to refer to OCR block N.
        // Filtering them here shifts every following translation onto the wrong box.
        var normalized = lines
            .Select(t => t?.Trim() ?? "")
            .ToList();

        translation = new LlmTextTranslationResult
        {
            TargetLanguage = string.IsNullOrWhiteSpace(envelope.TranslationTargetLanguage) ? "Unknown" : envelope.TranslationTargetLanguage,
            TranslatedFullText = envelope.TranslationFullText ?? "",
            Translations = normalized
        };

        if (translation.Translations.Count == 0 && !string.IsNullOrWhiteSpace(translation.TranslatedFullText))
            translation.Translations = new List<string> { translation.TranslatedFullText };
        if (string.IsNullOrWhiteSpace(translation.TranslatedFullText) && translation.Translations.Count > 0)
            translation.TranslatedFullText = string.Join(Environment.NewLine, translation.Translations);

        return true;
    }

    private static bool TryLoadSavedTranslationResult(string imagePath, out LlmTextTranslationResult? translation)
    {
        translation = null;
        if (!TryLoadSavedOcrEnvelope(imagePath, out var envelope) || envelope == null)
            return false;

        return TryBuildSavedTranslation(envelope, out translation);
    }

    private static string NormalizeLanguageKey(string? language)
        => string.IsNullOrWhiteSpace(language) ? "" : language.Trim().ToLowerInvariant();

    private void ApplyLoadedOcrToViewer(string imagePath, LlmImageTextResult ocr, bool fromSavedCache)
    {
        _ocrImagePath = imagePath;
        _lastOcrResult = ocr;
        _lastTranslations = new List<string>();
        SetOverlayFromOcrResult(ocr, null);
        ApplyCachedOverlayOverridesForCurrentImage();
        _aiOutputBox.Text = RenderOcrResult(ocr);
        _currentOverlayFromSavedCache = fromSavedCache;

        if (!fromSavedCache)
            return;

        string cacheLabel = TryGetExistingOcrCachePath(imagePath, out string existingCachePath)
            ? Path.GetFileName(existingCachePath)
            : Path.GetFileName(GetOcrCachePath(imagePath));
        _aiOutputBox.AppendText(Environment.NewLine + Environment.NewLine + $"[Loaded from OCR_output cache: {cacheLabel}]");
    }

    private static void SaveOcrResultToCache(string imagePath, string? modelId, LlmImageTextResult ocr)
    {
        if (ocr == null)
            return;

        if (!TryGetSourceStamp(imagePath, out long srcLength, out long srcTicks))
            return;

        try
        {
            Directory.CreateDirectory(GetOcrOutputDirectory());
            string cachePath = GetOcrCachePath(imagePath);
            var envelope = TryReadOcrEnvelopeUnchecked(cachePath) ?? new OcrCacheEnvelope();

            envelope.SourcePath = Path.GetFullPath(imagePath);
            envelope.SourceLength = srcLength;
            envelope.SourceLastWriteUtcTicks = srcTicks;
            envelope.SavedUtcTicks = DateTime.UtcNow.Ticks;
            if (!string.IsNullOrWhiteSpace(modelId))
                envelope.ModelId = modelId!;
            envelope.Result = ocr;
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
            LlmDebugLogger.LogError($"Failed to save OCR cache for '{imagePath}': {ex.Message}");
        }
    }

    private static void SaveTranslationToCache(string imagePath, string? modelId, LlmImageTextResult ocr, LlmTextTranslationResult translation)
    {
        if (ocr == null || translation == null)
            return;

        if (!TryGetSourceStamp(imagePath, out long srcLength, out long srcTicks))
            return;

        try
        {
            Directory.CreateDirectory(GetOcrOutputDirectory());
            string cachePath = GetOcrCachePath(imagePath);
            var envelope = TryReadOcrEnvelopeUnchecked(cachePath) ?? new OcrCacheEnvelope();

            envelope.SourcePath = Path.GetFullPath(imagePath);
            envelope.SourceLength = srcLength;
            envelope.SourceLastWriteUtcTicks = srcTicks;
            envelope.SavedUtcTicks = DateTime.UtcNow.Ticks;
            if (!string.IsNullOrWhiteSpace(modelId))
            {
                envelope.ModelId = modelId!;
                envelope.TranslationModelId = modelId!;
            }
            envelope.Result = ocr;
            envelope.TranslationTargetLanguage = translation.TargetLanguage ?? "";
            envelope.TranslationSourceLanguage = ocr.DetectedLanguage ?? "";
            envelope.TranslationFullText = translation.TranslatedFullText ?? "";
            // TranslationLines is parallel to OCR blocks; empty translations must remain
            // as placeholders instead of shifting later entries to earlier boxes.
            envelope.TranslationLines = translation.Translations?
                .Select(t => t?.Trim() ?? "")
                .ToList() ?? new List<string>();
            envelope.TranslationSavedUtcTicks = DateTime.UtcNow.Ticks;

            if (string.IsNullOrWhiteSpace(envelope.TranslationFullText) && envelope.TranslationLines.Count > 0)
                envelope.TranslationFullText = string.Join(Environment.NewLine, envelope.TranslationLines);

            File.WriteAllText(cachePath, SerializeOcrCacheEnvelopeForDisk(envelope));
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Failed to save translation cache for '{imagePath}': {ex.Message}");
        }
    }

    private void SetShowSavedTranslationChecked(bool value, bool updatePreference = false)
    {
        if (updatePreference)
        {
            _showSavedTranslationPreferred = value;
            _settings.ImageViewerShowSavedTranslation = value;
            _settings.Save();
        }

        _suppressSavedTranslationToggleEvent = true;
        try
        {
            _showSavedTranslationCheck.Checked = value;
        }
        finally
        {
            _suppressSavedTranslationToggleEvent = false;
        }
    }

    private void UpdateSavedCacheUiState()
    {
        bool hasSaved = false;
        bool hasSavedTranslation = false;
        string? imagePath = GetCurrentImagePath();
        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            hasSaved = TryGetExistingOcrCachePath(imagePath, out _);
            hasSavedTranslation =
                TryLoadSavedOcrEnvelope(imagePath, out var envelope) &&
                envelope != null &&
                TryBuildSavedTranslation(envelope, out _);
        }

        bool currentImageActive = IsCurrentImageActivelyProcessing();
        _openSavedOcrFileBtn.Enabled = hasSaved;
        _clearOverlayBtn.Enabled = hasSaved && !currentImageActive;
        _deleteSavedTranslationBtn.Enabled = hasSavedTranslation && !currentImageActive;
        _showSavedOcrCheck.Enabled = _currentImage != null;

        _showSavedTranslationCheck.Enabled = _showSavedOcrCheck.Checked && _savedTranslationForCurrentImage != null;
    }

    private void OnShowSavedTranslationToggled()
    {
        if (!_suppressSavedTranslationToggleEvent)
        {
            _showSavedTranslationPreferred = _showSavedTranslationCheck.Checked;
            _settings.ImageViewerShowSavedTranslation = _showSavedTranslationCheck.Checked;
            _settings.Save();
        }

        ApplySavedTranslationToggleForCurrentImage();
    }

    private void OnShowSavedOcrToggled()
    {
        _settings.ImageViewerShowSavedOcr = _showSavedOcrCheck.Checked;
        _settings.Save();

        if (_showSavedOcrCheck.Checked)
        {
            TryApplySavedOcrForCurrentImage(allowStatusUpdate: true);
        }
        else
        {
            if (_currentOverlayFromSavedCache)
            {
                _overlayBlocks.Clear();
                _lastTranslations = new List<string>();
                _savedTranslationForCurrentImage = null;
                _ocrImagePath = null;
                _lastOcrResult = null;
                _aiOutputBox.Clear();
                _pictureBox.Invalidate();
                _currentOverlayFromSavedCache = false;
            }
            if (!_aiBusy)
                _aiStatusLabel.Text = "Saved OCR hidden";
        }

        UpdateSavedCacheUiState();
    }

}
