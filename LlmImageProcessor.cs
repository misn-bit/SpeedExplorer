using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace SpeedExplorer;

/// <summary>
/// Handles image preparation for LLM vision models.
/// Resizes large images to stay within pixel budget while maintaining aspect ratio.
/// </summary>
public static class LlmImageProcessor
{
    /// <summary>
    /// Resizes image for vision models to avoid 'payload too large' errors while maintaining visibility.
    /// Targeted at ~2.36M pixels (equivalent to 1536x1536) while maintaining aspect ratio.
    /// </summary>
    public static (byte[], LlmImageStats) PrepareImageForVision(string path)
    {
        var stats = new LlmImageStats { Path = path };
        try
        {
            using (var img = Image.FromFile(path))
            {
                stats.OrigW = img.Width;
                stats.OrigH = img.Height;

                const long maxPixels = 1536 * 1536;
                long currentPixels = (long)img.Width * img.Height;
                
                // Only resize if exceeds pixel budget
                if (currentPixels <= maxPixels)
                {
                    stats.NewW = img.Width;
                    stats.NewH = img.Height;

                    // Check if original is already a JPEG, otherwise re-encode to be sure it's optimized
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext == ".jpg" || ext == ".jpeg")
                    {
                        var bytes = File.ReadAllBytes(path);
                        stats.Bytes = bytes.Length;
                        return (bytes, stats);
                    }
                }

                // Calculate scaling ratio based on total area
                double ratio = Math.Sqrt((double)maxPixels / currentPixels);
                if (ratio > 1.0) ratio = 1.0; // Never upscale

                int newWidth = (int)Math.Round(img.Width * ratio);
                int newHeight = (int)Math.Round(img.Height * ratio);
                
                // Ensure we don't end up with 0 size due to rounding or extreme ratios
                if (newWidth < 1) newWidth = 1;
                if (newHeight < 1) newHeight = 1;

                stats.NewW = newWidth;
                stats.NewH = newHeight;

                using (var newImg = new Bitmap(newWidth, newHeight))
                using (var g = Graphics.FromImage(newImg))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;

                    g.DrawImage(img, 0, 0, newWidth, newHeight);

                    using (var ms = new MemoryStream())
                    {
                        // Save as high quality JPEG (OpenAI/LM Studio standard)
                        var encoderParameters = new EncoderParameters(1);
                        encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
                        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                        
                        newImg.Save(ms, codec, encoderParameters);
                        var bytes = ms.ToArray();
                        stats.Bytes = bytes.Length;
                        return (bytes, stats);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Image preparation failed for {path}: {ex.Message}");
            var bytes = File.ReadAllBytes(path);
            stats.Bytes = bytes.Length;
            return (bytes, stats);
        }
    }
}
