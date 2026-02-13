using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    // Win32 for borderless window dragging
    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

    // Drag ghost now uses a lightweight overlay form.
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;

    [DllImport("user32.dll", EntryPoint = "SendMessage")]
    private static extern IntPtr SendMessagePtr(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    private const int SB_HORZ = 0;
    private const int SB_VERT = 1;
    private const int SIF_ALL = 0x17;
    private const int LVM_GETHEADER = 0x101F;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HT_CAPTION = 0x2;
    private const uint SEE_MASK_INVOKEIDLIST = 0xC;
    private const int WS_MINIMIZEBOX = 0x20000;
    private const int WS_SYSMENU = 0x80000;
    private const int WM_NCHITTEST = 0x84;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;
    private const int WM_NCCALCSIZE = 0x83;
    private const int WS_THICKFRAME = 0x40000;
    private const int WM_SETREDRAW = 0x000B;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string lpVerb;
        public string lpFile;
        public string lpParameters;
        public string lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIconOrMonitor;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHAddToRecentDocs(uint uFlags, string? pv);

    private const uint SHARD_PATHW = 0x00000003;

    private static void TryRegisterFolderInWindowsRecent(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        if (folderPath == ThisPcPathConst) return;
        if (IsShellPath(folderPath)) return;
        if (!Directory.Exists(folderPath)) return;
        try
        {
            // Skip drive roots: they are not useful as recent folders and can be slow on some systems.
            var root = Path.GetPathRoot(folderPath);
            if (!string.IsNullOrEmpty(root))
            {
                var normalizedPath = Path.TrimEndingDirectorySeparator(folderPath);
                var normalizedRoot = Path.TrimEndingDirectorySeparator(root);
                if (string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }
        catch
        {
        }

        try
        {
            SHAddToRecentDocs(SHARD_PATHW, folderPath);
        }
        catch
        {
            // Recent registration is best-effort.
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.Style |= WS_MINIMIZEBOX | WS_SYSMENU | WS_THICKFRAME;
            return cp;
        }
    }
}
