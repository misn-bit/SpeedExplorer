using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace SpeedExplorer.Design;

/// <summary>
/// The popup half of Dropdown: a themed, searchable list with a mouse-driven scrollbar. It never
/// takes focus for ordinary lists; searchable lists focus their embedded search field.
/// </summary>
internal sealed class DropdownList : Control
{
    public const int MaxVisible = 8;

    // Shared with Dropdown, which sizes the popup before the list is shown.
    public static int RowHeightFor(float s) => (int)(Metrics.ListItemHeight * s);
    public static int PadFor(float s) => Theme.Flat ? 1 : (int)(4 * s) + 1;
    public static int SearchHeaderHeightFor(float s) => RowHeightFor(s) + (int)(6 * s);

    private const TextFormatFlags TextFlags =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

    private readonly List<string> _items;
    private readonly List<int> _filteredIndices = [];
    private readonly TextBox _searchBox;
    private string? _value;
    private string _empty = "";
    private string _searchPlaceholder = "Search…";
    private int _hot = -1;
    private int _top;
    private int _contentTop;
    private bool _searchable;
    private bool _previewItemsInOwnFont;
    private bool _draggingScroll;
    private int _dragStartY;
    private int _dragStartTop;

    /// <summary>Raised with the index of the chosen item in the original item list.</summary>
    public event Action<int>? Chosen;
    public event KeyEventHandler? SearchKeyDown;
    public event EventHandler? PopupSizeChanged;

