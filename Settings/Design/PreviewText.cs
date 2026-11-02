using System;
using System.Drawing;

namespace SpeedExplorer.Design;

/// <summary>Sample text drawn at a chosen point size, inside an input-style frame.</summary>
internal sealed class PreviewText : Control, IRowControl
{
    private string _sample = "The quick brown fox jumps over the lazy dog.";

    private const TextFormatFlags TextFlags =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak |
        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

    private Font? _font;
    private float _points = 12f;
    private string _familyName = "Segoe UI";

    public PreviewText()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        AccessibleRole = AccessibleRole.StaticText;
    }

    private Font PreviewFont
    {
        get
        {
            if (_font is not null) return _font;
            try { _font = new Font(_familyName, _points, GraphicsUnit.Point); }
            catch { _font = new Font(Font.FontFamily, _points, GraphicsUnit.Point); }
            return _font;
        }
    }

    public void SetPreview(string familyName, float points, string sample)
    {
        familyName = string.IsNullOrWhiteSpace(familyName) ? "Segoe UI" : familyName;
        sample ??= "";
        if (Math.Abs(points - _points) < 0.01f &&
            string.Equals(familyName, _familyName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(sample, _sample, StringComparison.Ordinal)) return;

        _familyName = familyName;
        _points = points;
        _sample = sample;
        _font?.Dispose();
        _font = null;
        Parent?.PerformLayout();   // row height follows the text height
        Invalidate();
    }

    public Size GetRowSize(float scale, int availableWidth)
    {
        int textWidth = Math.Max(1, availableWidth - (int)(24 * scale));
        int textH = TextRenderer.MeasureText(_sample, PreviewFont, new Size(textWidth, int.MaxValue), TextFlags).Height;
        return new Size((int)(320 * scale), textH + (int)(24 * scale));
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Current.Card);

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        float s = DeviceDpi / 96f;
        Gfx.FieldSurface(e.Graphics, ClientRectangle, s, p.Field, p.FieldBorder, null);
        var text = new Rectangle((int)(12 * s), 0, Width - (int)(24 * s), Height);
        TextRenderer.DrawText(e.Graphics, _sample, PreviewFont, text, p.Text, TextFlags);
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
