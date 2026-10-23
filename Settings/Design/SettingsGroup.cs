using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpeedExplorer.Design;

/// <summary>
/// A rounded card of rows: title + description on the left, a control on the right.
/// The card paints the rows' text itself and positions each row's control, so there is no
/// nested Label/Panel layout to fight. Height follows the content (descriptions wrap).
/// In the flat style it uses a square card with hairline row dividers.
/// </summary>
internal sealed class SettingsGroup : Control
{
    private const TextFormatFlags TextFlags =
        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak |
        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

    private readonly record struct Row(string Title, string Description, Control Host, bool Stacked, bool Error);
    private readonly record struct RowLayout(Rectangle Bounds, Rectangle Title, Rectangle Desc);

    private readonly List<Row> _rows = [];
    private readonly List<RowLayout> _layout = [];
    private Font? _titleFont;

    public SettingsGroup()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Margin = new Padding(0, 0, 0, 8);
    }

    private Font TitleFont => _titleFont ??= new Font("Segoe UI Semibold", Font.Size + 0.5f, GraphicsUnit.Point);

    /// <summary>stacked = true puts the control on its own line under the text, full width.</summary>
    public void AddRow(string title, string description, Control host, bool stacked = false)
    {
        if (string.IsNullOrEmpty(host.AccessibleName)) host.AccessibleName = title;
        _rows.Add(new Row(title, description, host, stacked, false));
        Controls.Add(host);
        PerformLayout();
        Invalidate();
    }

    /// <summary>Changes a row's description in place (status text, validation errors).</summary>
    public void SetDescription(Control host, string text, bool error = false)
    {
        int i = _rows.FindIndex(r => r.Host == host);
        if (i < 0) return;
        _rows[i] = _rows[i] with { Description = text, Error = error };
        host.AccessibleDescription = text;
        PerformLayout();
        Invalidate();
    }

    public TextField AddText(string title, string description, string value, string placeholder)
    {
        var field = new TextField { Value = value, PlaceholderText = placeholder, AccessibleDescription = description };
        AddRow(title, description, field, stacked: true);
        return field;
    }

    /// <summary>live fires while dragging; committed fires once on release (save there).</summary>
    public SliderField AddSlider(string title, string description, int min, int max, int step, int value,
        string suffix, Action<int> live, Action<int>? committed = null)
    {
        var slider = new SliderField { AccessibleDescription = description };
        slider.Configure(min, max, step, value, suffix);
        slider.ValueChanged += (_, _) => live(slider.Value);
        if (committed is not null) slider.Committed += (_, _) => committed(slider.Value);
        AddRow(title, description, slider);
        return slider;
    }

    public Dropdown AddDropdown(string title, string description, string? value, string placeholder,
        string emptyText, Action<string?> changed)
    {
        var drop = new Dropdown { Value = value, Placeholder = placeholder, EmptyText = emptyText, AccessibleDescription = description };
        drop.ValueChanged += (_, _) => changed(drop.Value);
        AddRow(title, description, drop);
        return drop;
    }

    public FlatButton AddButton(string title, string description, string buttonText, ButtonKind kind, Action click)
    {
        var button = new FlatButton { Text = buttonText, Kind = kind, AccessibleDescription = description };
        button.Click += (_, _) => click();
        AddRow(title, description, button);
        return button;
    }

    public PreviewText AddPreview(string title, string description)
    {
        var preview = new PreviewText();
        AddRow(title, description, preview, stacked: true);
        return preview;
    }

    public ToggleSwitch AddToggle(string title, string description, bool isOn, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { Checked = isOn, AccessibleDescription = description };
        toggle.CheckedChanged += (_, _) => changed(toggle.Checked);
        AddRow(title, description, toggle);
        return toggle;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        _layout.Clear();
        if (Width < 120) return;

        float s = DeviceDpi / 96f;
        int border = Math.Max(1, (int)Math.Round(s));
        int padX = (int)(Metrics.RowPadX * s), padY = (int)(Metrics.RowPadY * s);
        int gap = (int)(16 * s), lineGap = (int)(2 * s);
        int y = border;

        int contentW = Width - 2 * border - 2 * padX;
        int hostGap = (int)(10 * s);

        foreach (var row in _rows)
        {
            if (row.Host is IRowControl sized)
            {
                var size = sized.GetRowSize(s, contentW);
                row.Host.Size = row.Stacked ? new Size(contentW, size.Height) : size;
            }
            else if (row.Stacked)
            {
                // stacked plain-control hosts (list boxes, button rows…) span the card width
                row.Host.Width = contentW;
            }

            int textW = row.Stacked ? contentW : Math.Max(1, contentW - row.Host.Width - gap);
            var proposed = new Size(textW, int.MaxValue);
            Size t = TextRenderer.MeasureText(row.Title, TitleFont, proposed, TextFlags);
            Size d = row.Description.Length == 0
                ? Size.Empty
                : TextRenderer.MeasureText(row.Description, Font, proposed, TextFlags);

            int textH = t.Height + (d.Height > 0 ? lineGap + d.Height : 0);
            int x = border + padX;
            int h, top;

            if (row.Stacked)
            {
                h = padY + textH + hostGap + row.Host.Height + padY;
                top = y + padY;
                row.Host.Location = new Point(x, top + textH + hostGap);
            }
            else
            {
                h = Math.Max(textH, row.Host.Height) + 2 * padY;
                top = y + (h - textH) / 2;
                row.Host.Location = new Point(
                    Width - border - padX - row.Host.Width,
                    y + (h - row.Host.Height) / 2);
            }

            _layout.Add(new RowLayout(
                new Rectangle(border, y, Width - 2 * border, h),
                new Rectangle(x, top, textW, t.Height),
                new Rectangle(x, top + t.Height + lineGap, textW, d.Height)));
            y += h;
        }

        int total = y + border;
        if (Height != total) Height = total;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var p = Theme.Current;
        var g = e.Graphics;
        float s = DeviceDpi / 96f;

        if (Theme.Flat)
        {
            // Keep the square flat style, while giving each group its own surface.
            using (var fill = new SolidBrush(p.Card))
                g.FillRectangle(fill, ClientRectangle);
            using var edge = new Pen(p.Border);
            g.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);
        }
        else
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Gfx.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 8f * s))
            using (var fill = new SolidBrush(p.Card))
            using (var pen = new Pen(p.Border))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            g.SmoothingMode = SmoothingMode.Default;
        }

        using var divider = new Pen(p.Border);
        for (int i = 0; i < _layout.Count; i++)
        {
            var l = _layout[i];
            if (i > 0) g.DrawLine(divider, l.Bounds.Left, l.Bounds.Top, l.Bounds.Right - 1, l.Bounds.Top);
            TextRenderer.DrawText(g, _rows[i].Title, TitleFont, l.Title, p.Text, TextFlags);
            if (l.Desc.Height > 0)
                TextRenderer.DrawText(g, _rows[i].Description, Font, l.Desc, _rows[i].Error ? p.Danger : p.TextDim, TextFlags);
        }
    }

    protected override void OnFontChanged(EventArgs e)
    {
        _titleFont?.Dispose();
        _titleFont = null;
        base.OnFontChanged(e);
        PerformLayout();
    }

    // Moved to a monitor with a different scale: row metrics depend on DeviceDpi.
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        PerformLayout();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _titleFont?.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>One page of settings: a heading, then section labels and cards flowing top to bottom.</summary>
internal class SettingsPage : FlowLayoutPanel
{
    private const int WmThemeChanged = 0x031A;
    private readonly Font _headingFont = new("Segoe UI Semibold", Metrics.HeadingPoints, GraphicsUnit.Point);
    private readonly Panel _bottomSpacer = new()
    {
        Size = new Size(1, 20),
        Margin = Padding.Empty,
        BackColor = Color.Transparent,
        TabStop = false,
    };

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern IntPtr SendWindowMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title { get; }

