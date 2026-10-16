using System;
using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

/// <summary>
/// Theme palette for the settings window. Mirrors the main window's
/// dark/light palettes so the settings dialog matches the rest of the app.
/// Dark surfaces are slightly darker than the previous (45,45,48) to match
/// the main window's list background (25,25,25) while keeping contrast.
/// </summary>
public static class SettingsTheme
{
    public sealed class Palette
    {
        public required bool IsDark { get; init; }
        public required Color WindowBackground { get; init; }      // Tab content / general surface
        public required Color ChromeBackground { get; init; }      // Title bar, footer, tab strip
        public required Color TabSelectedBackground { get; init; }
        public required Color TabInactiveBackground { get; init; }
        public required Color ControlBackground { get; init; }     // Editable inputs
        public required Color ControlHoverBackground { get; init; }
        public required Color ListBackground { get; init; }
        public required Color NeutralButtonBackground { get; init; }
        public required Color SubtleButtonBackground { get; init; }
        public required Color PrimaryButtonBackground { get; init; }
        public required Color Foreground { get; init; }
        public required Color ForegroundMuted { get; init; }
        public required Color Border { get; init; }
        public required Color Accent { get; init; }
    }

    public static Palette Dark { get; } = new()
    {
        IsDark = true,
        WindowBackground = Color.FromArgb(30, 30, 30),
        ChromeBackground = Color.FromArgb(25, 25, 25),
        TabSelectedBackground = Color.FromArgb(45, 45, 48),
        TabInactiveBackground = Color.FromArgb(38, 38, 38),
        ControlBackground = Color.FromArgb(25, 25, 25),
        ControlHoverBackground = Color.FromArgb(60, 60, 60),
        ListBackground = Color.FromArgb(25, 25, 25),
        NeutralButtonBackground = Color.FromArgb(70, 70, 70),
        SubtleButtonBackground = Color.FromArgb(60, 60, 60),
        PrimaryButtonBackground = Color.FromArgb(0, 120, 212),
        Foreground = Color.FromArgb(240, 240, 240),
        ForegroundMuted = Color.FromArgb(180, 180, 180),
        Border = Color.FromArgb(50, 50, 50),
        Accent = Color.FromArgb(0, 120, 212)
    };

    public static Palette Light { get; } = new()
    {
        IsDark = false,
        WindowBackground = Color.FromArgb(244, 244, 247),
        ChromeBackground = Color.FromArgb(232, 232, 236),
        TabSelectedBackground = Color.FromArgb(255, 255, 255),
        TabInactiveBackground = Color.FromArgb(233, 235, 239),
        ControlBackground = Color.FromArgb(255, 255, 255),
        ControlHoverBackground = Color.FromArgb(216, 227, 240),
        ListBackground = Color.FromArgb(255, 255, 255),
        NeutralButtonBackground = Color.FromArgb(226, 231, 238),
        SubtleButtonBackground = Color.FromArgb(238, 238, 242),
        PrimaryButtonBackground = Color.FromArgb(0, 120, 212),
        Foreground = Color.FromArgb(32, 32, 32),
        ForegroundMuted = Color.FromArgb(110, 110, 110),
        Border = Color.FromArgb(198, 200, 206),
        Accent = Color.FromArgb(0, 120, 212)
    };

    public static Palette Current =>
        string.Equals(AppSettings.Current.MainThemePreset, "Light", StringComparison.OrdinalIgnoreCase)
            ? Light
            : Dark;

    /// <summary>
    /// Walks a control tree and re-applies theme colors to standard controls
    /// (labels, checkboxes, text inputs, lists). Buttons are re-applied
    /// through their remembered <see cref="SettingsButtonStyle"/> style.
    ///
    /// Control tags override the default treatment:
    ///   "keep-color" -> ForeColor is left untouched
    ///   "muted"      -> ForeColor is set to ForegroundMuted
    ///   "surface"    -> (Panels) BackColor is set to WindowBackground
    /// </summary>
    public static void ApplyToTree(Control root, Palette p)
    {
        ApplyNode(root, p);
    }

    private static void ApplyNode(Control c, Palette p)
    {
        switch (c)
        {
            case Label lbl:
                if (IsKeptColor(c)) break;
                lbl.ForeColor = IsMuted(c) ? p.ForegroundMuted : p.Foreground;
                break;
            case CheckBox chk:
                if (IsKeptColor(c)) break;
                chk.ForeColor = chk.Enabled ? p.Foreground : p.ForegroundMuted;
                break;
            case ComboBox cb:
                cb.BackColor = p.ControlBackground;
                cb.ForeColor = p.Foreground;
                break;
            case NumericUpDown num:
                num.BackColor = p.ControlBackground;
                num.ForeColor = p.Foreground;
                break;
            case TextBox tb:
                tb.BackColor = p.ControlBackground;
                tb.ForeColor = p.Foreground;
                break;
            case ListBox lb:
                lb.BackColor = p.ListBackground;
                lb.ForeColor = p.Foreground;
                break;
            case Panel pnl when c.Tag is string surfaceTag && surfaceTag == "surface":
                pnl.BackColor = p.WindowBackground;
                break;
            case Button btn:
                if (SettingsButtonStyle.TryGetStyle(btn, out string? style) && style != null)
                    SettingsButtonStyle.Reapply(btn, style, p);
                break;
        }

        foreach (Control child in c.Controls)
            ApplyNode(child, p);
    }

    private static bool IsKeptColor(Control c) =>
        c.Tag is string tag && tag.StartsWith("keep-color", StringComparison.Ordinal);

    private static bool IsMuted(Control c) =>
        c.Tag is string tag && tag.StartsWith("muted", StringComparison.Ordinal);
}
