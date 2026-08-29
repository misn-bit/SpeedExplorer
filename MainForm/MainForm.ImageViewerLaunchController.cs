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
        TryOpenImageViewerForImagePath(imagePath, State.Items.Select(static x => x.FullPath));
    }

    private async Task NavigateToAndMaybeOpenImageViewerAsync(
        string path,
        List<string>? selectPaths,
        string? imagePathForViewer)
    {
        await NavigateTo(path, selectPaths);

        if (!string.IsNullOrWhiteSpace(imagePathForViewer))
            TryOpenImageViewerForImagePath(imagePathForViewer, State.Items.Select(static x => x.FullPath));
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
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }

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
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }

        if (!File.Exists(normalizedImagePath))
            return false;
        if (!FileSystemService.IsImageFile(normalizedImagePath))
            return false;

        var imageFiles = ImageViewerSequenceBuilder.BuildForPath(normalizedImagePath, preferredImagePool);
        if (imageFiles.Count == 0)
            return false;

        int startIndex = imageFiles.FindIndex(p => string.Equals(p, normalizedImagePath, StringComparison.OrdinalIgnoreCase));
        if (startIndex < 0)
            startIndex = 0;

        var viewer = new ImageViewerForm(
            imageFiles,
            startIndex,
            new ImageViewerSortOptions(State.SortColumn, State.SortDirection, State.TaggedFilesOnTop));
        viewer.Show();
        return true;
    }

}