    public SettingsPage(string title)
    {
        Title = title;
        Dock = DockStyle.Fill;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoScroll = true;
        Padding = new Padding(16, 12, 16, 0);
        Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Font = _headingFont,
            Margin = new Padding(0, 0, 0, 4),
        });
        Controls.Add(_bottomSpacer);
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (!ReferenceEquals(e.Control, _bottomSpacer) &&
            Controls.Contains(_bottomSpacer) &&
            Controls.GetChildIndex(_bottomSpacer) != Controls.Count - 1)
        {
            Controls.SetChildIndex(_bottomSpacer, Controls.Count - 1);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyScrollbarTheme();
    }

    public void ApplyScrollbarTheme()
    {
        if (!IsHandleCreated) return;
        SetWindowTheme(Handle, Theme.Current.IsDark ? "DarkMode_Explorer" : "Explorer", null);
        SendWindowMessage(Handle, WmThemeChanged, IntPtr.Zero, IntPtr.Zero);
        Invalidate();
    }

    public void EnsureBottomSpacer()
    {
        if (!Controls.Contains(_bottomSpacer))
            Controls.Add(_bottomSpacer);

        int lastIndex = Controls.Count - 1;
        if (Controls.GetChildIndex(_bottomSpacer) != lastIndex)
            Controls.SetChildIndex(_bottomSpacer, lastIndex);
        PerformLayout();
    }

    public void AddSection(string? label, SettingsGroup group)
    {
        if (label is not null)
            Controls.Add(new Label { Text = label, AutoSize = true, Tag = "section-title", Margin = new Padding(2, 16, 0, 6) });
        Controls.Add(group);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        // cards always span the page width
        int w = ClientSize.Width - Padding.Horizontal;
        foreach (var group in Controls.OfType<SettingsGroup>())
        {
            int width = Math.Max(120, w - group.Margin.Horizontal);
            if (group.Width != width) group.Width = width;
        }
        base.OnLayout(levent);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) PerformLayout();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _headingFont.Dispose();
        base.Dispose(disposing);
    }
}
