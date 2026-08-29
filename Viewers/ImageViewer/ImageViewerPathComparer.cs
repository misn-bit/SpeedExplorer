using System;
using System.IO;

namespace SpeedExplorer;

internal static class ImageViewerPathComparer
{
    public static int Compare(string leftPath, string rightPath, ImageViewerSortOptions sortOptions)
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

        var leftItem = CreateFileItem(leftPath);
        var rightItem = CreateFileItem(rightPath);
        return FileSystemService.CompareItems(leftItem, rightItem, sortOptions.Column, sortOptions.Direction);
    }

    private static FileItem CreateFileItem(string path)
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
        catch (IOException)
        {
            return CreateFallbackFileItem(path);
        }
        catch (UnauthorizedAccessException)
        {
            return CreateFallbackFileItem(path);
        }
        catch (ArgumentException)
        {
            return CreateFallbackFileItem(path);
        }
    }

    private static FileItem CreateFallbackFileItem(string path)
        => new()
        {
            FullPath = path,
            Name = Path.GetFileName(path),
            IsDirectory = false,
            Extension = Path.GetExtension(path),
            DisplayPath = Path.GetDirectoryName(path) ?? ""
        };
}
