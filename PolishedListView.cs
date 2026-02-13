using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpeedExplorer
{
    // Thin wrapper around ListView used to keep the dark background stable.
    // We intentionally rely on native marquee selection (rubber-band box).
    public class PolishedListView : ListView
    {
        private const int WM_PAINT = 0x000F;
        private const int LVM_GETHEADER = 0x101F;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        public PolishedListView()
        {
            // Keep native paint pipeline intact for virtual owner-draw stability.
        }

        protected override void OnLostFocus(EventArgs e)
        {
            try
            {
                base.OnLostFocus(e);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Guard against transient focus index issues when switching view modes.
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            try
            {
                base.OnHandleDestroyed(e);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Guard against transient selection/index issues during view mode switches.
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT)
            {
                PaintTailBackgroundSafe();
            }
        }

        private void PaintTailBackgroundSafe()
        {
            if (IsDisposed || !IsHandleCreated || View != View.Details)
                return;
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return;
            if ((Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left && Capture)
                return;

            try
            {
                int headerHeight = GetHeaderHeight();
                int tailTop = headerHeight;

                int count = VirtualMode ? VirtualListSize : Items.Count;
                if (count > 0)
                {
                    try
                    {
                        var lastRect = GetItemRect(count - 1, ItemBoundsPortion.Entire);
                        tailTop = Math.Max(headerHeight, lastRect.Bottom);
                    }
                    catch
                    {
                        tailTop = headerHeight;
                    }
                }

                if (tailTop < 0 || tailTop >= ClientSize.Height)
                    return;

                using var g = Graphics.FromHwnd(Handle);
                using var b = new SolidBrush(BackColor);
                g.FillRectangle(b, 0, tailTop, ClientSize.Width, ClientSize.Height - tailTop);
            }
            catch
            {
                // Best-effort visual cleanup.
            }
        }

        private int GetHeaderHeight()
        {
            try
            {
                var header = SendMessage(Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                if (header == IntPtr.Zero)
                    return 0;
                if (!GetWindowRect(header, out var rc))
                    return 0;

                var topLeft = PointToClient(new Point(rc.Left, rc.Top));
                var bottomRight = PointToClient(new Point(rc.Right, rc.Bottom));
                return Math.Max(0, bottomRight.Y - topLeft.Y);
            }
            catch
            {
                return 0;
            }
        }

    }
}
