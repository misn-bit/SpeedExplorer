using System;
using System.IO;

namespace SpeedExplorer;

internal static class NavigationDebugLogger
{
    private static readonly object _sync = new();
    private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_navigation.log");

    public static void Log(string message)
    {
        if (!AppSettings.Current.DebugNavigationLogging) return;
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            lock (_sync)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
        }
    }
}

