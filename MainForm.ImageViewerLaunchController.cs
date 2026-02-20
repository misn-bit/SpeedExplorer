using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SpeedExplorer;

public partial class MainForm
{
    private string? _startupImagePath;

    private void CaptureStartupImageCandidate(string? rawPath)
    {
        _startupImagePath = ResolveImagePathForBuiltInViewer(rawPath);
    }

    private void OpenStartupImageViewerIfPending()
    {
        if (string.IsNullOrWhiteSpace(_startupImagePath))
            return;

        string imagePath = _startupImagePath;
        _startupImagePath = null;
        TryOpenImageViewerForImagePath(imagePath, _items.Select(static x => x.FullPath));
    }

    private async Task NavigateToAndMaybeOpenImageViewerAsync(
        string path,
        List<string>? selectPaths,
        string? imagePathForViewer)
    {
        await NavigateTo(path, selectPaths);

        if (!string.IsNullOrWhiteSpace(imagePathForViewer))
            TryOpenImageViewerForImagePath(imagePathForViewer, _items.Select(static x => x.FullPath));
    }

    private string? ResolveImagePathForBuiltInViewer(string? rawPath)
    {
        if (!AppSettings.Current.UseBuiltInImageViewer)
            return null;
        if (string.IsNullOrWhiteSpace(rawPath))
            return null;

        string? candidate = Program.ExtractStartPathFromSingleArg(rawPath);
        if (string.IsNullOrWhiteSpace(candidate))
            candidate = rawPath;

        candidate = candidate.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        try
        {
            candidate = Path.GetFullPath(candidate);
        }
        catch { }

        if (!File.Exists(candidate))
            return null;
        if (!FileSystemService.IsImageFile(candidate))
            return null;

        return candidate;
    }

    private bool TryOpenImageViewerForImagePath(string imagePath, IEnumerable<string>? preferredImagePool = null)
    {
        if (!AppSettings.Current.UseBuiltInImageViewer)
            return false;
        if (string.IsNullOrWhiteSpace(imagePath))
            return false;

        string normalizedImagePath = imagePath.Trim();
        try
        {
            normalizedImagePath = Path.GetFullPath(normalizedImagePath);
        }
        catch { }

        if (!File.Exists(normalizedImagePath))
            return false;
        if (!FileSystemService.IsImageFile(normalizedImagePath))
            return false;

        var imageFiles = BuildImageSequenceForPath(normalizedImagePath, preferredImagePool);
        if (imageFiles.Count == 0)
            return false;

        int startIndex = imageFiles.FindIndex(p => string.Equals(p, normalizedImagePath, StringComparison.OrdinalIgnoreCase));
        if (startIndex < 0)
            startIndex = 0;

        var viewer = new ImageViewerForm(imageFiles, startIndex);
        viewer.Show();
        return true;
    }

    private static List<string> BuildImageSequenceForPath(string imagePath, IEnumerable<string>? preferredImagePool)
    {
        var directory = Path.GetDirectoryName(imagePath);
        if (string.IsNullOrWhiteSpace(directory))
            return new List<string> { imagePath };

        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (preferredImagePool != null)
        {
            foreach (var candidate in preferredImagePool)
            {
                if (TryNormalizeImageCandidate(candidate, directory, out var normalized) && seen.Add(normalized))
                    results.Add(normalized);
            }
        }

        if (results.Count == 0)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    if (TryNormalizeImageCandidate(file, directory, out var normalized) && seen.Add(normalized))
                        results.Add(normalized);
                }
                results.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                // Fall back to just the explicitly requested image.
            }
        }

        if (seen.Add(imagePath))
            results.Add(imagePath);

        return results;
    }

    private static bool TryNormalizeImageCandidate(string? candidatePath, string requiredDirectory, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(candidatePath))
            return false;

        string candidate = candidatePath.Trim();
        try
        {
            candidate = Path.GetFullPath(candidate);
        }
        catch
        {
            return false;
        }

        if (!File.Exists(candidate))
            return false;
        if (!FileSystemService.IsImageFile(candidate))
            return false;

        var candidateDir = Path.GetDirectoryName(candidate);
        if (string.IsNullOrWhiteSpace(candidateDir))
            return false;

        if (!string.Equals(
                candidateDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                requiredDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalizedPath = candidate;
        return true;
    }
}
