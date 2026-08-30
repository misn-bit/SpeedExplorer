using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    private void EnsureImageFolderWatcher(string imagePath)
    {
        string? folder = Path.GetDirectoryName(imagePath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        folder = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(_watchedImageFolder, folder, StringComparison.OrdinalIgnoreCase))
            return;

        _imageFolderWatcher?.Dispose();
        _imageFolderWatcher = null;
        _watchedImageFolder = folder;

        try
        {
            _imageFolderWatcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            _imageFolderWatcher.Created += ImageFolderWatcher_FileChanged;
            _imageFolderWatcher.Renamed += ImageFolderWatcher_FileChanged;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to watch image viewer folder: {ex.Message}");
        }
    }

    private void ImageFolderWatcher_FileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            BeginInvoke(new Action(() =>
            {
                _imageFolderRefreshTimer.Stop();
                _imageFolderRefreshTimer.Start();
            }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to schedule image viewer folder refresh: {ex.Message}");
        }
    }

    private void ImageFolderRefreshTimer_Tick(object? sender, EventArgs e)
    {
        _imageFolderRefreshTimer.Stop();
        AddNewImagesFromWatchedFolder();
    }

    private void AddNewImagesFromWatchedFolder()
    {
        if (string.IsNullOrWhiteSpace(_watchedImageFolder) || !Directory.Exists(_watchedImageFolder))
            return;

        var seen = new HashSet<string>(_imagePaths, StringComparer.OrdinalIgnoreCase);
        var added = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(_watchedImageFolder))
            {
                string fullPath;
                try { fullPath = Path.GetFullPath(file); }
                catch { continue; }

                if (!seen.Add(fullPath))
                    continue;
                if (!FileSystemService.IsImageFile(fullPath))
                    continue;

                added.Add(fullPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to add new image viewer files: {ex.Message}");
            return;
        }

        if (added.Count == 0)
            return;

        string? currentPath = GetCurrentImagePath();

        if (_sortOptions == null)
        {
            int insertIndex = FindWatchedFolderAppendIndex();
            _imagePaths.InsertRange(insertIndex, added);
        }
        else
        {
            added.Sort((a, b) => ImageViewerPathComparer.Compare(a, b, _sortOptions));
            foreach (var imagePath in added)
            {
                int insertIndex = FindWatchedFolderSortedInsertIndex(imagePath, _sortOptions);
                _imagePaths.Insert(insertIndex, imagePath);
            }
        }

        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            int newIndex = _imagePaths.FindIndex(path => string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase));
            if (newIndex >= 0)
                _currentIndex = newIndex;
        }

        _indexLabel.Text = $"{_currentIndex + 1} / {_imagePaths.Count}";
    }

    private int FindWatchedFolderAppendIndex()
    {
        if (string.IsNullOrWhiteSpace(_watchedImageFolder))
            return _imagePaths.Count;

        int lastFolderImageIndex = -1;
        for (int i = 0; i < _imagePaths.Count; i++)
        {
            string? folder = Path.GetDirectoryName(_imagePaths[i]);
            if (!string.IsNullOrWhiteSpace(folder) &&
                string.Equals(
                    folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    _watchedImageFolder,
                    StringComparison.OrdinalIgnoreCase))
            {
                lastFolderImageIndex = i;
            }
        }

        return lastFolderImageIndex >= 0 ? lastFolderImageIndex + 1 : _imagePaths.Count;
    }

    private int FindWatchedFolderSortedInsertIndex(string imagePath, ImageViewerSortOptions sortOptions)
    {
        if (string.IsNullOrWhiteSpace(_watchedImageFolder))
            return _imagePaths.Count;

        int lastFolderImageIndex = -1;
        for (int i = 0; i < _imagePaths.Count; i++)
        {
            if (!IsPathInWatchedImageFolder(_imagePaths[i]))
                continue;

            lastFolderImageIndex = i;
            if (ImageViewerPathComparer.Compare(imagePath, _imagePaths[i], sortOptions) < 0)
                return i;
        }

        return lastFolderImageIndex >= 0 ? lastFolderImageIndex + 1 : _imagePaths.Count;
    }

    private bool IsPathInWatchedImageFolder(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(_watchedImageFolder))
            return false;

        string? folder = Path.GetDirectoryName(imagePath);
        return !string.IsNullOrWhiteSpace(folder) &&
            string.Equals(
                folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                _watchedImageFolder,
                StringComparison.OrdinalIgnoreCase);
    }

}
