using System.Drawing;
using System.Drawing.Imaging;

namespace SpeedExplorer.Tests;

public sealed class ImageViewerImageLoaderTests
{
    [Fact]
    public void Load_StaticPngReturnsBitmapResult()
    {
        string path = Path.Combine(Path.GetTempPath(), $"SpeedExplorer.Tests-{Guid.NewGuid():N}.png");
        ImageViewerImageLoadResult? result = null;

        try
        {
            using (var source = new Bitmap(3, 2))
            {
                source.SetPixel(0, 0, Color.Red);
                source.Save(path, ImageFormat.Png);
            }

            result = ImageViewerImageLoader.Load(path);

            Assert.False(result.IsAnimated);
            Assert.NotNull(result.Bitmap);
            Assert.Equal(3, result.Bitmap!.Width);
            Assert.Equal(2, result.Bitmap.Height);
        }
        finally
        {
            result?.Bitmap?.Dispose();
            result?.Animation?.Dispose();
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
