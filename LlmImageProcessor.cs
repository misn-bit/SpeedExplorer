using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using ImageSharpSize = SixLabors.ImageSharp.Size;

namespace SpeedExplorer;

/// <summary>
/// Handles image preparation for LLM vision models.
/// Always encodes output as JPEG to provide a consistent image payload.
/// </summary>
public static class LlmImageProcessor
{
    /// <summary>
    /// Resizes image for vision models to avoid payload issues while maintaining visibility.
    /// Targeted at ~2.36M pixels (1536x1536 equivalent).
    /// </summary>
    public static (byte[], LlmImageStats) PrepareImageForVision(string path, long maxPixels = 1536L * 1536L, int jpegQuality = 85)
    {
        var stats = new LlmImageStats { Path = path };
        if (maxPixels < 256L * 256L) maxPixels = 256L * 256L;
        if (jpegQuality < 40) jpegQuality = 40;
        if (jpegQuality > 95) jpegQuality = 95;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using Image<Rgba32> image = ImageSharpImage.Load<Rgba32>(stream);
            image.Mutate(static ctx => ctx.AutoOrient());

            stats.OrigW = image.Width;
            stats.OrigH = image.Height;

            long currentPixels = (long)image.Width * image.Height;
            if (currentPixels > maxPixels)
            {
                double ratio = Math.Sqrt((double)maxPixels / currentPixels);
                if (ratio > 1.0) ratio = 1.0;

                int newWidth = Math.Max(1, (int)Math.Round(image.Width * ratio));
                int newHeight = Math.Max(1, (int)Math.Round(image.Height * ratio));

                image.Mutate(ctx => ctx.Resize(new ResizeOptions
                {
                    Size = new ImageSharpSize(newWidth, newHeight),
                    Mode = ResizeMode.Max,
                    Sampler = KnownResamplers.Lanczos3
                }));
            }

            stats.NewW = image.Width;
            stats.NewH = image.Height;

            using var ms = new MemoryStream();
            image.Save(ms, new JpegEncoder { Quality = jpegQuality });
            var bytes = ms.ToArray();
            stats.Bytes = bytes.Length;
            return (bytes, stats);
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Image preparation failed for {path}: {ex.Message}");
            throw new InvalidOperationException($"Image preparation failed for '{path}': {ex.Message}", ex);
        }
    }
}
