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

        if (_sortOptions == null)
        {
            int insertIndex = FindWatchedFolderAppendIndex();
            _imagePaths.InsertRange(insertIndex, added);
        }
        else
        {
            added.Sort((a, b) => CompareImagePathsForSort(a, b, _sortOptions));
            foreach (var imagePath in added)
            {
                int insertIndex = FindWatchedFolderSortedInsertIndex(imagePath, _sortOptions);
                _imagePaths.Insert(insertIndex, imagePath);
            }
        }

        string? currentPath = GetCurrentImagePath();
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
            if (CompareImagePathsForSort(imagePath, _imagePaths[i], sortOptions) < 0)
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

    private static int CompareImagePathsForSort(string leftPath, string rightPath, ImageViewerSortOptions sortOptions)
    {
        if (string.Equals(leftPath, rightPath, StringComparison.OrdinalIgnoreCase))
            return 0;

        if (sortOptions.TaggedFilesOnTop)
        {
            bool leftTagged = TagManager.Instance.HasTags(leftPath);
            bool rightTagged = TagManager.Instance.HasTags(rightPath);
            if (leftTagged != rightTagged)
                return leftTagged ? -1 : 1;
        }

        var leftItem = CreateImageFileItemForSort(leftPath);
        var rightItem = CreateImageFileItemForSort(rightPath);
        return FileSystemService.CompareItems(leftItem, rightItem, sortOptions.Column, sortOptions.Direction);
    }

    private static FileItem CreateImageFileItemForSort(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return new FileItem
            {
                FullPath = info.FullName,
                Name = info.Name,
                IsDirectory = false,
                Size = info.Exists ? info.Length : 0,
                DateModified = info.Exists ? info.LastWriteTime : DateTime.MinValue,
                DateCreated = info.Exists ? info.CreationTime : DateTime.MinValue,
                Extension = info.Extension,
                DisplayPath = info.DirectoryName ?? ""
            };
        }
        catch
        {
            return new FileItem
            {
                FullPath = path,
                Name = Path.GetFileName(path),
                IsDirectory = false,
                Extension = Path.GetExtension(path),
                DisplayPath = Path.GetDirectoryName(path) ?? ""
            };
        }
    }

}
