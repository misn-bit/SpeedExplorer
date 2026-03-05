using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using ImageSharpSize = SixLabors.ImageSharp.Size;

namespace SpeedExplorer;

internal sealed class AnimatedImageSequence : IDisposable
{
    private readonly List<Bitmap> _frames;
    private readonly List<int> _frameDelaysMs;

    public AnimatedImageSequence(List<Bitmap> frames, List<int> frameDelaysMs)
    {
        if (frames == null) throw new ArgumentNullException(nameof(frames));
        if (frameDelaysMs == null) throw new ArgumentNullException(nameof(frameDelaysMs));
        if (frames.Count == 0) throw new ArgumentException("At least one frame is required.", nameof(frames));
        if (frames.Count != frameDelaysMs.Count) throw new ArgumentException("Frame count and delay count must match.");

        _frames = frames;
        _frameDelaysMs = frameDelaysMs;
    }

    public int FrameCount => _frames.Count;
    public bool IsAnimated => FrameCount > 1;
    public int Width => _frames[0].Width;
    public int Height => _frames[0].Height;

    public Bitmap GetFrame(int index) => _frames[index];
    public int GetFrameDelayMs(int index) => _frameDelaysMs[index];

    public void Dispose()
    {
        foreach (Bitmap frame in _frames)
        {
            frame.Dispose();
        }
        _frames.Clear();
        _frameDelaysMs.Clear();
    }
}

internal static class ImageSharpViewerService
{
    private const int DefaultFrameDelayMs = 100;
    private const int MinFrameDelayMs = 16;
    private const int MaxFrameDelayMs = 10_000;

    public static Bitmap LoadBitmap(string path)
    {
        using var stream = OpenSharedRead(path);
        using Image<Bgra32> image = ImageSharpImage.Load<Bgra32>(stream);
        image.Mutate(static ctx => ctx.AutoOrient());
        return ToBitmap(image);
    }

    public static Bitmap LoadBitmap(string path, int maxWidth, int maxHeight)
    {
        if (maxWidth <= 0) throw new ArgumentOutOfRangeException(nameof(maxWidth));
        if (maxHeight <= 0) throw new ArgumentOutOfRangeException(nameof(maxHeight));

        using var stream = OpenSharedRead(path);
        using Image<Bgra32> image = ImageSharpImage.Load<Bgra32>(stream);
        image.Mutate(static ctx => ctx.AutoOrient());

        if (image.Width > maxWidth || image.Height > maxHeight)
        {
            image.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new ImageSharpSize(maxWidth, maxHeight),
                Mode = ResizeMode.Max,
                Sampler = KnownResamplers.Lanczos3,
            }));
        }

        return ToBitmap(image);
    }

    public static AnimatedImageSequence LoadAnimation(string path)
    {
        return LoadAnimationInternal(path, null, null);
    }

    public static AnimatedImageSequence LoadAnimation(string path, int maxWidth, int maxHeight)
    {
        if (maxWidth <= 0) throw new ArgumentOutOfRangeException(nameof(maxWidth));
        if (maxHeight <= 0) throw new ArgumentOutOfRangeException(nameof(maxHeight));

        return LoadAnimationInternal(path, maxWidth, maxHeight);
    }

    private static FileStream OpenSharedRead(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    private static AnimatedImageSequence LoadAnimationInternal(string path, int? maxWidth, int? maxHeight)
    {
        using var stream = OpenSharedRead(path);
        using Image<Bgra32> image = ImageSharpImage.Load<Bgra32>(stream);
        image.Mutate(static ctx => ctx.AutoOrient());

        if (maxWidth.HasValue && maxHeight.HasValue && (image.Width > maxWidth.Value || image.Height > maxHeight.Value))
        {
            image.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new ImageSharpSize(maxWidth.Value, maxHeight.Value),
                Mode = ResizeMode.Max,
                Sampler = KnownResamplers.Lanczos3,
            }));
        }

        var frames = new List<Bitmap>(image.Frames.Count);
        var delays = new List<int>(image.Frames.Count);

        try
        {
            for (int i = 0; i < image.Frames.Count; i++)
            {
                using Image<Bgra32> frame = image.Frames.CloneFrame(i);
                frames.Add(ToBitmap(frame));
                delays.Add(GetFrameDelayMilliseconds(frame.Frames.RootFrame.Metadata));
            }

            return new AnimatedImageSequence(frames, delays);
        }
        catch
        {
            foreach (Bitmap frame in frames)
            {
                frame.Dispose();
            }
            throw;
        }
    }

    private static int GetFrameDelayMilliseconds(ImageFrameMetadata metadata)
    {
        int delayMs = 0;

        if (metadata.TryGetGifMetadata(out GifFrameMetadata? gif))
        {
            delayMs = gif.FrameDelay * 10;
        }
        else if (metadata.TryGetWebpFrameMetadata(out WebpFrameMetadata? webp))
        {
            delayMs = webp.FrameDelay > int.MaxValue ? int.MaxValue : (int)webp.FrameDelay;
        }
        else if (metadata.TryGetPngMetadata(out PngFrameMetadata? png))
        {
            double seconds = png.FrameDelay.ToDouble();
            if (!double.IsNaN(seconds) && !double.IsInfinity(seconds))
            {
                delayMs = (int)Math.Round(seconds * 1000);
            }
        }

        if (delayMs <= 0)
        {
            delayMs = DefaultFrameDelayMs;
        }

        return Math.Clamp(delayMs, MinFrameDelayMs, MaxFrameDelayMs);
    }

    private static Bitmap ToBitmap(Image<Bgra32> source)
    {
        var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        var rect = new System.Drawing.Rectangle(0, 0, source.Width, source.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            IntPtr firstRow = data.Stride < 0
                ? IntPtr.Add(data.Scan0, data.Stride * (source.Height - 1))
                : data.Scan0;
            int destinationStride = Math.Abs(data.Stride);
            int rowLength = source.Width * 4;
            byte[] rowBuffer = new byte[rowLength];

            source.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Bgra32> row = accessor.GetRowSpan(y);
                    MemoryMarshal.AsBytes(row).CopyTo(rowBuffer);
                    IntPtr destination = IntPtr.Add(firstRow, y * destinationStride);
                    Marshal.Copy(rowBuffer, 0, destination, rowLength);
                }
            });
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }
}
