using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SpeedExplorer;

public static class FileManagerIntegrationService
{
    private const string EnableArg = "--apply-default-file-manager";
    private const string DisableArg = "--remove-default-file-manager";

    private static readonly string[] TargetRoots = new[]
    {
        "Software\\Classes\\Folder",
        "Software\\Classes\\Directory",
        "Software\\Classes\\Drive"
    };

    private static readonly string[] DefaultManagerRoots = new[]
    {
        "Software\\Classes\\Folder",
        "Software\\Classes\\Directory",
        "Software\\Classes\\Drive"
    };

    public static bool IsApplyArg(string arg) => string.Equals(arg, EnableArg, StringComparison.OrdinalIgnoreCase);
    public static bool IsRemoveArg(string arg) => string.Equals(arg, DisableArg, StringComparison.OrdinalIgnoreCase);

    public static IntegrationApplyResult ApplyFromCommandLine(bool enable)
    {
        var result = ApplyIntegrationInternal(enable, allowElevation: false, owner: null, forceElevatedScope: true);
        return result;
    }

    public static IntegrationApplyResult ApplyFromUi(bool enable, IWin32Window? owner)
    {
        var result = ApplyIntegrationInternal(enable, allowElevation: true, owner, forceElevatedScope: false);
        return result;
    }

    public static IntegrationStatus GetCurrentStatus()
    {
        string exePath = Application.ExecutablePath;
        string expected = BuildShellOpenCommand(exePath);

        bool hkcuOk = IsRegistryApplied(Registry.CurrentUser, expected);
        bool hklmOk = false;
        try { hklmOk = IsRegistryApplied(Registry.LocalMachine, expected); } catch { }

        if (hkcuOk && hklmOk) return IntegrationStatus.AppliedHkcuHklm;
        if (hkcuOk) return IntegrationStatus.AppliedHkcu;
        if (hklmOk) return IntegrationStatus.AppliedHklm;
        return IntegrationStatus.WindowsDefaults;
    }

