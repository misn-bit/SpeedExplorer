using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SpeedExplorer.Design;

internal enum ButtonKind { Secondary, Primary }

/// <summary>
/// Button (rounded in the rounded style, square in the flat style).
/// Secondary is the quiet outlined default; Primary is the accent-filled one
/// (use at most one per page). Sizes itself to its text through IRowControl.
/// </summary>
internal sealed class FlatButton : Control, IRowControl
{
    private const TextFormatFlags TextFlags =
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

    private ButtonKind _kind;
    private bool _hover, _down;

    public FlatButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable | ControlStyles.StandardClick, true);
        SetStyle(ControlStyles.StandardDoubleClick, false);
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
    }

    protected override Size DefaultSize => new(96, 32);

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ButtonKind Kind
    {
        get => _kind;
        set { if (_kind == value) return; _kind = value; Invalidate(); }
    }

    public Size GetRowSize(float scale)
    {
        int textW = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue), TextFlags).Width;
        return new Size(Math.Max((int)(88 * scale), textW + (int)(32 * scale)), (int)(Metrics.ButtonHeight * scale));
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Current.Card);

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        float s = DeviceDpi / 96f;
        bool primary = _kind == ButtonKind.Primary;

        Color fill, line, text;
        if (primary)
        {
            fill = _down ? Gfx.Lerp(p.Accent, p.Page, 0.30f)
                 : _hover ? Gfx.Lerp(p.Accent, p.Page, 0.12f)
                 : p.Accent;
            line = fill;
            text = p.OnAccent;
        }
        else
        {
            fill = _down ? Gfx.Lerp(p.FieldHover, p.Text, 0.06f)
                 : _hover ? p.FieldHover
                 : p.Field;
            line = p.FieldBorder;
            text = p.Text;
        }
        if (!Enabled)
        {
            fill = primary ? Gfx.Lerp(p.Card, p.TextDim, 0.35f) : p.Field;
            line = primary ? fill : p.Border;
            text = primary ? p.Card : p.TextDim;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using (var path = Gfx.RoundedRect(r, 4f * s))
        using (var brush = new SolidBrush(fill))
        using (var pen = new Pen(line))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        if (Focused && ShowFocusCues)
        {
            using var ring = Gfx.RoundedRect(RectangleF.Inflate(r, -2f * s, -2f * s), 3f * s);
            using var pen = new Pen(primary ? p.OnAccent : p.Text, 1.5f * s);
            g.DrawPath(pen, ring);
        }
        g.SmoothingMode = SmoothingMode.Default;

        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text, TextFlags);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _down = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _down = false;
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space) { _down = true; Invalidate(); }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode != Keys.Space || !_down) return;
        _down = false;
        Invalidate();
        OnClick(EventArgs.Empty);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
        Parent?.PerformLayout();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Parent?.PerformLayout();
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); _down = false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
}
