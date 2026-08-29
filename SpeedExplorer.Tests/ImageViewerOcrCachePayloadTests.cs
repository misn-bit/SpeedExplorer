namespace SpeedExplorer.Tests;

public sealed class ImageViewerOcrCachePayloadTests
{
    private const string Separator = "###CACHE###";

    [Fact]
    public void ExtractJsonPayload_ReadsPayloadAfterSeparator()
    {
        const string json = "{\"value\":1}";
        string raw = "Readable OCR text\r\n" + Separator + "\r\n  " + json;

        string result = ImageViewerOcrCachePayload.ExtractJsonPayload(raw, Separator);

        Assert.Equal(json, result);
    }

    [Fact]
    public void ExtractJsonPayload_FallsBackToFirstJsonObject()
    {
        const string json = "{\"value\":2}";

        string result = ImageViewerOcrCachePayload.ExtractJsonPayload("legacy header\n" + json, Separator);

        Assert.Equal(json, result);
    }

    [Fact]
    public void ExtractJsonPayload_LeavesJsonWithoutHeaderUntouched()
    {
        const string json = "{\"value\":3}";

        string result = ImageViewerOcrCachePayload.ExtractJsonPayload(json, Separator);

        Assert.Equal(json, result);
    }

    [Fact]
    public void ExtractJsonPayload_PreservesEmptyInput()
    {
        Assert.Equal("", ImageViewerOcrCachePayload.ExtractJsonPayload("", Separator));
        Assert.Equal("   ", ImageViewerOcrCachePayload.ExtractJsonPayload("   ", Separator));
    }
}
