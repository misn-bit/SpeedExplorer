using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace SpeedExplorer;

internal static class SettingsButtonStyle
{
    // Remembers the style of every styled button so a theme change can
    // re-apply the correct colors without touching the button's own Tag
    // (which is used for other purposes, e.g. hotkey action names).
    private static readonly ConditionalWeakTable<Button, string> StyleByButton = new();

    public static Color NeutralColor => SettingsTheme.Current.NeutralButtonBackground;
    public static Color SubtleColor => SettingsTheme.Current.SubtleButtonBackground;
    public static Color PrimaryColor => SettingsTheme.Current.PrimaryButtonBackground;

    public static void ApplyNeutral(Button button)
        => ApplyNeutral(button, SettingsTheme.Current);

    public static void ApplyNeutral(Button button, SettingsTheme.Palette palette)
        => Apply(button, "neutral", palette.NeutralButtonBackground, palette.Foreground);

    public static void ApplySubtle(Button button)
        => ApplySubtle(button, SettingsTheme.Current);

    public static void ApplySubtle(Button button, SettingsTheme.Palette palette)
        => Apply(button, "subtle", palette.SubtleButtonBackground, palette.Foreground);

    public static void ApplyPrimary(Button button)
        => ApplyPrimary(button, SettingsTheme.Current);

    public static void ApplyPrimary(Button button, SettingsTheme.Palette palette)
        => Apply(button, "primary", palette.PrimaryButtonBackground, Color.White);

    public static bool TryGetStyle(Button button, out string? style)
        => StyleByButton.TryGetValue(button, out style);

    public static void Reapply(Button button, string style, SettingsTheme.Palette palette)
    {
        switch (style)
        {
            case "primary":
                ApplyPrimary(button, palette);
                break;
            case "subtle":
                ApplySubtle(button, palette);
                break;
            default:
                ApplyNeutral(button, palette);
                break;
        }
    }

    private static void Apply(Button button, string style, Color backColor, Color foreColor)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = backColor;
        button.ForeColor = foreColor;
        button.Cursor = Cursors.Hand;
        button.FlatAppearance.BorderSize = 0;
        StyleByButton.TryAdd(button, style);
    }
}
