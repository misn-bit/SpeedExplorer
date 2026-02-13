using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class HeaderTailController : NativeWindow, IDisposable
    {
        private readonly MainForm _owner;
        private IntPtr _hwnd;

        private const int WM_PAINT = 0x000F;
        private const int WM_THEMECHANGED = 0x031A;
        private const int WM_STYLECHANGED = 0x007D;
        private const int WM_SIZE = 0x0005;

        public HeaderTailController(MainForm owner)
        {
            _owner = owner;
        }

        public void Attach(IntPtr headerHwnd)
        {
            if (headerHwnd == IntPtr.Zero) return;
            if (_hwnd == headerHwnd) return;

            try { ReleaseHandle(); } catch { }
            _hwnd = headerHwnd;
            AssignHandle(headerHwnd);
        }

        public void Invalidate()
        {
            if (_hwnd == IntPtr.Zero) return;
            try { InvalidateRect(_hwnd, IntPtr.Zero, false); } catch { }
        }

        public void Dispose()
        {
            try { ReleaseHandle(); } catch { }
            _hwnd = IntPtr.Zero;
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (_hwnd == IntPtr.Zero) return;

            if (m.Msg == WM_PAINT || m.Msg == WM_SIZE || m.Msg == WM_THEMECHANGED || m.Msg == WM_STYLECHANGED)
            {
                TryPaintTail();
            }
        }

        private void TryPaintTail()
        {
            try
            {
                var lv = _owner._listView;
                if (lv == null) return;
                if (!lv.IsHandleCreated) return;
                if (lv.View != View.Details) return;
                if (lv.Columns.Count <= 0) return;

                if (!GetClientRect(_hwnd, out var rc)) return;
                int w = rc.Right - rc.Left;
                int h = rc.Bottom - rc.Top;
                if (w <= 0 || h <= 0) return;

                int used = 0;
                foreach (ColumnHeader col in lv.Columns)
                    used += col.Width;

                if (used >= w) return;

                var tail = new Rectangle(used, 0, w - used, h);
                using var g = Graphics.FromHwnd(_hwnd);
                using var b = new SolidBrush(Color.FromArgb(45, 45, 45));
                g.FillRectangle(b, tail);
            }
            catch
            {
                // Best-effort UI polish only.
            }
        }

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}

