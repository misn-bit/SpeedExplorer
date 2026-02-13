using System;
using Microsoft.Win32;
using System.Windows.Forms;
using System.IO;

namespace SpeedExplorer;

public static class StartupService
{
    private const string StartupKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "SpeedExplorer";

    public static void SyncWithSettings()
    {
        try
        {
            var settings = AppSettings.Current;
            using (var key = Registry.CurrentUser.OpenSubKey(StartupKey, true))
            {
                if (key == null) return;

                if (settings.RunAtStartup)
                {
                    string exePath = Application.ExecutablePath;
                    string command = $"\"{exePath}\" --startup";
                    if (settings.StartMinimized)
                    {
                        command += " --minimized";
                    }
                    key.SetValue(AppName, command);
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to sync startup settings: {ex.Message}");
        }
    }
}
