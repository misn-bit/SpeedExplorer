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
        private const int ApplyDebounceMs = 500;

        private readonly MainForm _owner;
        private readonly System.Windows.Forms.Timer _applyTimer;
        private int _baseSize;
        private string? _zoomPath;
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
            try { CommitPendingZoom(applyUi: false); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
            try { _applyTimer.Stop(); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
            try { _applyTimer.Dispose(); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
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
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
        }

        public void HandleMouseWheel(object? sender, MouseEventArgs e)
        {
            if ((Control.ModifierKeys & Keys.Control) == 0) return;

            NavigationDebugLogger.Log($"ICON_SIZE_WHEEL path=\"{_owner.State.CurrentPath}\" delta={e.Delta} current={_owner.GetLogicalIconSize()}");

            int notches = e.Delta / SystemInformation.MouseWheelScrollDelta;
            if (notches == 0)
                notches = e.Delta > 0 ? 1 : -1;

            if (_baseSize == 0)
            {
                _baseSize = _owner.GetLogicalIconSize();
                _zoomPath = _owner.State.CurrentPath;
            }

            QueueZoomDelta(Math.Sign(notches), Math.Abs(notches));
        }

        public void HandleZoomHotkey(int direction)
        {
            if (direction == 0)
                return;

            if (_baseSize == 0)
            {
                _baseSize = _owner.GetLogicalIconSize();
                _zoomPath = _owner.State.CurrentPath;
            }

            QueueZoomDelta(direction, 1);
        }

        private void QueueZoomDelta(int direction, int count)
        {
            int min = LogicalMinIconSize;
            int max = LogicalMaxIconSize;
            int nextSteps = _steps;

            for (int i = 0; i < count; i++)
            {
                int proposed = _baseSize + ((nextSteps + direction) * LogicalIconSizeStep);
                int current = Math.Clamp(_baseSize + (nextSteps * LogicalIconSizeStep), min, max);
                int bounded = Math.Clamp(proposed, min, max);
                if (bounded == current)
                {
                    // Ignore overscroll beyond bounds.
                    break;
                }
                nextSteps += direction;
            }

            // A rejected step at a limit must not create a delayed commit.
            if (nextSteps == _steps)
            {
                if (_steps == 0)
                    _baseSize = 0;
                return;
            }

            _steps = nextSteps;

            int target = _baseSize + (_steps * LogicalIconSizeStep);
            _pendingSize = Math.Clamp(target, min, max);

            ShowZoomPreview(_owner.GetEffectiveIconSizeForLogicalSize(_pendingSize));

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

            _previewOverlay = new ZoomSizePreviewOverlay(_owner) { Visible = false };
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

            // size is already in physical pixels; only the fixed padding needs scaling.
            int overlayWidth = Math.Max(_owner.Scale(150), size + _owner.Scale(70));
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
            try { HideZoomPreview(); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }

            bool hasZoomChange = _steps != 0;
            int target = _pendingSize > 0 ? _pendingSize : _baseSize;
            string? zoomPath = _zoomPath;
            NavigationDebugLogger.Log($"ICON_SIZE_COMMIT path=\"{zoomPath}\" changed={hasZoomChange} target={target} steps={_steps} applyUi={applyUi} currentPath=\"{_owner.State.CurrentPath}\"");

            if (hasZoomChange)
                _owner.SaveIconSizeForPath(zoomPath, target);

            if (hasZoomChange && applyUi && !_owner.IsDisposed && !_owner.Disposing &&
                string.Equals(_owner.State.CurrentPath, zoomPath, StringComparison.OrdinalIgnoreCase))
            {
                _owner.ApplySettings();
                if (_owner.IsTileView)
                    _owner.UpdateTileViewMetrics();
            }

            _baseSize = 0;
            _zoomPath = null;
            _steps = 0;
            _pendingSize = 0;
        }

        public void CommitPendingZoomForLocationChange()
        {
            _applyTimer.Stop();
            CommitPendingZoom(applyUi: false);
        }

        public void CommitPendingZoomForDpiChange()
        {
            // The pending value is logical, so committing is independent of
            // whether WinForms has already updated DeviceDpi for this event.
            _applyTimer.Stop();
            CommitPendingZoom(applyUi: false);
        }

        private sealed class ZoomSizePreviewOverlay : Control
        {
            private const int WM_NCHITTEST = 0x0084;
            private static readonly IntPtr HTTRANSPARENT = new IntPtr(-1);
            private readonly MainForm _owner;
            private int _previewSize = LogicalMinIconSize;

            // Preview sizes are physical pixels; clamp against DPI-scaled bounds.
            private int ScaledMinIconSize => _owner.GetEffectiveIconSizeForLogicalSize(LogicalMinIconSize);
            private int ScaledMaxIconSize => _owner.GetEffectiveIconSizeForLogicalSize(LogicalMaxIconSize);

            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public int PreviewSize
            {
                get => _previewSize;
                set
                {
                    int clamped = Math.Clamp(value, ScaledMinIconSize, ScaledMaxIconSize);
                    if (_previewSize == clamped) return;
                    _previewSize = clamped;
                    Invalidate();
                }
            }

            public ZoomSizePreviewOverlay(MainForm owner)
            {
                _owner = owner;
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

                int range = Math.Max(1, ScaledMaxIconSize - ScaledMinIconSize);
                int mapped = 28 + (int)Math.Round((PreviewSize - ScaledMinIconSize) * (112.0 / range));
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
