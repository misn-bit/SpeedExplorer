using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

internal static class SettingsButtonStyle
{
    public static Color NeutralColor => Color.FromArgb(70, 70, 70);
    public static Color SubtleColor => Color.FromArgb(60, 60, 60);
    public static Color PrimaryColor => Color.FromArgb(0, 120, 212);

    public static void ApplyNeutral(Button button) => Apply(button, NeutralColor);
    public static void ApplySubtle(Button button) => Apply(button, SubtleColor);
    public static void ApplyPrimary(Button button) => Apply(button, PrimaryColor);

    public static void Apply(Button button, Color backColor)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = backColor;
        button.ForeColor = Color.White;
        button.Cursor = Cursors.Hand;
        button.FlatAppearance.BorderSize = 0;
    }
}
