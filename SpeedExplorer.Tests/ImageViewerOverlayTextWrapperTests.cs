namespace SpeedExplorer.Tests;

public sealed class ImageViewerOverlayTextWrapperTests
{
    [Fact]
    public void Wrap_KeepsQuotedWordsAndTheirSeparatingSpaceTogether()
    {
        List<string> lines = Wrap("before 'quoted' after", 9);

        Assert.Equal(new[] { "before", "'quoted'", "after" }, lines);
    }

    [Fact]
    public void Wrap_KeepsCurlyQuotesWithTheWordsTheyQualify()
    {
        List<string> lines = Wrap("before “quoted” after", 9);

        Assert.Equal(new[] { "before", "“quoted”", "after" }, lines);
    }

    [Fact]
    public void Wrap_PreservesSpaceBeforeQuotedTextWhenItFits()
    {
        List<string> lines = Wrap("one 'two' three", 10);

        Assert.Equal(new[] { "one 'two'", "three" }, lines);
    }

    [Fact]
    public void Wrap_DoesNotSplitAQuotedWordAtItsQuoteMarks()
    {
        List<string> lines = Wrap("word 'citation'", 6);

        Assert.Equal(new[] { "word", "'citation'" }, lines);
    }

    [Fact]
    public void Wrap_UsesWhitespaceAsTheNormalBreakOpportunity()
    {
        List<string> lines = Wrap("one two three", 7);

        Assert.Equal(new[] { "one two", "three" }, lines);
    }

    [Fact]
    public void Wrap_DoesNotAddAnArtificialHyphenToAnExistingHyphen()
    {
        List<string> lines = Wrap("prefix text-moretext", 8);

        Assert.Equal(new[] { "prefix", "text-", "moretext" }, lines);
        Assert.DoesNotContain(lines, line => line.Contains("--", StringComparison.Ordinal));
    }

    [Fact]
    public void Wrap_LeavesHyphenatedTextOnOneLineWhenItFits()
    {
        List<string> lines = Wrap("text-moretext", 13);

        Assert.Equal(new[] { "text-moretext" }, lines);
    }

    [Fact]
    public void Wrap_HyphenatesOnlyPlainLongWords()
    {
        List<string> lines = Wrap("encyclopedia", 5);

        Assert.Equal(new[] { "ency-", "clop-", "edia" }, lines);
    }

    [Fact]
    public void Wrap_KeepsCjkClosingPunctuationOnThePreviousLine()
    {
        List<string> lines = Wrap("你好，世界", 2);

        Assert.Equal(new[] { "你好，", "世界" }, lines);
    }

    private static List<string> Wrap(string text, float maxWidth)
        => ImageViewerOverlayTextWrapper.Wrap(text, maxWidth, line => line.Length);
}
