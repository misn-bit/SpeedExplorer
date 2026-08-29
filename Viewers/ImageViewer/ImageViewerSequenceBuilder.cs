using System;
using System.Collections.Generic;
using System.IO;
using System.Security;

namespace SpeedExplorer;

internal static class ImageViewerSequenceBuilder
{
    public static List<string> BuildForPath(string imagePath, IEnumerable<string>? preferredImagePool)
    {
        string? directory = Path.GetDirectoryName(imagePath);
        if (string.IsNullOrWhiteSpace(directory))
            return new List<string> { imagePath };

        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (preferredImagePool != null)
        {
            foreach (var candidate in preferredImagePool)
            {
                if (TryNormalizeImageCandidate(candidate, out var normalized) && seen.Add(normalized))
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
            catch (IOException)
            {
                // Fall back to just the explicitly requested image.
            }
            catch (UnauthorizedAccessException)
            {
                // Fall back to just the explicitly requested image.
            }
        }

        if (seen.Add(imagePath))
            results.Add(imagePath);

        return results;
    }

    private static bool TryNormalizeImageCandidate(string? candidatePath, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(candidatePath))
            return false;

        string candidate = candidatePath.Trim();
        try
        {
            candidate = Path.GetFullPath(candidate);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }

        if (!File.Exists(candidate) || !FileSystemService.IsImageFile(candidate))
            return false;

        normalizedPath = candidate;
        return true;
    }

    private static bool TryNormalizeImageCandidate(string? candidatePath, string requiredDirectory, out string normalizedPath)
    {
        if (!TryNormalizeImageCandidate(candidatePath, out normalizedPath))
            return false;

        string? candidateDirectory = Path.GetDirectoryName(normalizedPath);
        if (string.IsNullOrWhiteSpace(candidateDirectory))
            return false;

        if (!string.Equals(
                candidateDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                requiredDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = string.Empty;
            return false;
        }

        return true;
    }
}
