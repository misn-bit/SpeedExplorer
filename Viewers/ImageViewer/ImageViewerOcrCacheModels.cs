using System.Collections.Generic;
using System.Drawing;

namespace SpeedExplorer;

internal sealed class OcrCacheEnvelope
{
    public string SourcePath { get; set; } = "";
    public long SourceLength { get; set; }
    public long SourceLastWriteUtcTicks { get; set; }
    public long SavedUtcTicks { get; set; }
    public string ModelId { get; set; } = "";
    public LlmImageTextResult? Result { get; set; }
    public string TranslationTargetLanguage { get; set; } = "";
    public string TranslationSourceLanguage { get; set; } = "";
    public string TranslationModelId { get; set; } = "";
    public string TranslationFullText { get; set; } = "";
    public List<string> TranslationLines { get; set; } = new();
    public long TranslationSavedUtcTicks { get; set; }
    public OverlayStyleDefaults? OverlayDefaults { get; set; }
    public List<OcrOverlayBlockOverride> OverlayOverrides { get; set; } = new();
}

internal sealed class OcrOverlayBlockOverride
{
    public int SourceIndex { get; set; }
    public string? Text { get; set; }
    public string? TranslationText { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }
    public float FontSize { get; set; }
    public int? TextColorArgb { get; set; }
    public int? TextOutlineColorArgb { get; set; }
    public StringAlignment? TextAlignment { get; set; }
    public StringAlignment? TextVerticalAlignment { get; set; }
    public bool? TextOutlineVisible { get; set; }
    public int? BoxFillColorArgb { get; set; }
    public int? BoxBorderColorArgb { get; set; }
    public bool? BoxFillVisible { get; set; }
    public bool? BoxBorderVisible { get; set; }
}
