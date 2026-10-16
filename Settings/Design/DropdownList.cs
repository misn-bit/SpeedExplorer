using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SpeedExplorer.Design;

/// <summary>
/// The popup half of Dropdown: a themed, scrollable list. It never takes focus; Dropdown keeps
/// the keyboard and drives it through MoveHot / ChooseHot, so the popup behaves like a menu.
/// </summary>
internal sealed class DropdownList : Control
{
    public const int MaxVisible = 8;

    // Shared with Dropdown, which sizes the popup before the list is shown.
    public static int RowHeightFor(float s) => (int)(Metrics.ListItemHeight * s);
    public static int PadFor(float s) => Theme.Flat ? 1 : (int)(4 * s) + 1;   // flat: just the border line

    private const TextFormatFlags TextFlags =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

    private readonly List<string> _items;
    private string? _value;
    private string _empty = "";
    private int _hot = -1;
    private int _top;

    /// <summary>Raised with the index of the chosen item.</summary>
    public event Action<int>? Chosen;

    public DropdownList(List<string> items)
    {
        _items = items;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
    }

    /// <summary>Called right before the popup is shown.</summary>
    public void Prepare(string? value, string emptyText, Size size)
    {
        _value = value;
        _empty = emptyText;
        _hot = value is null ? -1 : _items.IndexOf(value);
        int maxTop = Math.Max(0, _items.Count - MaxVisible);
        _top = Math.Clamp(_hot - MaxVisible / 2, 0, maxTop);
        Size = size;
        Invalidate();
    }

    public void MoveHot(int delta)
    {
        if (_items.Count == 0) return;
        _hot = _hot < 0
            ? (delta > 0 ? 0 : _items.Count - 1)
            : Math.Clamp(_hot + delta, 0, _items.Count - 1);
        if (_hot < _top) _top = _hot;
        else if (_hot >= _top + MaxVisible) _top = _hot - MaxVisible + 1;
        Invalidate();
    }

    public void ScrollRows(int rows)
    {
        _top = Math.Clamp(_top + rows, 0, Math.Max(0, _items.Count - MaxVisible));
        Invalidate();
    }

    public void ChooseHot()
    {
        if (_hot >= 0 && _hot < _items.Count) Chosen?.Invoke(_hot);
    }

    private int Pad => PadFor(DeviceDpi / 96f);
    private int RowHeight => RowHeightFor(DeviceDpi / 96f);

    private int IndexAt(int y)
    {
        int i = (y - Pad) / RowHeight + _top;
        return y >= Pad && i >= 0 && i < _items.Count ? i : -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        float s = DeviceDpi / 96f;
        int pad = Pad, rowH = RowHeight;

        g.Clear(p.Card);
        using (var border = new Pen(p.FieldBorder))
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

        if (_items.Count == 0)
        {
            var empty = new Rectangle(pad + (int)(8 * s), pad, Width - 2 * pad - (int)(16 * s), rowH);
            TextRenderer.DrawText(g, _empty, Font, empty, p.TextDim, TextFlags);
            return;
        }

        int visible = Math.Min(MaxVisible, _items.Count - _top);
        for (int i = 0; i < visible; i++)
        {
            int index = _top + i;
            bool chosen = _items[index] == _value;
            var row = new Rectangle(pad, pad + i * rowH, Width - 2 * pad, rowH);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (index == _hot || chosen)
            {
                using var path = Gfx.RoundedRect(row, 4f * s);
                using var brush = new SolidBrush(Theme.Flat
                    ? (chosen ? p.Accent : p.Hover)
                    : (index == _hot ? p.Selected : p.Hover));
                g.FillPath(brush, path);
            }
            if (chosen && !Theme.Flat)
            {
                float barW = 3f * s, barH = 16f * s;
                using var bar = Gfx.RoundedRect(
                    new RectangleF(row.Left + 2f * s, row.Top + (row.Height - barH) / 2f, barW, barH), barW / 2f);
                using var brush = new SolidBrush(p.Accent);
                g.FillPath(brush, bar);
            }
            g.SmoothingMode = SmoothingMode.Default;

            int padX = (int)(Metrics.FieldPadX * s);
            var text = new Rectangle(row.Left + padX, row.Top, row.Width - 2 * padX, row.Height);
            TextRenderer.DrawText(g, _items[index], Font, text,
                Theme.Flat && chosen ? p.OnAccent : p.Text, TextFlags);
        }

        if (_items.Count > MaxVisible)   // slim scroll indicator, wheel does the scrolling
        {
            float trackH = MaxVisible * rowH;
            float thumbH = Math.Max(16f * s, trackH * MaxVisible / _items.Count);
            float thumbY = pad + (trackH - thumbH) * _top / (_items.Count - MaxVisible);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Gfx.RoundedRect(new RectangleF(Width - pad - 3f * s, thumbY, 3f * s, thumbH), 1.5f * s);
            using var brush = new SolidBrush(Gfx.Lerp(p.Card, p.TextDim, 0.6f));
            g.FillPath(brush, path);
            g.SmoothingMode = SmoothingMode.Default;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = IndexAt(e.Y);
        if (i < 0 || i == _hot) return;
        _hot = i;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        int i = IndexAt(e.Y);
        if (i >= 0) Chosen?.Invoke(i);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ScrollRows(-Math.Sign(e.Delta) * 2);
    }
}
