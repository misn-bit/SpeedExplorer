namespace SpeedExplorer.Tests;

public sealed class ImageViewerSequenceBuilderTests
{
    [Fact]
    public void BuildForPath_UsesPreferredImagesAndRemovesDuplicates()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string current = CreateImageFile(directory, "current.png");
            string previous = CreateImageFile(directory, "previous.jpg");
            string text = Path.Combine(directory, "notes.txt");
            File.WriteAllText(text, "not an image");

            List<string> sequence = ImageViewerSequenceBuilder.BuildForPath(
                current,
                new[] { previous, previous, text });

            Assert.Equal(new[] { previous, current }, sequence);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public void BuildForPath_EnumeratesAndSortsFolderWhenNoPoolIsProvided()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string current = CreateImageFile(directory, "middle.png");
            string first = CreateImageFile(directory, "first.jpg");
            CreateImageFile(directory, "last.gif");
            File.WriteAllText(Path.Combine(directory, "notes.txt"), "not an image");

            List<string> sequence = ImageViewerSequenceBuilder.BuildForPath(current, preferredImagePool: null);

            Assert.Equal(
                new[]
                {
                    first,
                    Path.Combine(directory, "last.gif"),
                    current
                },
                sequence);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"SpeedExplorer.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string CreateImageFile(string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, Array.Empty<byte>());
        return path;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
