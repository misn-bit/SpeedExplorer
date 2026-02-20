using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class StartupIconController
    {
        public StartupIconController(MainForm owner)
        {
            _ = owner;
        }

        public string NormalizeStartupPath(string? input, out List<string>? selectPaths, bool inferRecentSelectionForDirectory = false)
        {
            selectPaths = null;
            if (string.IsNullOrWhiteSpace(input))
                return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            string raw = input.Trim();
            string lower = raw.ToLowerInvariant();
            if (lower.StartsWith("/select") || lower.StartsWith("-select"))
            {
                int comma = raw.IndexOf(',');
                if (comma >= 0 && comma + 1 < raw.Length)
                    raw = raw[(comma + 1)..];
            }
            else if (lower.StartsWith("/e,") || lower.StartsWith("/root,") || lower.StartsWith("/root"))
            {
                int comma = raw.IndexOf(',');
                if (comma >= 0 && comma + 1 < raw.Length)
                    raw = raw[(comma + 1)..];
            }

            raw = raw.Trim().Trim('"');

            if (raw.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var uri = new Uri(raw, UriKind.Absolute);
                    if (uri.IsFile)
                        raw = uri.LocalPath;
                }
                catch { }
            }

            if (!File.Exists(raw) && !Directory.Exists(raw))
            {
                int switchSplit = raw.IndexOf(" /", StringComparison.Ordinal);
                if (switchSplit < 0)
                    switchSplit = raw.IndexOf(" -", StringComparison.Ordinal);
                if (switchSplit > 0)
                {
                    var shortened = raw[..switchSplit].Trim().Trim('"');
                    if (!string.IsNullOrWhiteSpace(shortened))
                        raw = shortened;
                }
            }

            if (Directory.Exists(raw))
            {
                if (inferRecentSelectionForDirectory && TryInferRecentSelectionForDirectory(raw, out var inferredPath))
                    selectPaths = new List<string> { inferredPath };
                return raw;
            }

            if (File.Exists(raw))
            {
                selectPaths = new List<string> { raw };
                var parent = Path.GetDirectoryName(raw);
                return string.IsNullOrEmpty(parent) ? raw : parent;
            }

            // If raw includes arguments (e.g., "C:\dir\file.txt" -a), trim to file path
            int argSplit = raw.IndexOf("\" ", StringComparison.Ordinal);
            if (argSplit > 0)
            {
                var candidate = raw[..argSplit].Trim().Trim('"');
                if (File.Exists(candidate))
                {
                    selectPaths = new List<string> { candidate };
                    var parent = Path.GetDirectoryName(candidate);
                    return string.IsNullOrEmpty(parent) ? candidate : parent;
                }
                raw = candidate;
            }

            var inferredParent = Path.GetDirectoryName(raw);
            if (!string.IsNullOrEmpty(inferredParent) && Directory.Exists(inferredParent))
            {
                selectPaths = new List<string> { raw };
                return inferredParent;
            }

            return raw;
        }

        private static bool TryInferRecentSelectionForDirectory(string directoryPath, out string inferredPath)
        {
            inferredPath = string.Empty;
            if (string.IsNullOrWhiteSpace(directoryPath))
                return false;
            if (!IsLikelyDownloadsFolder(directoryPath))
                return false;

            // Keep this bounded to avoid UI stalls on huge folders.
            const int scanBudgetMs = 120;
            const int maxFilesScanned = 1800;
            DateTime cutoffUtc = DateTime.UtcNow.AddSeconds(-10);
            string bestPath = string.Empty;
            DateTime bestTimeUtc = DateTime.MinValue;
            int scanned = 0;
            var sw = Stopwatch.StartNew();

            try
            {
                foreach (var file in Directory.EnumerateFiles(directoryPath))
                {
                    if (sw.ElapsedMilliseconds > scanBudgetMs)
                        break;
                    scanned++;
                    if (scanned > maxFilesScanned)
                        break;

                    try
                    {
                        DateTime createdUtc = File.GetCreationTimeUtc(file);
                        DateTime modifiedUtc = File.GetLastWriteTimeUtc(file);
                        DateTime candidateUtc = modifiedUtc > createdUtc ? modifiedUtc : createdUtc;
                        if (candidateUtc < cutoffUtc)
                            continue;
                        if (candidateUtc <= bestTimeUtc)
                            continue;
                        bestTimeUtc = candidateUtc;
                        bestPath = file;
                    }
                    catch { }
                }
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(bestPath))
                return false;

            inferredPath = bestPath;
            return true;
        }

        private static bool IsLikelyDownloadsFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            string defaultDownloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!string.IsNullOrWhiteSpace(defaultDownloads) &&
                string.Equals(normalized, defaultDownloads, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return normalized.EndsWith($"{Path.DirectorySeparatorChar}Downloads", StringComparison.OrdinalIgnoreCase) ||
                   normalized.EndsWith($"{Path.AltDirectorySeparatorChar}Downloads", StringComparison.OrdinalIgnoreCase);
        }

        public ImageList CreateIconList(int size)
        {
            var list = new ImageList { ImageSize = new Size(size, size), ColorDepth = ColorDepth.Depth32Bit };

            // Add default internal icons (fallback).
            list.Images.Add("folder", CreateFolderIcon(size));
            list.Images.Add("file", CreateFileIcon(size));
            list.Images.Add("image", CreateImageIcon(size));
            list.Images.Add("drive", CreateDriveIcon(size));
            list.Images.Add("usb", CreateUsbIcon(size));
            list.Images.Add("computer", CreateComputerIcon(size));

            return list;
        }

        public Bitmap CreateFolderIcon(int size)
        {
            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Color.FromArgb(160, 160, 160));
            g.FillRectangle(brush, 0, size / 4, size, size * 3 / 4);
            g.FillRectangle(brush, 0, size / 5, size / 2, size / 5);
            return bmp;
        }

        public Bitmap CreateFileIcon(int size)
        {
            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Color.FromArgb(180, 180, 180));
            g.FillRectangle(brush, size / 6, 0, size * 2 / 3, size);
            return bmp;
        }

        public Bitmap CreateImageIcon(int size)
        {
            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Color.FromArgb(180, 180, 180));
            g.FillRectangle(brush, 1, 1, size - 2, size - 2);
            using var darkBrush = new SolidBrush(Color.FromArgb(120, 120, 120));
            g.FillPolygon(darkBrush, new[]
            {
                new Point(2, size - 2),
                new Point(size / 2, size / 2),
                new Point(size - 2, size - 2)
            });
            return bmp;
        }

        public Bitmap CreateDriveIcon(int size)
        {
            try
            {
                var sys = IconHelper.GetIconSized("C:\\", false, size, forceUnique: true, grayscale: false);
                if (sys != null)
                    return sys;
            }
            catch { }

            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Color.FromArgb(120, 120, 120));
            g.FillRectangle(brush, 1, size / 4, size - 2, size / 2);
            return bmp;
        }

        public Bitmap CreateComputerIcon(int size)
        {
            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var color = Color.FromArgb(0, 120, 212);
            var rect = new Rectangle(size / 4, size / 4, size / 2, size / 3);
            using (var p = new Pen(color, 2))
                g.DrawRectangle(p, rect);
            using (var p = new Pen(color, 2))
                g.DrawLine(p, size / 2, size * 2 / 3, size / 2, size * 3 / 4);
            using (var p = new Pen(color, 2))
                g.DrawLine(p, size / 3, size * 3 / 4, size * 2 / 3, size * 3 / 4);
            return bmp;
        }

        public Bitmap CreateUsbIcon(int size)
        {
            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var color = Color.FromArgb(0, 120, 212);
            var body = new Rectangle(size / 3, size / 4, size / 3, size / 2);
            var head = new Rectangle(size * 4 / 10, size * 3 / 4, size / 5, size / 8);
            using (var b = new SolidBrush(color))
            {
                g.FillRectangle(b, body);
                g.FillRectangle(b, head);
            }
            using (var b = new SolidBrush(Color.Black))
            {
                g.FillRectangle(b, size * 42 / 100, size * 78 / 100, size / 15, size / 20);
                g.FillRectangle(b, size * 52 / 100, size * 78 / 100, size / 15, size / 20);
            }
            return bmp;
        }
    }
}
