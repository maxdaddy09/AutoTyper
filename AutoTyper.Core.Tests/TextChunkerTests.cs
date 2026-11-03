using System.Diagnostics;

namespace AutoTyper.Tests;

public class TextChunkerTests
{
    [Fact]
    public void Letters_SplitsIntoSingleCharacters()
    {
        Assert.Equal(new[] { "a", "b", "c" }, TextChunker.Chunk("abc", TypingMode.Letters));
    }

    [Fact]
    public void Letters_KeepsEmojiAndCombiningMarksWhole()
    {
        Assert.Equal(
            new[] { "a", "😀", "é" },
            TextChunker.Chunk("a😀é", TypingMode.Letters));
    }

    [Theory]
    [InlineData("a\r\nb")]
    [InlineData("a\rb")]
    public void Letters_CrLfAndLoneCr_BecomeOneNewlineUnit(string text)
    {
        Assert.Equal(new[] { "a", "\n", "b" }, TextChunker.Chunk(text, TypingMode.Letters));
    }

    [Fact]
    public void Words_CrLf_BecomesOneNewline()
    {
        Assert.Equal(new[] { "a\n", "b" }, TextChunker.Chunk("a\r\nb", TypingMode.Words));
    }

    [Fact]
    public void Words_UnitIsWordPlusFollowingWhitespace()
    {
        Assert.Equal(
            new[] { "hello  ", "big\t", "world\n" },
            TextChunker.Chunk("hello  big\tworld\n", TypingMode.Words));
    }

    [Fact]
    public void Words_LeadingWhitespaceIsOwnUnit()
    {
        Assert.Equal(
            new[] { "  ", "hi ", "there" },
            TextChunker.Chunk("  hi there", TypingMode.Words));
    }

    [Fact]
    public void Words_WhitespaceOnlyIsOneUnit()
    {
        Assert.Equal(new[] { " \n " }, TextChunker.Chunk(" \n ", TypingMode.Words));
    }

    [Fact]
    public void Words_TreatsNonBreakingSpaceAsWhitespace()
    {
        Assert.Equal(
            new[] { "a ", "b" },
            TextChunker.Chunk("a b", TypingMode.Words));
    }

    [Theory]
    [InlineData(TypingMode.Letters)]
    [InlineData(TypingMode.Words)]
    public void EmptyText_ReturnsEmptyList(TypingMode mode)
    {
        Assert.Empty(TextChunker.Chunk("", mode));
    }

    [Theory]
    [InlineData(TypingMode.Letters)]
    [InlineData(TypingMode.Words)]
    public void Units_ConcatenateToNormalizedText(TypingMode mode)
    {
        const string text = "Héllo wörld 😀\r\nsecond\tline\r\n\r\n  indented\rend ";
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");

        Assert.Equal(normalized, string.Concat(TextChunker.Chunk(text, mode)));
    }

    [Theory]
    [InlineData(TypingMode.Letters)]
    [InlineData(TypingMode.Words)]
    public void LargeInput_ChunksUnderTwoSeconds(TypingMode mode)
    {
        var text = string.Concat(Enumerable.Repeat("lorem ipsum dolor sit\r\n", 25_000));
        Assert.True(text.Length >= 500_000);

        var timer = Stopwatch.StartNew();
        var units = TextChunker.Chunk(text, mode);
        timer.Stop();

        Assert.NotEmpty(units);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2), $"took {timer.Elapsed}");
    }
}
