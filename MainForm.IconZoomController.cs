using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class IconZoomController : IDisposable
    {
        private const int MinIconSize = 16;
        private const int MaxIconSize = 192;
        private const int IconStep = 8;
        private const int ApplyDebounceMs = 500;

        private readonly MainForm _owner;
        private readonly System.Windows.Forms.Timer _applyTimer;
        private int _baseSize;
        private int _steps;
        private int _pendingSize;
        private ZoomSizePreviewOverlay? _previewOverlay;

        public IconZoomController(MainForm owner)
        {
            _owner = owner;
            _applyTimer = new System.Windows.Forms.Timer { Interval = ApplyDebounceMs };
            _applyTimer.Tick += (s, e) => ApplyPendingZoom();
        }

        public void Dispose()
        {
            try { CommitPendingZoom(applyUi: false); } catch { }
            try { _applyTimer.Stop(); } catch { }
            try { _applyTimer.Dispose(); } catch { }
            try
            {
                if (_previewOverlay != null)
                {
                    if (_previewOverlay.Parent != null)
                        _previewOverlay.Parent.Controls.Remove(_previewOverlay);
                    _previewOverlay.Dispose();
                    _previewOverlay = null;
                }
            }
            catch { }
        }

        public void HandleMouseWheel(object? sender, MouseEventArgs e)
        {
            if ((Control.ModifierKeys & Keys.Control) == 0) return;

            int notches = e.Delta / SystemInformation.MouseWheelScrollDelta;
            if (notches == 0)
                notches = e.Delta > 0 ? 1 : -1;

            var s = AppSettings.Current;

            if (_baseSize == 0)
                _baseSize = Math.Clamp(s.IconSize, MinIconSize, MaxIconSize);

            int direction = Math.Sign(notches);
            int count = Math.Abs(notches);
            int nextSteps = _steps;

            for (int i = 0; i < count; i++)
            {
                int proposed = _baseSize + ((nextSteps + direction) * IconStep);
                if (proposed < MinIconSize || proposed > MaxIconSize)
                {
                    // Ignore overscroll beyond bounds.
                    break;
                }
                nextSteps += direction;
            }

            _steps = nextSteps;

            int target = _baseSize + (_steps * IconStep);
            _pendingSize = Math.Clamp(target, MinIconSize, MaxIconSize);

            ShowZoomPreview(_pendingSize);

            _applyTimer.Stop();
            _applyTimer.Start();
        }

        private void ApplyPendingZoom()
        {
            _applyTimer.Stop();
            CommitPendingZoom(applyUi: true);
        }

        private void EnsurePreviewOverlay()
        {
            if (_previewOverlay != null && !_previewOverlay.IsDisposed)
                return;

            _previewOverlay = new ZoomSizePreviewOverlay { Visible = false };
        }

        private void ShowZoomPreview(int size)
        {
            EnsurePreviewOverlay();
            if (_previewOverlay == null || _previewOverlay.IsDisposed)
                return;

            var host = _owner._splitContainer?.Panel2;
            if (host == null || host.IsDisposed || _owner._listView == null || _owner._listView.IsDisposed)
                return;

            if (_previewOverlay.Parent != host)
                host.Controls.Add(_previewOverlay);

            _previewOverlay.PreviewSize = size;

            int overlayWidth = Math.Max(_owner.Scale(150), _owner.Scale(size) + _owner.Scale(70));
            int overlayHeight = overlayWidth;
            int x = _owner._listView.Left + ((_owner._listView.Width - overlayWidth) / 2);
            int y = _owner._listView.Top + ((_owner._listView.Height - overlayHeight) / 2);

            _previewOverlay.Bounds = new Rectangle(x, y, overlayWidth, overlayHeight);
            _previewOverlay.BringToFront();
            _previewOverlay.Visible = true;
            _previewOverlay.Invalidate();
        }

        private void HideZoomPreview()
        {
            if (_previewOverlay == null || _previewOverlay.IsDisposed)
                return;
            _previewOverlay.Visible = false;
        }

        private void CommitPendingZoom(bool applyUi)
        {
            try { HideZoomPreview(); } catch { }

            var s = AppSettings.Current;
            int target = _pendingSize > 0 ? _pendingSize : Math.Clamp(s.IconSize, MinIconSize, MaxIconSize);

            if (target != s.IconSize)
            {
                s.IconSize = target;
                s.Save();

                if (applyUi && _owner != null && !_owner.IsDisposed && !_owner.Disposing)
                {
                    _owner.ApplySettings();
                    if (_owner.IsTileView)
                        _owner.UpdateTileViewMetrics();
                }
            }

            _baseSize = 0;
            _steps = 0;
            _pendingSize = 0;
        }

        private sealed class ZoomSizePreviewOverlay : Control
        {
            private const int WM_NCHITTEST = 0x0084;
            private static readonly IntPtr HTTRANSPARENT = new IntPtr(-1);
            private int _previewSize = MinIconSize;

            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public int PreviewSize
            {
                get => _previewSize;
                set
                {
                    int clamped = Math.Clamp(value, MinIconSize, MaxIconSize);
                    if (_previewSize == clamped) return;
                    _previewSize = clamped;
                    Invalidate();
                }
            }

            public ZoomSizePreviewOverlay()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw |
                         ControlStyles.UserPaint |
                         ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
                TabStop = false;
            }

            protected override void WndProc(ref Message m)
            {
                // Make overlay click-through / wheel-through so Ctrl+Wheel keeps reaching ListView.
                if (m.Msg == WM_NCHITTEST)
                {
                    m.Result = HTTRANSPARENT;
                    return;
                }
                base.WndProc(ref m);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var full = ClientRectangle;
                if (full.Width <= 2 || full.Height <= 2) return;

                using (var panelPath = CreateRoundedRect(new Rectangle(0, 0, full.Width - 1, full.Height - 1), 12))
                using (var panelBack = new SolidBrush(Color.FromArgb(190, 20, 20, 20)))
                using (var panelBorder = new Pen(Color.FromArgb(140, 190, 190, 190), 1f))
                {
                    e.Graphics.FillPath(panelBack, panelPath);
                    e.Graphics.DrawPath(panelBorder, panelPath);
                }

                int mapped = 28 + (int)Math.Round((PreviewSize - MinIconSize) * (112.0 / (MaxIconSize - MinIconSize)));
                mapped = Math.Clamp(mapped, 28, Math.Min(full.Width - 40, full.Height - 70));

                int docX = (full.Width - mapped) / 2;
                int docY = Math.Max(16, (full.Height - mapped - 36) / 2);
                var docRect = new Rectangle(docX, docY, mapped, mapped);

                int fold = Math.Max(8, mapped / 6);
                using (var docPen = new Pen(Color.FromArgb(220, 220, 220), 2f))
                {
                    // Document outline with folded corner.
                    e.Graphics.DrawLine(docPen, docRect.Left, docRect.Top, docRect.Right - fold, docRect.Top);
                    e.Graphics.DrawLine(docPen, docRect.Right - fold, docRect.Top, docRect.Right, docRect.Top + fold);
                    e.Graphics.DrawLine(docPen, docRect.Right, docRect.Top + fold, docRect.Right, docRect.Bottom);
                    e.Graphics.DrawLine(docPen, docRect.Right, docRect.Bottom, docRect.Left, docRect.Bottom);
                    e.Graphics.DrawLine(docPen, docRect.Left, docRect.Bottom, docRect.Left, docRect.Top);
                    e.Graphics.DrawLine(docPen, docRect.Right - fold, docRect.Top, docRect.Right - fold, docRect.Top + fold);
                    e.Graphics.DrawLine(docPen, docRect.Right - fold, docRect.Top + fold, docRect.Right, docRect.Top + fold);
                }

                string text = $"{PreviewSize}px";
                var textRect = new Rectangle(0, docRect.Bottom + 8, full.Width, full.Height - docRect.Bottom - 8);
                TextRenderer.DrawText(
                    e.Graphics,
                    text,
                    new Font("Segoe UI", 10, FontStyle.Bold),
                    textRect,
                    Color.FromArgb(240, 240, 240),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            }

            private static GraphicsPath CreateRoundedRect(Rectangle rect, int radius)
            {
                int d = radius * 2;
                var path = new GraphicsPath();
                path.StartFigure();
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }
        }
    }
}
