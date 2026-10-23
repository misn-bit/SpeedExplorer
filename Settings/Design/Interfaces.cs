using System.Drawing;

namespace SpeedExplorer.Design;

/// <summary>
/// A control that can sit in a settings row and knows its own size.
/// SettingsGroup asks for it on every layout, so sizes stay right at any DPI.
/// </summary>
internal interface IRowControl
{
    /// <summary>Size in device pixels. Width is ignored for stacked rows; availableWidth is the row's content width.</summary>
    Size GetRowSize(float scale, int availableWidth);
}

/// <summary>A control that holds native child controls and has to recolor them itself.</summary>
internal interface IThemed
{
    void ApplyTheme(Palette palette);
}
