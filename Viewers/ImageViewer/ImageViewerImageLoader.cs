using System.Drawing;

namespace SpeedExplorer;

internal sealed class ImageViewerImageLoadResult
{
    public Bitmap? Bitmap { get; }
    public AnimatedImageSequence? Animation { get; }

    public bool IsAnimated => Animation != null;

    private ImageViewerImageLoadResult(Bitmap bitmap)
    {
        Bitmap = bitmap;
    }

    private ImageViewerImageLoadResult(AnimatedImageSequence animation)
    {
        Animation = animation;
    }

    public static ImageViewerImageLoadResult FromBitmap(Bitmap bitmap)
        => new(bitmap);

    public static ImageViewerImageLoadResult FromAnimation(AnimatedImageSequence animation)
        => new(animation);
}

internal static class ImageViewerImageLoader
{
    public static ImageViewerImageLoadResult Load(string path)
    {
        if (ImageSharpViewerService.IsAnimatedImage(path))
            return ImageViewerImageLoadResult.FromAnimation(ImageSharpViewerService.LoadAnimation(path));

        return ImageViewerImageLoadResult.FromBitmap(ImageSharpViewerService.LoadBitmap(path));
    }
}
