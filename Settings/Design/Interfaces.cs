using System.Drawing;

namespace SpeedExplorer.Design;

/// <summary>
/// A control that can sit in a settings row and knows its own size.
/// SettingsGroup asks for it on every layout, so sizes stay right at any DPI.
/// </summary>
internal interface IRowControl
{
    /// <summary>Size in device pixels. scale is 1.0 at 100% (96 dpi). Width is ignored for stacked rows.</summary>
    Size GetRowSize(float scale);
}

/// <summary>A control that holds native child controls and has to recolor them itself.</summary>
internal interface IThemed
{
    void ApplyTheme(Palette palette);
}
