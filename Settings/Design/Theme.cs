using System;
using System.Drawing;

namespace SpeedExplorer.Design;

internal sealed record Palette(
    bool IsDark,
    Color Page,       // window background
    Color Card,       // surface behind controls (same as Page in the flat style)
    Color Border,     // card outline and row dividers
    Color Text,
    Color TextDim,    // descriptions, section labels
    Color Accent,
    Color OnAccent,   // thumb / text drawn on top of Accent
    Color Selected,   // selected nav item (rounded) / hovered list row (flat)
    Color Hover,
    Color Field,        // text field, dropdown, secondary button
    Color FieldBorder,
    Color FieldHover,
    Color Danger);

internal enum ThemeStyle
{
    /// <summary>Soft cards, rounded corners, pill switch (Windows 11 Settings look).</summary>
    Rounded,
    /// <summary>Square, dense, hairline borders, solid-accent selection (matches the main window).</summary>
    Flat,
}

/// <summary>
/// Hard-coded palettes for both visual styles. The dark/light variant follows the app's own
/// theme setting (AppSettings.MainThemePreset), like the rest of SpeedExplorer.
/// </summary>
internal static class Theme
{
    private static readonly Palette Light = new(
        IsDark: false,
        Page: Color.FromArgb(243, 243, 243),
        Card: Color.FromArgb(255, 255, 255),
        Border: Color.FromArgb(229, 229, 229),
        Text: Color.FromArgb(26, 26, 26),
        TextDim: Color.FromArgb(96, 96, 96),
        Accent: Color.FromArgb(0, 103, 192),
        OnAccent: Color.White,
        Selected: Color.FromArgb(230, 230, 230),
        Hover: Color.FromArgb(236, 236, 236),
        Field: Color.FromArgb(251, 251, 251),
        FieldBorder: Color.FromArgb(207, 207, 207),
        FieldHover: Color.FromArgb(245, 245, 245),
        Danger: Color.FromArgb(196, 43, 28));

    private static readonly Palette Dark = new(
        IsDark: true,
        Page: Color.FromArgb(32, 32, 32),
        Card: Color.FromArgb(43, 43, 43),
        Border: Color.FromArgb(61, 61, 61),
        Text: Color.White,
        TextDim: Color.FromArgb(166, 166, 166),
        Accent: Color.FromArgb(96, 205, 255),
        OnAccent: Color.Black,
        Selected: Color.FromArgb(58, 58, 58),
        Hover: Color.FromArgb(46, 46, 46),
        Field: Color.FromArgb(50, 50, 50),
        FieldBorder: Color.FromArgb(86, 86, 86),
        FieldHover: Color.FromArgb(60, 60, 60),
        Danger: Color.FromArgb(255, 153, 164));

    // Flat: one surface color everywhere, structure comes from hairlines. Accent is the classic
    // Windows selection blue, with white text on top of it.
    private static readonly Palette FlatLight = new(
        IsDark: false,
        Page: Color.FromArgb(250, 250, 250),
        Card: Color.FromArgb(250, 250, 250),
        Border: Color.FromArgb(222, 222, 222),
        Text: Color.FromArgb(28, 28, 28),
        TextDim: Color.FromArgb(105, 105, 105),
        Accent: Color.FromArgb(0, 120, 215),
        OnAccent: Color.White,
        Selected: Color.FromArgb(225, 225, 225),
        Hover: Color.FromArgb(232, 232, 232),
        Field: Color.White,
        FieldBorder: Color.FromArgb(170, 170, 170),
        FieldHover: Color.FromArgb(240, 240, 240),
        Danger: Color.FromArgb(196, 43, 28));

    private static readonly Palette FlatDark = new(
        IsDark: true,
        Page: Color.FromArgb(30, 30, 30),
        Card: Color.FromArgb(30, 30, 30),
        Border: Color.FromArgb(62, 62, 62),
        Text: Color.FromArgb(240, 240, 240),
        TextDim: Color.FromArgb(158, 158, 158),
        Accent: Color.FromArgb(0, 120, 215),
        OnAccent: Color.White,
        Selected: Color.FromArgb(56, 56, 56),
        Hover: Color.FromArgb(56, 56, 56),
        Field: Color.FromArgb(34, 34, 34),
        FieldBorder: Color.FromArgb(90, 90, 90),
        FieldHover: Color.FromArgb(52, 52, 52),
        Danger: Color.FromArgb(255, 110, 110));

    // Square/flat is the default look; the rounded style remains available through Style.
    private static ThemeStyle _style = ThemeStyle.Flat;

    public static Palette Current { get; private set; } = Detect();

    /// <summary>Visual style. Set once at startup, before the first window is created.</summary>
    public static ThemeStyle Style
    {
        get => _style;
        set
        {
            _style = value;
            Refresh();
        }
    }

    public static bool Flat => _style == ThemeStyle.Flat;

    /// <summary>Raised when the active palette changes (not on a background thread here).</summary>
    public static event Action? Changed;

    /// <summary>Re-evaluates the palette after the app theme setting changed.</summary>
    public static void Refresh()
    {
        var next = Detect();
        if (next == Current) return;
        Current = next;
        Changed?.Invoke();
    }

    private static Palette Detect()
    {
        bool dark = !string.Equals(
            AppSettings.Current.MainThemePreset, "Light", StringComparison.OrdinalIgnoreCase);
        if (Flat) return dark ? FlatDark : FlatLight;
        return dark ? Dark : Light;
    }
}

/// <summary>Sizes in pixels at 100% scale; multiply by DeviceDpi / 96. The flat style is denser.</summary>
internal static class Metrics
{
    public static int RowPadX => Theme.Flat ? 12 : 16;
    public static int RowPadY => Theme.Flat ? 8 : 12;
    public static int FieldHeight => Theme.Flat ? 28 : 34;
    public static int FieldPadX => Theme.Flat ? 8 : 12;
    public static int ButtonHeight => Theme.Flat ? 28 : 32;
    public static int NavItemHeight => Theme.Flat ? 28 : 38;
    public static int ListItemHeight => Theme.Flat ? 26 : 32;
    public static float HeadingPoints => Theme.Flat ? 14f : 20f;
}