    public DropdownList(List<string> items)
    {
        _items = items;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;

        _searchBox = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            Visible = false,
            TabStop = true,
        };
        _searchBox.TextChanged += (_, _) => FilterItems();
        _searchBox.KeyDown += (_, e) => SearchKeyDown?.Invoke(this, e);
        _searchBox.MouseWheel += (_, e) => ScrollRows(-Math.Sign(e.Delta) * 2);
        Controls.Add(_searchBox);
        RebuildFilter();
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Searchable
    {
        get => _searchable;
        set
        {
            _searchable = value;
            _searchBox.Visible = value;
            PerformLayout();
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool PreviewItemsInOwnFont
    {
        get => _previewItemsInOwnFont;
        set { _previewItemsInOwnFont = value; Invalidate(); }
    }

    /// <summary>Called right before the popup is shown.</summary>
    public void Prepare(string? value, string emptyText, string searchPlaceholder, Size size)
    {
        _value = value;
        _empty = emptyText;
        _searchPlaceholder = searchPlaceholder;
        _searchBox.PlaceholderText = searchPlaceholder;
        _searchBox.Text = "";
        ApplySearchTheme();
        RebuildFilter();
        _hot = _filteredIndices.Contains(_items.IndexOf(value ?? "")) ? _items.IndexOf(value ?? "") : -1;
        int hotPosition = _filteredIndices.IndexOf(_hot);
        int maxTop = Math.Max(0, _filteredIndices.Count - VisibleRows);
        _top = hotPosition < 0 ? 0 : Math.Clamp(hotPosition - MaxVisible / 2, 0, maxTop);
        Size = size;
        LayoutSearchBox();
        Invalidate();
    }

    public bool ContainsPopupFocus => _searchBox.Focused || ContainsFocus;

    public void RefreshItems()
    {
        RebuildFilter();
        _top = 0;
        _hot = -1;
        PopupSizeChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public int GetPopupHeight(float scale) =>
        Math.Clamp(_filteredIndices.Count, 1, MaxVisible) * RowHeightFor(scale) +
        2 * PadFor(scale) + (_searchable ? SearchHeaderHeightFor(scale) : 0);

    public void MoveHot(int delta)
    {
        if (_filteredIndices.Count == 0) return;
        int position = _filteredIndices.IndexOf(_hot);
        position = position < 0
            ? (delta > 0 ? 0 : _filteredIndices.Count - 1)
            : Math.Clamp(position + delta, 0, _filteredIndices.Count - 1);
        _hot = _filteredIndices[position];
        if (position < _top) _top = position;
        else if (position >= _top + VisibleRows) _top = position - VisibleRows + 1;
        Invalidate();
    }

    public void ScrollRows(int rows)
    {
        _top = Math.Clamp(_top + rows, 0, MaxTop);
        Invalidate();
    }

    public void ChooseHot()
    {
        if (_filteredIndices.Contains(_hot)) Chosen?.Invoke(_hot);
    }

    private int Scale(int value) => (int)(value * (DeviceDpi / 96f));
    private int Pad => PadFor(DeviceDpi / 96f);
    private int RowHeight => RowHeightFor(DeviceDpi / 96f);
    private int VisibleRows => Math.Clamp(_filteredIndices.Count, 1, MaxVisible);
    private int MaxTop => Math.Max(0, _filteredIndices.Count - VisibleRows);

    private void LayoutSearchBox()
    {
        int pad = Pad;
        int inset = Scale(4);
        int rowHeight = RowHeight;
        _contentTop = pad + (_searchable ? SearchHeaderHeightFor(DeviceDpi / 96f) : 0);
        if (_searchable)
            _searchBox.Bounds = new Rectangle(pad + inset, pad + inset / 2,
                Math.Max(1, Width - 2 * pad - 2 * inset), Math.Max(1, rowHeight));
    }

    private void ApplySearchTheme()
    {
        var p = Theme.Current;
        _searchBox.BackColor = p.Field;
        _searchBox.ForeColor = p.Text;
    }

    private void RebuildFilter()
    {
        _filteredIndices.Clear();
        string query = _searchable ? _searchBox.Text.Trim() : "";
        for (int i = 0; i < _items.Count; i++)
            if (query.Length == 0 || _items[i].Contains(query, StringComparison.CurrentCultureIgnoreCase))
                _filteredIndices.Add(i);
    }

    private void FilterItems()
    {
        RebuildFilter();
        _top = 0;
        _hot = _filteredIndices.Contains(_items.IndexOf(_value ?? ""))
            ? _items.IndexOf(_value ?? "")
            : (_filteredIndices.Count > 0 ? _filteredIndices[0] : -1);
        PopupSizeChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private int IndexAt(int y)
    {
        int position = (y - _contentTop) / RowHeight + _top;
        return y >= _contentTop && position >= 0 && position < _filteredIndices.Count
            ? _filteredIndices[position]
            : -1;
    }

    private Rectangle ScrollHitBounds
    {
        get
        {
            float s = DeviceDpi / 96f;
            int width = Math.Max(14, (int)(16 * s));
            return new Rectangle(Width - Pad - width, _contentTop, width, VisibleRows * RowHeight);
        }
    }

    private Rectangle ScrollThumbBounds
    {
        get
        {
            if (_filteredIndices.Count <= VisibleRows) return Rectangle.Empty;
            float s = DeviceDpi / 96f;
            int trackHeight = VisibleRows * RowHeight;
            int thumbHeight = Math.Max((int)(18 * s), trackHeight * VisibleRows / _filteredIndices.Count);
            thumbHeight = Math.Min(trackHeight, thumbHeight);
            int travel = trackHeight - thumbHeight;
            int thumbY = _contentTop + (MaxTop == 0 ? 0 : (int)Math.Round(travel * _top / (double)MaxTop));
            Rectangle hit = ScrollHitBounds;
            int thumbWidth = Math.Max(6, (int)(7 * s));
            return new Rectangle(hit.Left + (hit.Width - thumbWidth) / 2, thumbY, thumbWidth, thumbHeight);
        }
    }

    private void SetScrollFromPointer(int y)
    {
        if (_filteredIndices.Count <= VisibleRows) return;
        Rectangle thumb = ScrollThumbBounds;
        int travel = VisibleRows * RowHeight - thumb.Height;
        if (travel <= 0) return;
        float offset = Math.Clamp(y - _contentTop - thumb.Height / 2f, 0, travel);
        _top = Math.Clamp((int)Math.Round(offset / travel * MaxTop), 0, MaxTop);
        Invalidate();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        LayoutSearchBox();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        LayoutSearchBox();
        Invalidate();
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

        if (_searchable)
        {
            using var divider = new Pen(p.FieldBorder);
            g.DrawLine(divider, pad, _contentTop - Scale(2), Width - pad - 1, _contentTop - Scale(2));
        }

        if (_filteredIndices.Count == 0)
        {
            var empty = new Rectangle(pad + Scale(8), _contentTop, Width - 2 * pad - Scale(16), rowH);
            TextRenderer.DrawText(g, _empty, Font, empty, p.TextDim, TextFlags);
            return;
        }

        int visible = Math.Min(VisibleRows, _filteredIndices.Count - _top);
        int scrollbarWidth = _filteredIndices.Count > VisibleRows ? Math.Max(14, Scale(16)) : 0;
        for (int i = 0; i < visible; i++)
        {
            int position = _top + i;
            int index = _filteredIndices[position];
            bool chosen = _items[index] == _value;
            var row = new Rectangle(pad, _contentTop + i * rowH, Width - 2 * pad - scrollbarWidth, rowH);

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
            using var previewFont = _previewItemsInOwnFont ? CreatePreviewFont(_items[index]) : null;
            TextRenderer.DrawText(g, _items[index], previewFont ?? Font, text,
                Theme.Flat && chosen ? p.OnAccent : p.Text, TextFlags);
        }

        if (_filteredIndices.Count > VisibleRows)
        {
            Rectangle hit = ScrollHitBounds;
            Rectangle thumb = ScrollThumbBounds;
            int railWidth = Math.Max(5, (int)(6 * s));
            var railBounds = new Rectangle(hit.Left + (hit.Width - railWidth) / 2, hit.Top, railWidth, hit.Height);
            using var rail = new SolidBrush(Color.FromArgb(64, p.TextDim));
            g.FillRectangle(rail, railBounds);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Gfx.RoundedRect(thumb, thumb.Width / 2f);
            using var brush = new SolidBrush(Gfx.Lerp(p.Card, p.TextDim, 0.72f));
            g.FillPath(brush, path);
            g.SmoothingMode = SmoothingMode.Default;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _filteredIndices.Count <= VisibleRows || !ScrollHitBounds.Contains(e.Location))
            return;

        Rectangle thumb = ScrollThumbBounds;
        _draggingScroll = true;
        Capture = true;
        if (!thumb.Contains(e.Location)) SetScrollFromPointer(e.Y);
        _dragStartY = e.Y;
        _dragStartTop = _top;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_draggingScroll)
        {
            int travel = VisibleRows * RowHeight - ScrollThumbBounds.Height;
            int trackTravel = Math.Max(1, travel);
            _top = Math.Clamp(_dragStartTop + (int)Math.Round((e.Y - _dragStartY) / (double)trackTravel * MaxTop), 0, MaxTop);
            Invalidate();
            return;
        }

        Cursor = ScrollHitBounds.Contains(e.Location) && _filteredIndices.Count > VisibleRows
            ? Cursors.Hand
            : Cursors.Default;
        int i = IndexAt(e.Y);
        if (i < 0 || i == _hot) return;
        _hot = i;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        if (_draggingScroll)
        {
            _draggingScroll = false;
            Capture = false;
            return;
        }
        if (_filteredIndices.Count > VisibleRows && ScrollHitBounds.Contains(e.Location)) return;
        int i = IndexAt(e.Y);
        if (i >= 0) Chosen?.Invoke(i);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ScrollRows(-Math.Sign(e.Delta) * 2);
    }

    private Font? CreatePreviewFont(string familyName)
    {
        try { return new Font(familyName, Font.SizeInPoints, GraphicsUnit.Point); }
        catch { return null; }
    }
}
