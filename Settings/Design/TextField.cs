using System;
using System.ComponentModel;
using System.Drawing;

namespace SpeedExplorer.Design;

/// <summary>
/// Text input: a real TextBox (so IME, selection, undo and copy/paste all behave) inside a
/// custom-painted frame (rounded in the rounded style, square in the flat style).
/// Focus shows an accent line; IsInvalid turns the frame red.
/// ValueChanged fires on every edit, Committed on Enter or focus loss.
/// </summary>
internal sealed class TextField : Control, IRowControl, IThemed
{
    private readonly TextBox _box = new() { BorderStyle = BorderStyle.None };
    private bool _invalid;

    public event EventHandler? ValueChanged;
    public event EventHandler? Committed;

    public TextField()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;   // tab goes straight to the inner box

        Controls.Add(_box);
        _box.TextChanged += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);
        _box.GotFocus += (_, _) => Invalidate();
        _box.LostFocus += (_, _) => Invalidate();
        _box.Leave += (_, _) => Committed?.Invoke(this, EventArgs.Empty);
        _box.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            Committed?.Invoke(this, EventArgs.Empty);
        };
        ApplyTheme(Theme.Current);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Value
    {
        get => _box.Text;
        set => _box.Text = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string PlaceholderText
    {
        get => _box.PlaceholderText;
        set => _box.PlaceholderText = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsInvalid
    {
        get => _invalid;
        set { if (_invalid == value) return; _invalid = value; Invalidate(); }
    }

    public Size GetRowSize(float scale, int availableWidth) => new((int)(320 * scale), (int)(Metrics.FieldHeight * scale));

    public void ApplyTheme(Palette p)
    {
        _box.BackColor = p.Field;
        _box.ForeColor = p.Text;
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int padX = (int)(Metrics.FieldPadX * DeviceDpi / 96f);
        _box.Width = Math.Max(10, Width - 2 * padX);
        _box.Location = new Point(padX, (Height - _box.Height) / 2);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        PerformLayout();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _box.AccessibleName = AccessibleName;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _box.Focus();   // clicks on the frame's padding still land in the box
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Current.Card);

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        Color? accent = null;
        if (_invalid) accent = p.Danger;
        else if (_box.Focused) accent = p.Accent;

        Gfx.FieldSurface(e.Graphics, ClientRectangle, DeviceDpi / 96f,
            p.Field, _invalid ? p.Danger : p.FieldBorder, accent);
    }
}
