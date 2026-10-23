using System;
using System.Drawing;

namespace SpeedExplorer.Design;

/// <summary>Sample text drawn at a chosen point size, inside an input-style frame.</summary>
internal sealed class PreviewText : Control, IRowControl
{
    private const string Sample = "The quick brown fox jumps over the lazy dog.";

    private const TextFormatFlags TextFlags =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

    private Font? _font;
    private float _points = 12f;

    public PreviewText()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        AccessibleRole = AccessibleRole.StaticText;
    }

    private Font PreviewFont => _font ??= new Font(Font.FontFamily, _points, GraphicsUnit.Point);

    public void SetPointSize(float points)
    {
        if (Math.Abs(points - _points) < 0.01f) return;
        _points = points;
        _font?.Dispose();
        _font = null;
        Parent?.PerformLayout();   // row height follows the text height
        Invalidate();
    }

    public Size GetRowSize(float scale, int availableWidth)
    {
        int textH = TextRenderer.MeasureText(Sample, PreviewFont, new Size(int.MaxValue, int.MaxValue), TextFlags).Height;
        return new Size((int)(320 * scale), textH + (int)(24 * scale));
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Current.Card);

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        float s = DeviceDpi / 96f;
        Gfx.FieldSurface(e.Graphics, ClientRectangle, s, p.Field, p.FieldBorder, null);
        var text = new Rectangle((int)(12 * s), 0, Width - (int)(24 * s), Height);
        TextRenderer.DrawText(e.Graphics, Sample, PreviewFont, text, p.Text, TextFlags);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        _font?.Dispose();
        _font = null;
        base.OnFontChanged(e);
        Parent?.PerformLayout();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _font?.Dispose();
        base.Dispose(disposing);
    }
}
