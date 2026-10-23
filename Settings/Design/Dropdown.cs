using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SpeedExplorer.Design;

/// <summary>
/// Combo-box replacement. The list is a suggestion list: Value can be any string, so a saved
/// model name still shows when the server is offline and the list is empty.
/// Keyboard: Up/Down/F4/Space/Enter open it, Up/Down move, Enter picks, Esc closes.
/// </summary>
internal sealed class Dropdown : Control, IRowControl
{
    private const TextFormatFlags TextFlags =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

    private readonly List<string> _items = [];
    private readonly ToolStripDropDown _drop;
    private readonly DropdownList _list;

    private string? _value;
    private string _placeholder = "Select…";
    private string _emptyText = "Nothing to choose from.";
    private readonly OutsideClickFilter _filter;
    private bool _hover, _open;
    private Form? _form;

    public event EventHandler? ValueChanged;

    public Dropdown()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable | ControlStyles.StandardClick, true);
        SetStyle(ControlStyles.StandardDoubleClick, false);
        TabStop = true;
        AccessibleRole = AccessibleRole.ComboBox;

        _list = new DropdownList(_items);
        _list.Chosen += OnChosen;
        _filter = new OutsideClickFilter(c => c == this || c == _list || c == _drop, ClosePopup);

        // AutoClose stays off on purpose: with it on, WinForms' menu machinery grabs the arrow/Enter
        // keys for the popup. We close it ourselves (outside click, Esc, focus lost, window deactivated).
        _drop = new ToolStripDropDown
        {
            AutoClose = false,
            DropShadowEnabled = true,
            Padding = Padding.Empty,
            Renderer = new FlatRenderer(),
        };
        _drop.Items.Add(new ToolStripControlHost(_list)
        {
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            AutoSize = false,
        });
        _drop.HandleCreated += (_, _) => Native.RoundCorners(_drop.Handle);
        _drop.Closed += (_, _) => EndOpen();
    }

    private void ClosePopup()
    {
        if (_open) _drop.Close();
    }

    private void OnFormDeactivate(object? sender, EventArgs e) => ClosePopup();

    private void EndOpen()
    {
        _open = false;
        Application.RemoveMessageFilter(_filter);
        if (_form is not null)
        {
            _form.Deactivate -= OnFormDeactivate;
            _form = null;
        }
        Invalidate();
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Placeholder
    {
        get => _placeholder;
        set { _placeholder = value; Invalidate(); }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EmptyText
    {
        get => _emptyText;
        set => _emptyText = value;
    }

    public void SetItems(IEnumerable<string> items)
    {
        _items.Clear();
        _items.AddRange(items);
        Invalidate();
    }

    public Size GetRowSize(float scale, int availableWidth) => new((int)(280 * scale), (int)(Metrics.FieldHeight * scale));

    private void Open()
    {
        if (_open || !Enabled) return;
        float s = DeviceDpi / 96f;

        int rows = Math.Clamp(_items.Count, 1, DropdownList.MaxVisible);
        int h = rows * DropdownList.RowHeightFor(s) + 2 * DropdownList.PadFor(s);
        int w = Math.Max(Width, MeasureWidest(s));
        var size = new Size(w, h);

        _list.Prepare(_value, _emptyText, size);
        _drop.Items[0].Size = size;

        int gap = (int)(2 * s);
        var work = Screen.FromControl(this).WorkingArea;
        int screenY = PointToScreen(Point.Empty).Y;
        bool flip = screenY + Height + gap + h > work.Bottom && screenY - gap - h > work.Top;

        _open = true;
        Invalidate();
        Application.AddMessageFilter(_filter);
        _form = FindForm();
        if (_form is not null) _form.Deactivate += OnFormDeactivate;
        _drop.Show(this, new Point(0, flip ? -h - gap : Height + gap));
    }

    private int MeasureWidest(float s)
    {
        int widest = 0;
        foreach (var item in _items)
            widest = Math.Max(widest, TextRenderer.MeasureText(item, Font, new Size(int.MaxValue, int.MaxValue), TextFlags).Width);
        return Math.Min(widest + (int)(48 * s), (int)(560 * s));
    }

    private void OnChosen(int index)
    {
        _drop.Close();
        if (index >= 0 && index < _items.Count) Value = _items[index];
        Focus();
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Current.Card);

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        float s = DeviceDpi / 96f;

        Color? accent = null;
        if (_open || (Focused && ShowFocusCues)) accent = p.Accent;
        Gfx.FieldSurface(g, ClientRectangle, s, _hover ? p.FieldHover : p.Field, p.FieldBorder, accent);

        bool empty = string.IsNullOrEmpty(_value);
        int padX = (int)(Metrics.FieldPadX * s);
        var text = new Rectangle(padX, 0, Width - padX - (int)(36 * s), Height);
        TextRenderer.DrawText(g, empty ? _placeholder : _value!, Font, text,
            empty || !Enabled ? p.TextDim : p.Text, TextFlags);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = Width - 18f * s, cy = Height / 2f;
        using var pen = new Pen(p.TextDim, 1.5f * s)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        g.DrawLines(pen, new PointF[]
        {
            new(cx - 4f * s, cy - 2f * s),
            new(cx, cy + 2f * s),
            new(cx + 4f * s, cy - 2f * s),
        });
        g.SmoothingMode = SmoothingMode.Default;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (_open) ClosePopup(); else Open();
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down or Keys.F4 ||
        (_open && keyData is Keys.Enter or Keys.Escape) ||
        base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.F4:
            case Keys.Down when e.Alt:
                if (_open) ClosePopup(); else Open();
                break;
            case Keys.Down:
                if (_open) _list.MoveHot(1); else Open();
                break;
            case Keys.Up:
                if (_open) _list.MoveHot(-1); else Open();
                break;
            case Keys.Enter or Keys.Space:
                if (_open) _list.ChooseHot(); else Open();
                break;
            case Keys.Escape when _open:
                ClosePopup();
                break;
            default:
                return;
        }
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_open) _list.ScrollRows(-Math.Sign(e.Delta) * 2);
        else base.OnMouseWheel(e);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        ClosePopup();   // Tab away, or another control grabbed focus
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Application.RemoveMessageFilter(_filter);
            _drop.Dispose();   // disposes the hosted list too
        }
        base.Dispose(disposing);
    }

    /// <summary>Closes the popup when the mouse goes down anywhere outside the dropdown and its list.</summary>
    private sealed class OutsideClickFilter(Func<Control, bool> isInside, Action close) : IMessageFilter
    {
        public bool PreFilterMessage(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x0201: // WM_LBUTTONDOWN
                case 0x0204: // WM_RBUTTONDOWN
                case 0x0207: // WM_MBUTTONDOWN
                case 0x00A1: // WM_NCLBUTTONDOWN (title bar, borders)
                case 0x00A4: // WM_NCRBUTTONDOWN
                    var target = Control.FromHandle(m.HWnd);
                    if (target is null || !isInside(target)) close();
                    break;
            }
            return false;   // never swallow the click; it still reaches whatever was clicked
        }
    }

    /// <summary>Strips the stock border and background so only DropdownList's own painting shows.</summary>
    private sealed class FlatRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) =>
            e.Graphics.Clear(Theme.Current.Card);
    }
}
