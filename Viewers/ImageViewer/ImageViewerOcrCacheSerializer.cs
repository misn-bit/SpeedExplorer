using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SpeedExplorer;

internal static class ImageViewerOcrCacheSerializer
{
    public static OcrCacheEnvelope? TryRead(string cachePath, string separator)
    {
        try
        {
            if (!File.Exists(cachePath))
                return null;

            string raw = File.ReadAllText(cachePath);
            string json = ImageViewerOcrCachePayload.ExtractJsonPayload(raw, separator);
            var envelope = JsonSerializer.Deserialize<OcrCacheEnvelope>(json);
            if (envelope == null)
                return null;

            envelope.TranslationLines ??= new List<string>();
            envelope.OverlayOverrides ??= new List<OcrOverlayBlockOverride>();
            return envelope;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    public static string Serialize(OcrCacheEnvelope envelope, string separator)
    {
        string json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        string cleanText = BuildCleanTextBlock(envelope);
        if (string.IsNullOrWhiteSpace(cleanText))
            return $"{separator}{Environment.NewLine}{json}";

        return $"{cleanText}{Environment.NewLine}{Environment.NewLine}{separator}{Environment.NewLine}{json}";
    }

    private static string BuildCleanTextBlock(OcrCacheEnvelope envelope)
    {
        string ocrText = envelope.Result?.FullText?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(ocrText))
        {
            ocrText = string.Join(
                Environment.NewLine,
                envelope.Result?.Blocks?
                    .Select(b => b?.Text?.Trim())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Cast<string>() ?? Array.Empty<string>());
        }

        string translated = envelope.TranslationFullText?.Trim() ?? "";
        var translationLines = envelope.TranslationLines ?? new List<string>();
        if (string.IsNullOrWhiteSpace(translated) && translationLines.Count > 0)
        {
            translated = string.Join(
                Environment.NewLine,
                translationLines
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim()));
        }

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(ocrText))
            sb.AppendLine(ocrText);
        if (!string.IsNullOrWhiteSpace(translated))
        {
            if (sb.Length > 0)
                sb.AppendLine();
            sb.AppendLine(translated);
        }

        return sb.ToString().TrimEnd();
    }
}
