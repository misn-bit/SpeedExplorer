using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;

namespace SpeedExplorer.Design;

/// <summary>
/// Sidebar navigation: icon + label rows, hover, selection pill, arrow-key support.
/// Icons are glyphs from Segoe Fluent Icons (Windows 11) / Segoe MDL2 Assets (Windows 10),
/// both already on the machine, so no image files are needed.
/// </summary>
internal sealed class NavList : Control
{
    private const TextFormatFlags LabelFlags =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

    private const TextFormatFlags GlyphFlags =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

    private static readonly string IconFamily = PickIconFamily();

    private readonly List<(string Text, string Glyph)> _items = [];
    private readonly ToolTip _toolTip = new();
    private Font? _iconFont;
    private int _selected;
    private int _hover = -1;
    private int _tooltipIndex = -1;

    public event EventHandler? SelectedIndexChanged;

    public NavList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        TabStop = true;
    }

    private Font IconFont => _iconFont ??= new Font(IconFamily, Font.Size + 1.5f, GraphicsUnit.Point);
    private bool IsCompact => Width < 176f * (DeviceDpi / 96f);

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value == _selected || value < 0 || value >= _items.Count) return;
            _selected = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Add(string text, string glyph)
    {
        _items.Add((text, glyph));
        Invalidate();
    }

    private Rectangle ItemRect(int i)
    {
        float s = DeviceDpi / 96f;
        bool flat = Theme.Flat;
        int pad = (int)((flat ? 2 : 8) * s), h = (int)(Metrics.NavItemHeight * s);
        int gap = (int)((flat ? 0 : 2) * s), top = (int)((flat ? 6 : 12) * s);
        int divider = flat ? Math.Max(1, (int)Math.Round(s)) : 0;   // room for the right-hand line
        return new Rectangle(pad, top + i * (h + gap), Width - 2 * pad - divider, h);
    }

    private int HitTest(Point pt)
    {
        for (int i = 0; i < _items.Count; i++)
            if (ItemRect(i).Contains(pt)) return i;
        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        float s = DeviceDpi / 96f;

        if (Theme.Flat)
        {
            PaintFlat(g, p, s);
            return;
        }

        for (int i = 0; i < _items.Count; i++)
        {
            var r = ItemRect(i);
            bool selected = i == _selected;
            bool compact = IsCompact;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (selected || i == _hover)
            {
                using var path = Gfx.RoundedRect(r, 6f * s);
                using var brush = new SolidBrush(selected ? p.Selected : p.Hover);
                g.FillPath(brush, path);
            }
            if (selected)
            {
                float barW = 3f * s, barH = 16f * s;
                using var bar = Gfx.RoundedRect(
                    new RectangleF(r.Left + 2f * s, r.Top + (r.Height - barH) / 2f, barW, barH), barW / 2f);
                using var brush = new SolidBrush(p.Accent);
                g.FillPath(brush, bar);
            }
            if (selected && Focused && ShowFocusCues)
            {
                using var ring = Gfx.RoundedRect(RectangleF.Inflate(r, -1f, -1f), 6f * s);
                using var pen = new Pen(p.Text, 1.5f * s);
                g.DrawPath(pen, ring);
            }
            g.SmoothingMode = SmoothingMode.Default;

            var (text, glyph) = _items[i];
            int iconWidth = (int)(24 * s);
            int iconLeft = compact
                ? r.Left + (r.Width - iconWidth) / 2 + (int)(2 * s)
                : r.Left + (int)(14 * s);
            var iconRect = new Rectangle(iconLeft, r.Top, iconWidth, r.Height);
            TextRenderer.DrawText(g, glyph, IconFont, iconRect, p.Text, GlyphFlags);
            if (!compact)
            {
                var textRect = new Rectangle(r.Left + (int)(46 * s), r.Top, r.Width - (int)(54 * s), r.Height);
                TextRenderer.DrawText(g, text, Font, textRect, p.Text, LabelFlags);
            }
        }
    }

    /// <summary>Flat look: full-width rows, solid accent for the selected one, gray hover, 1px line on the right.</summary>
    private void PaintFlat(Graphics g, Palette p, float s)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            var r = ItemRect(i);
            bool selected = i == _selected;
            bool compact = IsCompact;
            if (selected || i == _hover)
            {
                using var brush = new SolidBrush(selected ? p.Accent : p.Hover);
                g.FillRectangle(brush, r);
            }
            if (selected && Focused && ShowFocusCues)
            {
                using var pen = new Pen(p.OnAccent) { DashStyle = DashStyle.Dot };
                g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
            }

            Color fore = selected ? p.OnAccent : p.Text;
            var (text, glyph) = _items[i];
            int iconWidth = (int)(24 * s);
            int iconLeft = compact ? r.Left + (r.Width - iconWidth) / 2 : r.Left + (int)(10 * s);
            var iconRect = new Rectangle(iconLeft, r.Top, iconWidth, r.Height);
            TextRenderer.DrawText(g, glyph, IconFont, iconRect, fore, GlyphFlags);
            if (!compact)
            {
                var textRect = new Rectangle(r.Left + (int)(38 * s), r.Top, r.Width - (int)(46 * s), r.Height);
                TextRenderer.DrawText(g, text, Font, textRect, fore, LabelFlags);
            }
        }

        using var line = new Pen(p.Border);
        g.DrawLine(line, Width - 1, 0, Width - 1, Height);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SetHover(HitTest(e.Location));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetHover(-1);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (IsHandleCreated)
            SetHover(HitTest(PointToClient(Cursor.Position)));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int i = HitTest(e.Location);
        if (i >= 0) SelectedIndex = i;
    }

    private void SetHover(int i)
    {
        if (i != _hover)
        {
            _hover = i;
            Invalidate();
        }

        int tooltipIndex = IsCompact ? i : -1;
        if (tooltipIndex == _tooltipIndex) return;
        _tooltipIndex = tooltipIndex;
        _toolTip.SetToolTip(this, tooltipIndex >= 0 ? _items[tooltipIndex].Text : string.Empty);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Down) SelectedIndex = Math.Min(_selected + 1, _items.Count - 1);
        else if (e.KeyCode == Keys.Up) SelectedIndex = Math.Max(_selected - 1, 0);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnFontChanged(EventArgs e)
    {
        _iconFont?.Dispose();
        _iconFont = null;
        base.OnFontChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _iconFont?.Dispose();
            _toolTip.Dispose();
        }
        base.Dispose(disposing);
    }

    private static string PickIconFamily()
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    }
}
