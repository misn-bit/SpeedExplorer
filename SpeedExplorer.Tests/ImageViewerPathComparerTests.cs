using System.Text;

namespace SpeedExplorer.Tests;

public sealed class ImageViewerPathComparerTests
{
    [Fact]
    public void Compare_ByNameUsesRequestedDirection()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string alpha = Path.Combine(directory, "alpha.png");
            string beta = Path.Combine(directory, "beta.png");
            File.WriteAllText(alpha, "a", Encoding.UTF8);
            File.WriteAllText(beta, "b", Encoding.UTF8);

            var ascending = new ImageViewerSortOptions(SortColumn.Name, SortDirection.Ascending, false);
            var descending = new ImageViewerSortOptions(SortColumn.Name, SortDirection.Descending, false);

            Assert.True(ImageViewerPathComparer.Compare(alpha, beta, ascending) < 0);
            Assert.True(ImageViewerPathComparer.Compare(alpha, beta, descending) > 0);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public void Compare_SamePathReturnsZero()
    {
        string path = Path.Combine(CreateTemporaryDirectory(), "same.png");
        string directory = Path.GetDirectoryName(path)!;
        try
        {
            File.WriteAllText(path, "content", Encoding.UTF8);
            var options = new ImageViewerSortOptions(SortColumn.Name, SortDirection.Ascending, false);

            Assert.Equal(0, ImageViewerPathComparer.Compare(path, path, options));
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

    private static void DeleteTemporaryDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Test cleanup should not hide the assertion result.
        }
        catch (UnauthorizedAccessException)
        {
            // Test cleanup should not hide the assertion result.
        }
    }
}
