using System.Drawing;

namespace SpeedExplorer.Tests;

public sealed class ImageViewerOverlayGeometryTests
{
    [Fact]
    public void ClampNormalizedRect_ClampsCoordinatesAndDimensions()
    {
        RectangleF result = ImageViewerOverlayGeometry.ClampNormalizedRect(-0.2f, 0.3f, 0.9f, 0.9f);

        Assert.Equal(0f, result.X);
        Assert.Equal(0.3f, result.Y);
        Assert.Equal(0.9f, result.Width);
        Assert.Equal(0.7f, result.Height);
    }

    [Fact]
    public void RotateClockwise_AndUnrotate_ReturnOriginalRectangle()
    {
        var original = new RectangleF(0.1f, 0.2f, 0.3f, 0.4f);

        RectangleF rotated = ImageViewerOverlayGeometry.RotateClockwise(original);
        RectangleF restored = ImageViewerOverlayGeometry.Unrotate(rotated, 1);

        AssertRectangleApproximatelyEqual(original, restored);
    }

    [Fact]
    public void Unrotate_NormalizesNegativeAndLargeTurnCounts()
    {
        var original = new RectangleF(0.1f, 0.2f, 0.3f, 0.4f);

        RectangleF afterFourTurns = ImageViewerOverlayGeometry.Unrotate(original, 4);
        RectangleF afterNegativeTurn = ImageViewerOverlayGeometry.Unrotate(original, -1);

        AssertRectangleApproximatelyEqual(original, afterFourTurns);
        AssertRectangleApproximatelyEqual(
            ImageViewerOverlayGeometry.RotateClockwise(original),
            afterNegativeTurn);
    }

    [Fact]
    public void NormalizeToPixels_ConvertsNormalizedBoundsToImagePixels()
    {
        Rectangle result = ImageViewerOverlayGeometry.NormalizeToPixels(
            new RectangleF(0.1f, 0.2f, 0.3f, 0.4f),
            new Size(100, 50));

        Assert.Equal(new Rectangle(10, 10, 30, 20), result);
    }

    private static void AssertRectangleApproximatelyEqual(RectangleF expected, RectangleF actual)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X), 0f, 0.0001f);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0f, 0.0001f);
        Assert.InRange(Math.Abs(expected.Width - actual.Width), 0f, 0.0001f);
        Assert.InRange(Math.Abs(expected.Height - actual.Height), 0f, 0.0001f);
    }
}
