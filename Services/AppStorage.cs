using System;
using System.IO;

namespace SpeedExplorer;

/// <summary>
/// Resolves application-owned data while preserving the portable-app behavior.
/// Files are kept beside the executable whenever that directory is writable.
/// A per-user fallback is used only for installations that cannot write there.
/// </summary>
internal static class AppStorage
{
    private const string AppFolderName = "SpeedExplorer";
    private static readonly object Sync = new();

    public static string GetPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("A file name is required.", nameof(fileName));

        string portablePath = Path.Combine(AppContext.BaseDirectory, fileName);
        string fallbackPath = Path.Combine(GetFallbackDirectory(create: false), fileName);

        // Prefer the location that already contains the user's data. This avoids
        // silently switching between portable and fallback storage on each run.
        if (File.Exists(portablePath))
            return portablePath;
        if (File.Exists(fallbackPath))
            return fallbackPath;

        return CanWriteDirectory(Path.GetDirectoryName(portablePath)!)
            ? portablePath
            : Path.Combine(GetFallbackDirectory(create: true), fileName);
    }

    public static string WriteText(string currentPath, string fileName, string contents)
    {
        string targetPath = currentPath;
        string? directory = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrWhiteSpace(directory) || !CanWriteDirectory(directory))
            targetPath = Path.Combine(GetFallbackDirectory(create: true), fileName);

        directory = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new IOException($"Could not determine a storage directory for '{fileName}'.");

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporaryPath, contents);
            File.Move(temporaryPath, targetPath, overwrite: true);
            return targetPath;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch
            {
                // The successful write is still valid even if cleanup fails.
            }
        }
    }

    private static string GetFallbackDirectory(bool create)
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            localAppData = Path.GetTempPath();

        string directory = Path.Combine(localAppData, AppFolderName);
        if (create)
            Directory.CreateDirectory(directory);
        return directory;
    }

    private static bool CanWriteDirectory(string directory)
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string probePath = Path.Combine(directory, $".{Guid.NewGuid():N}.write-test");
                File.WriteAllText(probePath, "ok");
                File.Delete(probePath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
