using System.Collections.Generic;
using System.Drawing;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    private sealed class OverlayTextBlock
    {
        public int SourceIndex { get; set; }
        public string SourceText { get; set; } = "";
        public string DisplayText { get; set; } = "";
        public RectangleF NormalizedRect { get; set; }
        public float NormalizedFontSize { get; set; }
        public int? TextColorArgb { get; set; }
        public int? TextOutlineColorArgb { get; set; }
        public StringAlignment? TextAlignment { get; set; }
        public StringAlignment? TextVerticalAlignment { get; set; }
        public bool? TextOutlineVisible { get; set; }
        public int? BoxFillColorArgb { get; set; }
        public int? BoxBorderColorArgb { get; set; }
        public bool? BoxFillVisible { get; set; }
        public bool? BoxBorderVisible { get; set; }
        public bool IsManualBox { get; set; }
        public bool IsPendingManualBox { get; set; }
        public bool HasUserOverride { get; set; }
    }

    private enum OverlayDragMode
    {
        None,
        Move,
        ResizeLeft,
        ResizeRight,
        ResizeTop,
        ResizeBottom,
        ResizeTopLeft,
        ResizeTopRight,
        ResizeBottomLeft,
        ResizeBottomRight
    }

    private sealed class ManualOcrRegion
    {
        public RectangleF NormalizedRect { get; set; }
    }

    private sealed class ManualOcrSnippet
    {
        public RectangleF NormalizedRect { get; set; }
        public string TempPath { get; set; } = "";
    }

    private sealed class ImageAiJob
    {
        public string ImagePath { get; set; } = "";
        public bool WithTranslation { get; set; }
        public bool UseMaximumEffortManualTranslation { get; set; }
        public bool UseOcrReasoning { get; set; }
        public bool UseTranslationReasoning { get; set; }
        public string TargetLanguage { get; set; } = "English";
        public string SourceLanguageHint { get; set; } = "";
        public string OcrHint { get; set; } = "";
        public string TranslationContextHint { get; set; } = "";
        public string? ModelId { get; set; }
        public List<ManualOcrSnippet> ManualSnippets { get; set; } = new();
    }

    private sealed class ImageAiJobResult
    {
        public string ImagePath { get; set; } = "";
        public string StatusText { get; set; } = "";
        public string? ErrorText { get; set; }
        public bool FromSavedCache { get; set; }
        public bool ShowSavedTranslation { get; set; }
        public LlmImageTextResult? Ocr { get; set; }
        public LlmTextTranslationResult? Translation { get; set; }
    }

    private sealed class OverlayBlockEditResult
    {
        public string OcrText { get; set; } = "";
        // Null means the caller is changing only geometry/text and must preserve the
        // existing translation. An empty string is an intentional clear from the editor.
        public string? TranslationText { get; set; }
        public RectangleF NormalizedRect { get; set; }
        public float NormalizedFontSize { get; set; }
        public int? TextColorArgb { get; set; }
        public int? TextOutlineColorArgb { get; set; }
        public StringAlignment? TextAlignment { get; set; }
        public StringAlignment? TextVerticalAlignment { get; set; }
        public bool? TextOutlineVisible { get; set; }
        public int? BoxFillColorArgb { get; set; }
        public int? BoxBorderColorArgb { get; set; }
        public bool? BoxFillVisible { get; set; }
        public bool? BoxBorderVisible { get; set; }
        public bool StyleSettingsChanged { get; set; }
    }

}
