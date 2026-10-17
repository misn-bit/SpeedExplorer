using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SpeedExplorer.Design;

/// <summary>
/// Integer slider with a value readout ("14 pt"). Mouse drag, arrow keys, Home/End, PageUp/PageDown.
/// ValueChanged fires continuously; Committed fires once when the drag or key press ends.
/// Always keeps its rounded track and disc thumb, even in the flat style.
/// </summary>
internal sealed class SliderField : Control, IRowControl
{
    private const TextFormatFlags LabelFlags =
        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

    private const float ThumbRadius = 10f;   // at 100% scale
    private const float MinLabelWidth = 60f;

    /// <summary>Width of the value readout; grows with the widest value so long suffixes never clip.</summary>
    private float LabelW()
        => Math.Max(MinLabelWidth * (DeviceDpi / 96f),
            TextRenderer.MeasureText($"{_max}{_suffix}", Font, new Size(int.MaxValue / 2, 0),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 12f);

    private int _min, _max = 100, _step = 1, _value;
    private string _suffix = "";
    private bool _drag, _hover, _keyChanged;

    public event EventHandler? ValueChanged;
    public event EventHandler? Committed;

    public SliderField()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        TabStop = true;
    }

    public void Configure(int min, int max, int step, int value, string suffix)
    {
        _min = min;
        _max = Math.Max(min, max);
        _step = Math.Max(1, step);
        _suffix = suffix;
        _value = Snap(value);
        Invalidate();
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set => SetValue(value);
    }

    public Size GetRowSize(float scale) => new((int)(340 * scale), (int)(Metrics.ButtonHeight * scale));

    private int Snap(int v)
    {
        v = Math.Clamp(v, _min, _max);
        v = _min + (int)Math.Round((v - _min) / (double)_step) * _step;
        return Math.Min(v, _max);
    }

    private void SetValue(int v)
    {
        v = Snap(v);
        if (v == _value) return;
        _value = v;
        Invalidate();
        AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private RectangleF Track(float s)
    {
        float r = ThumbRadius * s;
        float h = 4f * s;
        float w = Math.Max(1f, Width - LabelW() - 2f * r);
        return new RectangleF(r, Height / 2f - h / 2f, w, h);
    }

    private float ThumbX(RectangleF track)
    {
        float t = _max == _min ? 0f : (_value - _min) / (float)(_max - _min);
        return track.Left + t * track.Width;
    }

    private void SetFromX(int x)
    {
        var track = Track(DeviceDpi / 96f);
        float t = Math.Clamp((x - track.Left) / track.Width, 0f, 1f);
        SetValue(_min + (int)Math.Round(t * (_max - _min)));
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Current.Card);

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        float s = DeviceDpi / 96f;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var track = Track(s);
        float x = ThumbX(track);
        float cy = Height / 2f;
        float r = ThumbRadius * s;

        using (var path = Gfx.RoundedRect(track, track.Height / 2f, forceRounded: true))
        using (var brush = new SolidBrush(Gfx.Lerp(p.Card, p.TextDim, 0.5f)))
            g.FillPath(brush, path);

        if (x > track.Left)
        {
            var filled = new RectangleF(track.Left, track.Top, Math.Max(x - track.Left, track.Height), track.Height);
            using var path = Gfx.RoundedRect(filled, track.Height / 2f, forceRounded: true);
            using var brush = new SolidBrush(p.Accent);
            g.FillPath(brush, path);
        }

        // thumb: card-colored disc with a hairline, accent dot in the middle
        var thumb = new RectangleF(x - r, cy - r, 2f * r, 2f * r);
        using (var brush = new SolidBrush(p.Card))
        using (var pen = new Pen(Gfx.Lerp(p.Card, p.TextDim, 0.35f)))
        {
            g.FillEllipse(brush, thumb);
            g.DrawEllipse(pen, thumb);
        }
        float dot = (_drag ? 5f : _hover ? 6.5f : 5.5f) * s;
        using (var brush = new SolidBrush(p.Accent))
            g.FillEllipse(brush, x - dot, cy - dot, 2 * dot, 2 * dot);

        if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(p.Text, 1.5f * s);
            g.DrawEllipse(pen, RectangleF.Inflate(thumb, 2.5f * s, 2.5f * s));
        }
        g.SmoothingMode = SmoothingMode.Default;

        int lw = (int)LabelW();
        var label = new Rectangle(Width - lw, 0, lw, Height);
        TextRenderer.DrawText(g, $"{_value}{_suffix}", Font, label, p.Text, LabelFlags);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _drag = true;
        Focus();
        SetFromX(e.X);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag) SetFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_drag) return;
        _drag = false;
        Invalidate();
        Committed?.Invoke(this, EventArgs.Empty);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End
            or Keys.PageUp or Keys.PageDown || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int big = Math.Max(_step, (_max - _min) / 10 / _step * _step);
        int before = _value;
        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Down: SetValue(_value - _step); break;
            case Keys.Right or Keys.Up: SetValue(_value + _step); break;
            case Keys.PageDown: SetValue(_value - big); break;
            case Keys.PageUp: SetValue(_value + big); break;
            case Keys.Home: SetValue(_min); break;
            case Keys.End: SetValue(_max); break;
            default: return;
        }
        e.Handled = true;
        if (_value != before) _keyChanged = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (!_keyChanged) return;
        _keyChanged = false;
        Committed?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override AccessibleObject CreateAccessibilityInstance() => new SliderAccessible(this);

    private sealed class SliderAccessible(SliderField owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.Slider;
        public override string? Value => $"{owner._value}{owner._suffix}";
    }
}
