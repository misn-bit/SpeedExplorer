using System;
using System.Runtime.InteropServices;

namespace SpeedExplorer.Design;

internal static class Native
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Rounded corners for popups on Windows 11. Ignored where unsupported.</summary>
    public static void RoundCorners(IntPtr hwnd)
    {
        int v = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33, ref v, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
    }

    /// <summary>Dark (or light) title bar. Windows 10 20H1+ and Windows 11; harmless elsewhere.</summary>
    public static void UseDarkTitleBar(IntPtr hwnd, bool dark)
    {
        int v = dark ? 1 : 0;
        // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE; 19 was the same flag on early Windows 10 builds.
        if (DwmSetWindowAttribute(hwnd, 20, ref v, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref v, sizeof(int));
    }
}
