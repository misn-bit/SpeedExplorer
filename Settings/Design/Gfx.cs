using System.Drawing;
using System.Drawing.Drawing2D;

namespace SpeedExplorer.Design;

internal static class Gfx
{
    /// <summary>
    /// Rounded rectangle. With radius = height / 2 it becomes a pill.
    /// In the flat style every corner is square, so this returns a plain rectangle —
    /// unless forceRounded is set (toggle switch and slider keep their round shape).
    /// </summary>
    public static GraphicsPath RoundedRect(RectangleF r, float radius) => RoundedRect(r, radius, false);

    public static GraphicsPath RoundedRect(RectangleF r, float radius, bool forceRounded)
    {
        var path = new GraphicsPath();
        if (!forceRounded && (Theme.Flat || radius < 0.5f))
        {
            path.AddRectangle(r);
            return path;
        }

        float d = radius * 2f;
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// Input-style surface. Rounded style: rounded fill and hairline, optional 2px accent line along the bottom.
    /// Flat style: square fill and a 1px border that turns accent-colored when accent is given.
    /// </summary>
    public static void FieldSurface(Graphics g, Rectangle bounds, float s, Color fill, Color border, Color? accent)
    {
        if (Theme.Flat)
        {
            using (var brush = new SolidBrush(fill))
                g.FillRectangle(brush, bounds);
            using (var pen = new Pen(accent ?? border))
                g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
            return;
        }

        var r = new RectangleF(bounds.X + 0.5f, bounds.Y + 0.5f, bounds.Width - 1f, bounds.Height - 1f);
        var oldMode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var path = RoundedRect(r, 4f * s);
        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
        using (var pen = new Pen(border)) g.DrawPath(pen, path);

        if (accent is Color a)
        {
            var state = g.Save();
            g.SetClip(path, CombineMode.Intersect);
            using (var brush = new SolidBrush(a))
                g.FillRectangle(brush, r.Left, r.Bottom - 2f * s, r.Width, 2f * s);
            g.Restore(state);
        }

        g.SmoothingMode = oldMode;
    }

    public static Color Lerp(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t),
        (int)(a.G + (b.G - a.G) * t),
        (int)(a.B + (b.B - a.B) * t));
}
