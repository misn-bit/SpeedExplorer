using System;
using System.IO;
using System.IO.Compression;

namespace SpeedExplorer;

public partial class MainForm
{
    private void CreateNewWinRarArchive() => _archiveController.CreateNewWinRarArchive();

    private static bool IsZipFilePath(string? path)
    {
        return !string.IsNullOrEmpty(path) &&
               string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);
    }

    private void ExtractZipHere() => _archiveController.ExtractZipHere();

    private void ExtractZipToFolder() => _archiveController.ExtractZipToFolder();

    private void CreateZipFromSelection() => _archiveController.CreateZipFromSelection();

    private void ExtractZipWithProgress(string zipPath, string destination)
        => _archiveController.ExtractZipWithProgress(zipPath, destination);

    private void CreateZipWithProgress(string[] paths, string outputZipPath, CompressionLevel level)
        => _archiveController.CreateZipWithProgress(paths, outputZipPath, level);

    private string GetWinRarPath() => _archiveController.GetWinRarPath();

    private static bool IsArchiveFilePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".rar" or ".zip" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz";
    }

    private void ExecuteWinRarAddPrompt(string[] paths) => _archiveController.ExecuteWinRarAddPrompt(paths);

    private void ExecuteWinRarExtractHere() => _archiveController.ExecuteWinRarExtractHere();

    private void ExecuteWinRarExtractTo() => _archiveController.ExecuteWinRarExtractTo();
}
