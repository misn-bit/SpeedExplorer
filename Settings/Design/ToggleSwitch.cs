using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SpeedExplorer.Design;

/// <summary>
/// Fluent-style on/off switch. Fully custom-painted, no dependencies.
/// Mouse, Space key, screen readers (reports as a checked/unchecked check button).
/// Meant to sit on a card, so it paints the card color behind itself.
/// Always keeps its rounded pill shape, even in the flat style.
/// </summary>
internal sealed class ToggleSwitch : Control
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private bool _checked;
    private float _pos;   // 0 = off, 1 = on; animated

    public event EventHandler? CheckedChanged;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable | ControlStyles.StandardClick, true);
        // Like a CheckBox: a fast second click must toggle again, not become a DoubleClick.
        SetStyle(ControlStyles.StandardDoubleClick, false);
        TabStop = true;
        _timer.Tick += (_, _) => Step();
    }

    protected override Size DefaultSize => new(44, 24);

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            if (IsHandleCreated) _timer.Start(); else _pos = value ? 1f : 0f;
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Step()
    {
        float target = _checked ? 1f : 0f;
        float delta = target - _pos;
        if (Math.Abs(delta) <= 0.15f)
        {
            _pos = target;
            _timer.Stop();
        }
        else
        {
            _pos += Math.Sign(delta) * 0.15f;
        }
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Current.Card);

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        float s = DeviceDpi / 96f;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color Dim(Color c) => Enabled ? c : Color.FromArgb(100, c);

        var track = RectangleF.Inflate(ClientRectangle, -3f * s, -3f * s);
        Color fill = Dim(Gfx.Lerp(p.Card, p.Accent, _pos));
        Color line = Dim(Gfx.Lerp(p.TextDim, p.Accent, _pos));
        Color thumb = Dim(Gfx.Lerp(p.TextDim, p.OnAccent, _pos));

        using (var path = Gfx.RoundedRect(track, track.Height / 2f, forceRounded: true))
        using (var brush = new SolidBrush(fill))
        using (var pen = new Pen(line, 1.5f * s))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        // thumb grows slightly when on
        float d = track.Height * (0.5f + 0.1f * _pos);
        float pad = (track.Height - d) / 2f;
        float x = track.Left + pad + _pos * (track.Width - d - 2f * pad);
        using (var brush = new SolidBrush(thumb))
            g.FillEllipse(brush, x, track.Top + pad, d, d);

        if (Focused && ShowFocusCues)
        {
            using var ring = Gfx.RoundedRect(RectangleF.Inflate(track, 1.5f * s, 1.5f * s), (track.Height + 3f * s) / 2f, forceRounded: true);
            using var pen = new Pen(p.Text, 1.5f * s);
            g.DrawPath(pen, ring);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Checked = !Checked;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode == Keys.Space) Checked = !Checked;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override AccessibleObject CreateAccessibilityInstance() => new ToggleAccessible(this);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    private sealed class ToggleAccessible(ToggleSwitch owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.CheckButton;
        public override AccessibleStates State =>
            base.State | (owner.Checked ? AccessibleStates.Checked : AccessibleStates.None);
        public override string DefaultAction => "Toggle";
        public override void DoDefaultAction() => owner.Checked = !owner.Checked;
    }
}
