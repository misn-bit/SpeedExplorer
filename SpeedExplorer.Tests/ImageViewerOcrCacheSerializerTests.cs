using System.Drawing;

namespace SpeedExplorer.Tests;

public sealed class ImageViewerOcrCacheSerializerTests
{
    private const string Separator = "###CACHE###";

    [Fact]
    public void SerializeAndTryRead_RoundTripsOcrTranslationAndOverlayMetadata()
    {
        string path = CreateTemporaryFilePath();
        try
        {
            var envelope = new OcrCacheEnvelope
            {
                SourcePath = @"C:\images\sample.png",
                SourceLength = 123,
                SourceLastWriteUtcTicks = 456,
                SavedUtcTicks = 789,
                ModelId = "vision-model",
                Result = new LlmImageTextResult
                {
                    FullText = "Hello world",
                    DetectedLanguage = "English",
                    Blocks = new List<LlmImageTextBlock>
                    {
                        new() { Text = "Hello world", X = 0.1f, Y = 0.2f, W = 0.3f, H = 0.4f }
                    }
                },
                TranslationTargetLanguage = "German",
                TranslationFullText = "Hallo Welt",
                TranslationLines = new List<string> { "Hallo Welt" },
                OverlayOverrides = new List<OcrOverlayBlockOverride>
                {
                    new() { SourceIndex = 0, Text = "Edited source", TextColorArgb = Color.Red.ToArgb() }
                }
            };

            string serialized = ImageViewerOcrCacheSerializer.Serialize(envelope, Separator);
            File.WriteAllText(path, serialized);

            OcrCacheEnvelope? loaded = ImageViewerOcrCacheSerializer.TryRead(path, Separator);

            Assert.NotNull(loaded);
            Assert.Contains("Hello world", serialized);
            Assert.Contains("Hallo Welt", serialized);
            Assert.Contains(Separator, serialized);
            Assert.Equal(envelope.SourceLength, loaded!.SourceLength);
            Assert.Equal(envelope.ModelId, loaded.ModelId);
            Assert.Equal("Hello world", loaded.Result!.FullText);
            Assert.Equal("German", loaded.TranslationTargetLanguage);
            Assert.Equal("Hallo Welt", loaded.TranslationLines[0]);
            Assert.Equal("Edited source", loaded.OverlayOverrides[0].Text);
            Assert.Equal(Color.Red.ToArgb(), loaded.OverlayOverrides[0].TextColorArgb);
        }
        finally
        {
            DeleteTemporaryFile(path);
        }
    }

    [Fact]
    public void Serialize_UsesBlockTextAndTranslationLinesWhenFullTextIsMissing()
    {
        var envelope = new OcrCacheEnvelope
        {
            Result = new LlmImageTextResult
            {
                Blocks = new List<LlmImageTextBlock>
                {
                    new() { Text = "First" },
                    new() { Text = "Second" }
                }
            },
            TranslationLines = new List<string> { "Erste", "", "Zweite" }
        };

        string serialized = ImageViewerOcrCacheSerializer.Serialize(envelope, Separator);

        Assert.Contains("First", serialized);
        Assert.Contains("Second", serialized);
        Assert.Contains("Erste", serialized);
        Assert.Contains("Zweite", serialized);
        Assert.Contains(Separator, serialized);
    }

    [Fact]
    public void TryRead_ReturnsNullForMissingOrCorruptCache()
    {
        string missingPath = CreateTemporaryFilePath();
        string corruptPath = CreateTemporaryFilePath();
        try
        {
            File.WriteAllText(corruptPath, "not a cache");

            Assert.Null(ImageViewerOcrCacheSerializer.TryRead(missingPath, Separator));
            Assert.Null(ImageViewerOcrCacheSerializer.TryRead(corruptPath, Separator));
        }
        finally
        {
            DeleteTemporaryFile(missingPath);
            DeleteTemporaryFile(corruptPath);
        }
    }

    private static string CreateTemporaryFilePath()
        => Path.Combine(Path.GetTempPath(), $"SpeedExplorer.Tests-{Guid.NewGuid():N}.json");

    private static void DeleteTemporaryFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
