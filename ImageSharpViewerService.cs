using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using ImageSharpSize = SixLabors.ImageSharp.Size;

namespace SpeedExplorer;

internal static class ImageSharpViewerService
{
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

    private static FileStream OpenSharedRead(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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
