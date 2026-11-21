using System;
using System.IO;

namespace SpeedExplorer;

/// <summary>
/// Resolves application-owned persisted data under the current user's local
/// application data directory. Existing portable settings are migrated there
/// once when first encountered.
/// </summary>
internal static class AppStorage
{
    private const string AppFolderName = "SpeedExplorer";
    private static readonly object Sync = new();

    public static string GetPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("A file name is required.", nameof(fileName));

        string userDataPath = Path.Combine(GetUserDataDirectory(), fileName);
        string legacyPortablePath = Path.Combine(AppContext.BaseDirectory, fileName);
        MigrateLegacyPortableFile(legacyPortablePath, userDataPath);
        return userDataPath;
    }

    public static string WriteText(string fileName, string contents)
    {
        string targetPath = GetPath(fileName);
        string? directory = Path.GetDirectoryName(targetPath);
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

    private static string GetUserDataDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            localAppData = Path.GetTempPath();

        string directory = Path.Combine(localAppData, AppFolderName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void MigrateLegacyPortableFile(string legacyPath, string userDataPath)
    {
        if (string.Equals(Path.GetFullPath(legacyPath), Path.GetFullPath(userDataPath), StringComparison.OrdinalIgnoreCase))
            return;

        lock (Sync)
        {
            if (File.Exists(userDataPath) || !File.Exists(legacyPath))
                return;

            string? directory = Path.GetDirectoryName(userDataPath);
            if (string.IsNullOrWhiteSpace(directory))
                return;

            string temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(userDataPath)}.{Guid.NewGuid():N}.migration.tmp");

            try
            {
                File.Copy(legacyPath, temporaryPath);
                File.Move(temporaryPath, userDataPath);
                try
                {
                    File.Delete(legacyPath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Migrated settings file but could not remove its old copy '{legacyPath}': {ex}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Failed to migrate portable settings file '{legacyPath}' to '{userDataPath}': {ex}");
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
                    // Migration succeeded if the destination was already moved.
                }
            }
        }
    }
}
