using DictateFlow.Core.Text;

namespace DictateFlow.Tests;

/// <summary>Tests for <see cref="WordCounter"/>, the whitespace-delimited dictation word count.</summary>
public sealed class WordCounterTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("\t\n ", 0)]
    [InlineData("hello", 1)]
    [InlineData("hello world", 2)]
    [InlineData("one two three four five", 5)]
    public void CountWords_ReturnsExpectedCount(string? text, int expected)
        => Assert.Equal(expected, WordCounter.CountWords(text));

    [Fact]
    public void CountWords_CollapsesRunsOfWhitespace()
        // Multiple spaces and tabs between words count as single boundaries, and
        // leading/trailing whitespace is ignored.
        => Assert.Equal(3, WordCounter.CountWords("  the   quick\tbrown  "));

    [Fact]
    public void CountWords_NewlinesSeparateWords()
        => Assert.Equal(3, WordCounter.CountWords("first\nsecond\r\nthird"));

    [Fact]
    public void CountWords_PunctuationStaysWithItsWord()
        => Assert.Equal(4, WordCounter.CountWords("Hello, world! How's it")); // "Hello," "world!" "How's" "it"
}
