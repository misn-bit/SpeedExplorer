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
        private const int WM_VSCROLL = 0x0115;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int LVM_GETHEADER = 0x101F;

        public event EventHandler? ScrollActivity;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SCROLLINFO
        {
            public int cbSize;
            public uint fMask;
            public int nMin;
            public int nMax;
            public int nPage;
            public int nPos;
            public int nTrackPos;
        }

        private const int SIF_RANGE = 0x001;
        private const int SIF_PAGE = 0x002;
        private const int SIF_POS = 0x004;
        private const int SB_VERT = 1;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetScrollInfo(IntPtr hWnd, int nBar, ref SCROLLINFO lpScrollInfo);

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
            if (m.Msg == WM_VSCROLL || m.Msg == WM_MOUSEWHEEL)
                ScrollActivity?.Invoke(this, EventArgs.Empty);
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
                // The empty strip below the last row can only be visible when the
                // list is scrolled to its bottom. Measuring the last item's rect
                // (GetItemRect) is expensive for huge virtual lists — it forces
                // layout of every row in between — and it used to run on every
                // WM_PAINT. Skip the whole tail paint unless the bottom is on screen.
                if (!BottomOfListIsOnScreen())
                    return;

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

        private bool BottomOfListIsOnScreen()
        {
            var si = new SCROLLINFO
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<SCROLLINFO>(),
                fMask = SIF_RANGE | SIF_PAGE | SIF_POS
            };
            if (!GetScrollInfo(Handle, SB_VERT, ref si) || si.nMax <= 0)
                return true; // Can't tell — keep the previous (safe) behavior.

            // Standard Win32 scrollbar math: the end of the content is on screen
            // only when the page covers the end of the range.
            return si.nPos + si.nPage > si.nMax;
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
