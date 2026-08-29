using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    private static LlmImageTextResult CloneOcrResult(LlmImageTextResult? source)
    {
        if (source == null)
            return new LlmImageTextResult();

        return new LlmImageTextResult
        {
            FullText = source.FullText ?? "",
            DetectedLanguage = source.DetectedLanguage ?? "",
            Blocks = source.Blocks?
                .Select(b => new LlmImageTextBlock
                {
                    Text = b.Text ?? "",
                    X = b.X,
                    Y = b.Y,
                    W = b.W,
                    H = b.H,
                    FontSize = b.FontSize
                })
                .ToList() ?? new List<LlmImageTextBlock>()
        };
    }

    private static LlmTextTranslationResult? CloneTranslationResult(LlmTextTranslationResult? source)
    {
        if (source == null)
            return null;

        return new LlmTextTranslationResult
        {
            TargetLanguage = source.TargetLanguage ?? "",
            TranslatedFullText = source.TranslatedFullText ?? "",
            Translations = source.Translations?.ToList() ?? new List<string>()
        };
    }

    private LlmImageTextResult GetBestBaseOcrForImage(string imagePath)
    {
        if (TryLoadSavedOcrEnvelope(imagePath, out var savedEnvelope) && savedEnvelope?.Result != null)
            return CloneOcrResult(savedEnvelope.Result);
        if (string.Equals(_ocrImagePath, imagePath, StringComparison.OrdinalIgnoreCase) && _lastOcrResult != null)
            return CloneOcrResult(_lastOcrResult);
        return new LlmImageTextResult();
    }

    private LlmTextTranslationResult? GetBestSavedTranslationForImage(string imagePath)
    {
        if (TryLoadSavedOcrEnvelope(imagePath, out var savedEnvelope) &&
            savedEnvelope != null &&
            TryBuildSavedTranslation(savedEnvelope, out var savedTranslation))
        {
            return CloneTranslationResult(savedTranslation);
        }

        if (string.Equals(_ocrImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
            return CloneTranslationResult(_savedTranslationForCurrentImage);

        return null;
    }

    private static string ComposeFullTextFromBlocks(IEnumerable<LlmImageTextBlock> blocks)
        => string.Join(
            Environment.NewLine,
            blocks.Select(b => b.Text?.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Cast<string>());

}
