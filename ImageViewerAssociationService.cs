using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SpeedExplorer;

public static class ImageViewerAssociationService
{
    private const string ProgId = "SpeedExplorer.Image";

    private static readonly string[] ImageExtensions = new[]
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif"
    };

    public static AssociationStatus GetCurrentStatus()
    {
        try
        {
            int owned = 0;
            foreach (var ext in ImageExtensions)
            {
                using var extKey = Registry.CurrentUser.OpenSubKey($"Software\\Classes\\{ext}", writable: false);
                var value = extKey?.GetValue("")?.ToString() ?? "";
                if (string.Equals(value, ProgId, StringComparison.OrdinalIgnoreCase))
                    owned++;
            }

            bool commandOk = false;
            using (var cmdKey = Registry.CurrentUser.OpenSubKey($"Software\\Classes\\{ProgId}\\shell\\open\\command", writable: false))
            {
                string expected = BuildOpenCommand(Application.ExecutablePath);
                string current = cmdKey?.GetValue("")?.ToString() ?? "";
                commandOk = string.Equals(current, expected, StringComparison.OrdinalIgnoreCase);
            }

            if (owned == 0)
                return AssociationStatus.WindowsDefaults;
            if (owned == ImageExtensions.Length && commandOk)
                return AssociationStatus.AppliedHkcu;
            return AssociationStatus.PartialHkcu;
        }
        catch
        {
            return AssociationStatus.WindowsDefaults;
        }
    }

    public static AssociationApplyResult ApplyFromUi(bool enable, IWin32Window? owner)
    {
        _ = owner;
        try
        {
            return ApplyInternal(enable);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ImageViewerAssociationService.ApplyFromUi failed: {ex.Message}");
            return new AssociationApplyResult { Success = false };
        }
    }

    private static AssociationApplyResult ApplyInternal(bool enable)
    {
        var result = new AssociationApplyResult();
        var root = Registry.CurrentUser;

        if (enable)
        {
            var backup = CaptureBackup(root);

            string exePath = Application.ExecutablePath;
            string command = BuildOpenCommand(exePath);
            WriteProgId(root, command, exePath);

            foreach (var ext in ImageExtensions)
            {
                using var extKey = root.CreateSubKey($"Software\\Classes\\{ext}");
                extKey?.SetValue("", ProgId, RegistryValueKind.String);
            }

            AppSettings.Current.ImageViewerFileAssocBackupJson = JsonSerializer.Serialize(backup);
            AppSettings.Current.ImageViewerFileAssocEnabled = true;
            AppSettings.Current.ImageViewerFileAssocScope = "HKCU";
            AppSettings.Current.Save();

            result.Success = true;
            result.HkcuApplied = true;
            return result;
        }

        var existingBackup = LoadBackup();
        if (existingBackup != null)
            RestoreBackup(root, existingBackup);
        else
            BestEffortRestore(root);

        try { root.DeleteSubKeyTree($"Software\\Classes\\{ProgId}", false); } catch { }

        AppSettings.Current.ImageViewerFileAssocBackupJson = "";
        AppSettings.Current.ImageViewerFileAssocEnabled = false;
        AppSettings.Current.ImageViewerFileAssocScope = "Windows Defaults";
        AppSettings.Current.Save();

        result.Success = true;
        result.HkcuApplied = true;
        return result;
    }

    private static string BuildOpenCommand(string exePath)
        => $"\"{exePath}\" \"%1\"";

    private static void WriteProgId(RegistryKey root, string command, string exePath)
    {
        using var progId = root.CreateSubKey($"Software\\Classes\\{ProgId}");
        progId?.SetValue("", "SpeedExplorer Image", RegistryValueKind.String);
        progId?.SetValue("FriendlyTypeName", "SpeedExplorer Image", RegistryValueKind.String);

        using var defaultIcon = root.CreateSubKey($"Software\\Classes\\{ProgId}\\DefaultIcon");
        defaultIcon?.SetValue("", $"\"{exePath}\",0", RegistryValueKind.String);

        using var openCmd = root.CreateSubKey($"Software\\Classes\\{ProgId}\\shell\\open\\command");
        openCmd?.SetValue("", command, RegistryValueKind.String);
    }

    private static AssociationBackup CaptureBackup(RegistryKey root)
    {
        var backup = new AssociationBackup();
        foreach (var ext in ImageExtensions)
        {
            using var key = root.OpenSubKey($"Software\\Classes\\{ext}", writable: false);
            var snap = new AssociationValueSnapshot
            {
                Extension = ext,
                KeyExists = key != null
            };

            if (key != null)
            {
                object? value = key.GetValue("", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (value != null)
                {
                    snap.ValueExists = true;
                    snap.Value = value.ToString();
                    try { snap.Kind = key.GetValueKind(""); } catch { snap.Kind = RegistryValueKind.String; }
                }
            }

            backup.Extensions.Add(snap);
        }
        return backup;
    }

    private static void RestoreBackup(RegistryKey root, AssociationBackup backup)
    {
        foreach (var snap in backup.Extensions)
        {
            if (string.IsNullOrWhiteSpace(snap.Extension))
                continue;

            using var key = root.CreateSubKey($"Software\\Classes\\{snap.Extension}");
            if (key == null)
                continue;

            try
            {
                if (!snap.ValueExists)
                {
                    key.DeleteValue("", false);
                }
                else
                {
                    key.SetValue("", snap.Value ?? "", snap.Kind);
                }
            }
            catch { }
        }
    }

    private static void BestEffortRestore(RegistryKey root)
    {
        foreach (var ext in ImageExtensions)
        {
            try
            {
                using var key = root.OpenSubKey($"Software\\Classes\\{ext}", writable: true);
                if (key == null)
                    continue;
                var current = key.GetValue("")?.ToString() ?? "";
                if (string.Equals(current, ProgId, StringComparison.OrdinalIgnoreCase))
                    key.DeleteValue("", false);
            }
            catch { }
        }
    }

    private static AssociationBackup? LoadBackup()
    {
        var json = AppSettings.Current.ImageViewerFileAssocBackupJson;
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<AssociationBackup>(json);
        }
        catch
        {
            return null;
        }
    }

    public class AssociationApplyResult
    {
        public bool Success { get; set; }
        public bool HkcuApplied { get; set; }
    }

    public enum AssociationStatus
    {
        WindowsDefaults,
        AppliedHkcu,
        PartialHkcu
    }

    public class AssociationBackup
    {
        public DateTime SavedAt { get; set; } = DateTime.UtcNow;
        public List<AssociationValueSnapshot> Extensions { get; set; } = new();
    }

    public class AssociationValueSnapshot
    {
        public string Extension { get; set; } = "";
        public bool KeyExists { get; set; }
        public bool ValueExists { get; set; }
        public string? Value { get; set; }
        public RegistryValueKind Kind { get; set; } = RegistryValueKind.String;
    }
}