    private static IntegrationApplyResult ApplyIntegrationInternal(bool enable, bool allowElevation, IWin32Window? owner, bool forceElevatedScope)
    {
        var result = new IntegrationApplyResult();
        bool isAdmin = IsAdministrator();
        string exePath = Application.ExecutablePath;
        string command = BuildShellOpenCommand(exePath);

        if (!isAdmin && allowElevation)
        {
            var prompt = MessageBox.Show(
                owner,
                "Admin access is recommended to apply system-wide defaults. Do you want to elevate?",
                "Admin Required",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (prompt == DialogResult.Yes)
            {
                if (TryRunElevated(enable))
                {
                    result.ElevationLaunched = true;
                    return result;
                }
            }
        }

        bool doHkcu = true;
        bool doHklm = isAdmin || forceElevatedScope;

        var backup = enable ? new IntegrationBackup() : LoadBackup();

        if (enable)
        {
            if (backup == null)
                backup = new IntegrationBackup();

            if (doHkcu)
                BackupHive(Registry.CurrentUser, "HKCU", backup);
            if (doHklm)
                BackupHive(Registry.LocalMachine, "HKLM", backup);

            WriteDefaults(Registry.CurrentUser, command);
            WriteContextMenu(Registry.CurrentUser, exePath);
            result.HkcuApplied = true;

            if (doHklm)
            {
                try
                {
                    WriteDefaults(Registry.LocalMachine, command);
                    WriteContextMenu(Registry.LocalMachine, exePath);
                    result.HklmApplied = true;
                }
                catch { }
            }

            AppSettings.Current.DefaultFileManagerBackupJson = JsonSerializer.Serialize(backup);
        }
        else
        {
            if (backup != null)
            {
                if (doHkcu)
                {
                    RestoreHive(Registry.CurrentUser, "HKCU", backup);
                    RemoveContextMenu(Registry.CurrentUser);
                    RestoreExplorerDefaults(Registry.CurrentUser);
                    result.HkcuApplied = true;
                }
                if (doHklm)
                {
                    try
                    {
                        RestoreHive(Registry.LocalMachine, "HKLM", backup);
                        RemoveContextMenu(Registry.LocalMachine);
                        RestoreExplorerDefaults(Registry.LocalMachine);
                        result.HklmApplied = true;
                    }
                    catch { }
                }
            }
            else
            {
                if (doHkcu)
                {
                    RestoreExplorerDefaults(Registry.CurrentUser);
                    RemoveContextMenu(Registry.CurrentUser);
                    result.HkcuApplied = true;
                }
                if (doHklm)
                {
                    try
                    {
                        RestoreExplorerDefaults(Registry.LocalMachine);
                        RemoveContextMenu(Registry.LocalMachine);
                        result.HklmApplied = true;
                    }
                    catch { }
                }
            }

            AppSettings.Current.DefaultFileManagerBackupJson = "";
        }

        AppSettings.Current.DefaultFileManagerEnabled = enable;
        if (enable)
            AppSettings.Current.DefaultFileManagerScope = result.HklmApplied ? "HKCU+HKLM" : "HKCU";
        else
            AppSettings.Current.DefaultFileManagerScope = "Windows Defaults";
        AppSettings.Current.Save();

        result.Success = result.HkcuApplied || result.HklmApplied;
        return result;
    }

    private static void WriteDefaults(RegistryKey root, string command)
    {
        foreach (var basePath in DefaultManagerRoots)
        {
            using var shellKey = root.CreateSubKey($"{basePath}\\shell");
            shellKey?.SetValue("", "open", RegistryValueKind.String);

            using var openCmd = root.CreateSubKey($"{basePath}\\shell\\open\\command");
            openCmd?.SetValue("", command, RegistryValueKind.String);
            openCmd?.DeleteValue("DelegateExecute", false);
            openCmd?.DeleteValue("ExplorerCommandHandler", false);

            try { root.DeleteSubKeyTree($"{basePath}\\shell\\open\\DropTarget", false); } catch { }

            using var exploreCmd = root.CreateSubKey($"{basePath}\\shell\\explore\\command");
            exploreCmd?.SetValue("", command, RegistryValueKind.String);
            exploreCmd?.DeleteValue("DelegateExecute", false);
            exploreCmd?.DeleteValue("ExplorerCommandHandler", false);

            using var openNewWindowCmd = root.CreateSubKey($"{basePath}\\shell\\opennewwindow\\command");
            openNewWindowCmd?.SetValue("", command, RegistryValueKind.String);
            openNewWindowCmd?.DeleteValue("DelegateExecute", false);
            openNewWindowCmd?.DeleteValue("ExplorerCommandHandler", false);

            using var findVerb = root.OpenSubKey($"{basePath}\\shell\\find", writable: true);
            if (findVerb != null)
            {
                using var findCmd = root.CreateSubKey($"{basePath}\\shell\\find\\command");
                findCmd?.SetValue("", command, RegistryValueKind.String);
                findCmd?.DeleteValue("DelegateExecute", false);
                findCmd?.DeleteValue("ExplorerCommandHandler", false);
                try { root.DeleteSubKeyTree($"{basePath}\\shell\\find\\DropTarget", false); } catch { }
            }

            SanitizeVerbMetadata(root, basePath, "open");
            SanitizeVerbMetadata(root, basePath, "explore");
            SanitizeVerbMetadata(root, basePath, "opennewwindow");
            SanitizeVerbMetadata(root, basePath, "find");

            if (basePath.EndsWith("Folder", StringComparison.OrdinalIgnoreCase))
            {
                using var openNewProcessCmd = root.CreateSubKey($"{basePath}\\shell\\opennewprocess\\command");
                openNewProcessCmd?.SetValue("", command, RegistryValueKind.String);
                openNewProcessCmd?.DeleteValue("DelegateExecute", false);
                openNewProcessCmd?.DeleteValue("ExplorerCommandHandler", false);

                using var openNewTabCmd = root.CreateSubKey($"{basePath}\\shell\\opennewtab\\command");
                openNewTabCmd?.SetValue("", command, RegistryValueKind.String);
                openNewTabCmd?.DeleteValue("DelegateExecute", false);
                openNewTabCmd?.DeleteValue("ExplorerCommandHandler", false);

                SanitizeVerbMetadata(root, basePath, "opennewprocess");
                SanitizeVerbMetadata(root, basePath, "opennewtab");
            }
        }

    }

    private static void SanitizeVerbMetadata(RegistryKey root, string basePath, string verb)
    {
        try
        {
            using var verbKey = root.OpenSubKey($"{basePath}\\shell\\{verb}", writable: true);
            if (verbKey == null) return;

            // Explorer-specific metadata can force shell handler fallbacks/timeouts
            // before command execution when another file manager owns the verb.
            foreach (var name in new[]
            {
                "DelegateExecute",
                "ExplorerCommandHandler",
                "ExplorerHost",
                "CommandStateHandler",
                "CommandStateSync",
                "OnlyInBrowserWindow",
                "ProgrammaticAccessOnly",
                "Extended",
                "AppliesTo",
                "MUIVerb",
                "LaunchExplorerFlags"
            })
            {
                verbKey.DeleteValue(name, false);
            }
        }
        catch { }

        try
        {
            root.DeleteSubKeyTree($"{basePath}\\shell\\{verb}\\ddeexec", false);
        }
        catch { }
    }

    private static bool IsRegistryApplied(RegistryKey root, string expectedCommand)
    {
        foreach (var basePath in DefaultManagerRoots)
        {
            using var openCmd = root.OpenSubKey($"{basePath}\\shell\\open\\command", writable: false);
            var current = openCmd?.GetValue("")?.ToString() ?? "";
            if (!string.Equals(current, expectedCommand, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static void WriteContextMenu(RegistryKey root, string exePath)
    {
        foreach (var basePath in TargetRoots)
        {
            using var menuKey = root.CreateSubKey($"{basePath}\\shell\\open_speed_explorer");
            if (menuKey == null) continue;
            menuKey.SetValue("", "Open in Speed Explorer", RegistryValueKind.String);
            menuKey.SetValue("Icon", $"\"{exePath}\"", RegistryValueKind.String);

            using var cmdKey = root.CreateSubKey($"{basePath}\\shell\\open_speed_explorer\\command");
            cmdKey?.SetValue("", BuildShellOpenCommand(exePath), RegistryValueKind.String);
        }
    }

    private static string BuildShellOpenCommand(string exePath)
        => $"\"{exePath}\" \"%1\" %*";

    private static void RemoveContextMenu(RegistryKey root)
    {
        foreach (var basePath in TargetRoots)
        {
            try { root.DeleteSubKeyTree($"{basePath}\\shell\\open_speed_explorer", false); }
            catch { }
        }
    }

    private static void RestoreExplorerDefaults(RegistryKey root)
        => RestoreExplorerDefaults(root, TargetRoots);

    private static void RestoreExplorerDefaults(RegistryKey root, IEnumerable<string> basePaths)
    {
        const string explorerDelegate = "{11dbb47c-a525-400b-9e80-a54615a090c0}";

        foreach (var basePath in basePaths)
        {
            using var shellKey = root.CreateSubKey($"{basePath}\\shell");
            shellKey?.SetValue("", "open", RegistryValueKind.String);

            using var openCmd = root.CreateSubKey($"{basePath}\\shell\\open\\command");
            openCmd?.SetValue("", "explorer.exe \"%1\"", RegistryValueKind.String);
            openCmd?.SetValue("DelegateExecute", explorerDelegate, RegistryValueKind.String);

            using var openVerb = root.CreateSubKey($"{basePath}\\shell\\open");
            openVerb?.SetValue("MultiSelectModel", "Document", RegistryValueKind.String);

            using var exploreCmd = root.CreateSubKey($"{basePath}\\shell\\explore\\command");
            exploreCmd?.SetValue("", "explorer.exe /e,\"%1\"", RegistryValueKind.String);
            exploreCmd?.SetValue("DelegateExecute", explorerDelegate, RegistryValueKind.String);

            using var exploreVerb = root.CreateSubKey($"{basePath}\\shell\\explore");
            exploreVerb?.SetValue("LaunchExplorerFlags", 0x18, RegistryValueKind.DWord);
            exploreVerb?.SetValue("MultiSelectModel", "Document", RegistryValueKind.String);
            exploreVerb?.SetValue("ProgrammaticAccessOnly", "", RegistryValueKind.String);

            using var openNewWindowCmd = root.CreateSubKey($"{basePath}\\shell\\opennewwindow\\command");
            openNewWindowCmd?.SetValue("", "", RegistryValueKind.String);
            openNewWindowCmd?.SetValue("DelegateExecute", explorerDelegate, RegistryValueKind.String);

            using var openNewWindowVerb = root.CreateSubKey($"{basePath}\\shell\\opennewwindow");
            openNewWindowVerb?.SetValue("LaunchExplorerFlags", 0x1, RegistryValueKind.DWord);
            openNewWindowVerb?.SetValue("MUIVerb", "@windows.storage.dll,-8517", RegistryValueKind.String);
            openNewWindowVerb?.SetValue("MultiSelectModel", "Document", RegistryValueKind.String);
            openNewWindowVerb?.SetValue("OnlyInBrowserWindow", "", RegistryValueKind.String);

            if (basePath.EndsWith("Folder", StringComparison.OrdinalIgnoreCase))
            {
                using var openNewProcessCmd = root.CreateSubKey($"{basePath}\\shell\\opennewprocess\\command");
                openNewProcessCmd?.SetValue("", "", RegistryValueKind.String);
                openNewProcessCmd?.SetValue("DelegateExecute", explorerDelegate, RegistryValueKind.String);

                using var openNewProcessVerb = root.CreateSubKey($"{basePath}\\shell\\opennewprocess");
                openNewProcessVerb?.SetValue("ExplorerHost", "{ceff45ee-c862-41de-aee2-a022c81eda92}", RegistryValueKind.String);
                openNewProcessVerb?.SetValue("Extended", "", RegistryValueKind.String);
                openNewProcessVerb?.SetValue("LaunchExplorerFlags", 0x3, RegistryValueKind.DWord);
                openNewProcessVerb?.SetValue("MUIVerb", "@shell32.dll,-8518", RegistryValueKind.String);
                openNewProcessVerb?.SetValue("MultiSelectModel", "Document", RegistryValueKind.String);

                using var openNewTabCmd = root.CreateSubKey($"{basePath}\\shell\\opennewtab\\command");
                openNewTabCmd?.SetValue("", "", RegistryValueKind.String);
                openNewTabCmd?.SetValue("DelegateExecute", explorerDelegate, RegistryValueKind.String);

                using var openNewTabVerb = root.CreateSubKey($"{basePath}\\shell\\opennewtab");
                openNewTabVerb?.SetValue("CommandStateHandler", "{11dbb47c-a525-400b-9e80-a54615a090c0}", RegistryValueKind.String);
                openNewTabVerb?.SetValue("CommandStateSync", "", RegistryValueKind.String);
                openNewTabVerb?.SetValue("LaunchExplorerFlags", 0x60, RegistryValueKind.DWord);
                openNewTabVerb?.SetValue("MUIVerb", "@windows.storage.dll,-8519", RegistryValueKind.String);
                openNewTabVerb?.SetValue("MultiSelectModel", "Document", RegistryValueKind.String);
                openNewTabVerb?.SetValue("OnlyInBrowserWindow", "", RegistryValueKind.String);
            }
        }
    }

    private static void BackupHive(RegistryKey root, string hiveName, IntegrationBackup backup)
    {
        foreach (var basePath in TargetRoots)
        {
            BackupKey(root, hiveName, $"{basePath}\\shell", backup);
            BackupKey(root, hiveName, $"{basePath}\\shell\\open\\command", backup);
            BackupKey(root, hiveName, $"{basePath}\\shell\\open\\DropTarget", backup);
            BackupKey(root, hiveName, $"{basePath}\\shell\\explore\\command", backup);
            BackupKey(root, hiveName, $"{basePath}\\shell\\find", backup);
            BackupKey(root, hiveName, $"{basePath}\\shell\\find\\command", backup);
            BackupKey(root, hiveName, $"{basePath}\\shell\\find\\DropTarget", backup);
            BackupKey(root, hiveName, $"{basePath}\\shell\\opennewwindow", backup);
            BackupKey(root, hiveName, $"{basePath}\\shell\\opennewwindow\\command", backup);
            if (basePath.EndsWith("Folder", StringComparison.OrdinalIgnoreCase))
            {
                BackupKey(root, hiveName, $"{basePath}\\shell\\opennewprocess", backup);
                BackupKey(root, hiveName, $"{basePath}\\shell\\opennewprocess\\command", backup);
                BackupKey(root, hiveName, $"{basePath}\\shell\\opennewtab", backup);
                BackupKey(root, hiveName, $"{basePath}\\shell\\opennewtab\\command", backup);
            }
            BackupKey(root, hiveName, $"{basePath}\\shell\\open_speed_explorer", backup);
            BackupKey(root, hiveName, $"{basePath}\\shell\\open_speed_explorer\\command", backup);
        }
    }

    private static void BackupKey(RegistryKey root, string hiveName, string subKeyPath, IntegrationBackup backup)
    {
        using var key = root.OpenSubKey(subKeyPath, writable: false);
        var snapshot = new RegistryKeySnapshot
        {
            Hive = hiveName,
            Path = subKeyPath,
            KeyExists = key != null,
            Values = new List<RegistryValueSnapshot>()
        };

        if (key != null)
        {
            foreach (var valueName in new[]
            {
                "",
                "Icon",
                "DelegateExecute",
                "ExplorerCommandHandler",
                "ExplorerHost",
                "CommandStateHandler",
                "CommandStateSync",
                "OnlyInBrowserWindow",
                "ProgrammaticAccessOnly",
                "Extended",
                "AppliesTo",
                "MUIVerb",
                "LaunchExplorerFlags",
                "MultiSelectModel"
            })
            {
                var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                var kind = RegistryValueKind.String;
                if (value != null)
                {
                    try { kind = key.GetValueKind(valueName); } catch { }
                }
                snapshot.Values.Add(new RegistryValueSnapshot
                {
                    Name = valueName,
                    Exists = value != null,
                    Value = value?.ToString(),
                    Kind = kind
                });
            }
        }
        else
        {
            snapshot.Values.Add(new RegistryValueSnapshot { Name = "", Exists = false });
            snapshot.Values.Add(new RegistryValueSnapshot { Name = "Icon", Exists = false });
            snapshot.Values.Add(new RegistryValueSnapshot { Name = "DelegateExecute", Exists = false });
            snapshot.Values.Add(new RegistryValueSnapshot { Name = "ExplorerCommandHandler", Exists = false });
            snapshot.Values.Add(new RegistryValueSnapshot { Name = "ExplorerHost", Exists = false });
            snapshot.Values.Add(new RegistryValueSnapshot { Name = "CommandStateHandler", Exists = false });
            snapshot.Values.Add(new RegistryValueSnapshot { Name = "CommandStateSync", Exists = false });
            snapshot.Values.Add(new RegistryValueSnapshot { Name = "OnlyInBrowserWindow", Exists = false });
            snapshot.Values.Add(new RegistryValueSnapshot { Name = "ProgrammaticAccessOnly", Exists = false });
            snapshot.Values.Add(new RegistryValueSnapshot { Name = "Extended", Exists = false });
            snapshot.Values.Add(new RegistryValueSnapshot { Name = "AppliesTo", Exists = false });
            snapshot.Values.Add(new RegistryValueSnapshot { Name = "MUIVerb", Exists = false });
            snapshot.Values.Add(new RegistryValueSnapshot { Name = "LaunchExplorerFlags", Exists = false });
            snapshot.Values.Add(new RegistryValueSnapshot { Name = "MultiSelectModel", Exists = false });
        }

        backup.Keys.Add(snapshot);
    }

    private static void RestoreHive(RegistryKey root, string hiveName, IntegrationBackup backup)
    {
        foreach (var snapshot in backup.Keys)
        {
            if (!string.Equals(snapshot.Hive, hiveName, StringComparison.OrdinalIgnoreCase))
                continue;
            RestoreKey(root, snapshot);
        }
    }

    private static void RestoreKey(RegistryKey root, RegistryKeySnapshot snapshot)
    {
        using var key = root.OpenSubKey(snapshot.Path, writable: true);
        if (key == null)
        {
            if (!snapshot.KeyExists)
                return;
            using var created = root.CreateSubKey(snapshot.Path);
            if (created == null) return;
            RestoreValues(created, snapshot);
            return;
        }

        RestoreValues(key, snapshot);

        if (!snapshot.KeyExists)
        {
            try
            {
                if (key.ValueCount == 0 && key.SubKeyCount == 0)
                {
                    key.Close();
                    root.DeleteSubKey(snapshot.Path, false);
                }
            }
            catch { }
        }
    }

    private static void RestoreValues(RegistryKey key, RegistryKeySnapshot snapshot)
    {
        foreach (var value in snapshot.Values)
        {
            try
            {
                if (!value.Exists)
                {
                    key.DeleteValue(value.Name, false);
                }
                else
                {
                    key.SetValue(value.Name, value.Value ?? "", value.Kind);
                }
            }
            catch { }
        }
    }

    private static void BestEffortRevert(RegistryKey root, string command)
    {
        foreach (var basePath in TargetRoots)
        {
            using var openCmd = root.OpenSubKey($"{basePath}\\shell\\open\\command", writable: true);
            if (openCmd != null)
            {
                var current = openCmd.GetValue("")?.ToString();
                if (string.Equals(current, command, StringComparison.OrdinalIgnoreCase))
                    openCmd.DeleteValue("", false);
            }

            using var exploreCmd = root.OpenSubKey($"{basePath}\\shell\\explore\\command", writable: true);
            if (exploreCmd != null)
            {
                var current = exploreCmd.GetValue("")?.ToString();
                if (string.Equals(current, command, StringComparison.OrdinalIgnoreCase))
                    exploreCmd.DeleteValue("", false);
            }

            using var openNewWindowCmd = root.OpenSubKey($"{basePath}\\shell\\opennewwindow\\command", writable: true);
            if (openNewWindowCmd != null)
            {
                var current = openNewWindowCmd.GetValue("")?.ToString();
                if (string.Equals(current, command, StringComparison.OrdinalIgnoreCase))
                    openNewWindowCmd.DeleteValue("", false);
            }

            using var findCmd = root.OpenSubKey($"{basePath}\\shell\\find\\command", writable: true);
            if (findCmd != null)
            {
                var current = findCmd.GetValue("")?.ToString();
                if (string.Equals(current, command, StringComparison.OrdinalIgnoreCase))
                    findCmd.DeleteValue("", false);
            }

            if (basePath.EndsWith("Folder", StringComparison.OrdinalIgnoreCase))
            {
                using var openNewProcessCmd = root.OpenSubKey($"{basePath}\\shell\\opennewprocess\\command", writable: true);
                if (openNewProcessCmd != null)
                {
                    var current = openNewProcessCmd.GetValue("")?.ToString();
                    if (string.Equals(current, command, StringComparison.OrdinalIgnoreCase))
                        openNewProcessCmd.DeleteValue("", false);
                }

                using var openNewTabCmd = root.OpenSubKey($"{basePath}\\shell\\opennewtab\\command", writable: true);
                if (openNewTabCmd != null)
                {
                    var current = openNewTabCmd.GetValue("")?.ToString();
                    if (string.Equals(current, command, StringComparison.OrdinalIgnoreCase))
                        openNewTabCmd.DeleteValue("", false);
                }
            }
        }
    }

    private static IntegrationBackup? LoadBackup()
    {
        var json = AppSettings.Current.DefaultFileManagerBackupJson;
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<IntegrationBackup>(json);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryRunElevated(bool enable)
    {
        try
        {
            var args = enable ? EnableArg : DisableArg;
            var psi = new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                Arguments = args,
                Verb = "runas",
                UseShellExecute = true
            };
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public class IntegrationApplyResult
    {
        public bool Success { get; set; }
        public bool HkcuApplied { get; set; }
        public bool HklmApplied { get; set; }
        public bool ElevationLaunched { get; set; }
    }

    public enum IntegrationStatus
    {
        WindowsDefaults,
        AppliedHkcu,
        AppliedHklm,
        AppliedHkcuHklm
    }

    public class IntegrationBackup
    {
        public DateTime SavedAt { get; set; } = DateTime.UtcNow;
        public List<RegistryKeySnapshot> Keys { get; set; } = new();
    }

    public class RegistryKeySnapshot
    {
        public string Hive { get; set; } = "";
        public string Path { get; set; } = "";
        public bool KeyExists { get; set; }
        public List<RegistryValueSnapshot> Values { get; set; } = new();
    }

    public class RegistryValueSnapshot
    {
        public string Name { get; set; } = "";
        public bool Exists { get; set; }
        public string? Value { get; set; }
        public RegistryValueKind Kind { get; set; } = RegistryValueKind.String;
    }
}
