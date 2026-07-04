using DictateFlow.Core.Services.Commands;

namespace DictateFlow.Tests;

/// <summary>Tests for <see cref="CommandArgumentPlaceholder"/> and <see cref="ProcessArgumentEscaper"/>.</summary>
public sealed class CommandArgumentPlaceholderTests
{
    [Theory]
    [InlineData("{{Argument}}")]
    [InlineData("q={{argument}}")]
    [InlineData("q={{ARGUMENT}}")]
    [InlineData("q={{ argument }}")]
    public void Contains_RecognizesPlaceholderCaseAndWhitespaceInsensitively(string template)
        => Assert.True(CommandArgumentPlaceholder.Contains(template));

    [Theory]
    [InlineData("no placeholder here")]
    [InlineData("{argument}")]
    [InlineData("{{arg}}")]
    [InlineData("")]
    [InlineData(null)]
    public void Contains_ReturnsFalseWithoutPlaceholder(string? template)
        => Assert.False(CommandArgumentPlaceholder.Contains(template));

    [Fact]
    public void Substitute_ReplacesEveryOccurrence()
        => Assert.Equal(
            "a=cats&b=cats",
            CommandArgumentPlaceholder.Substitute("a={{Argument}}&b={{argument}}", "cats"));

    [Fact]
    public void Substitute_InsertsReplacementVerbatim_NoRegexDollarSemantics()
        => Assert.Equal("q=$1 raw", CommandArgumentPlaceholder.Substitute("q={{Argument}}", "$1 raw"));

    [Theory]
    [InlineData("meeting notes", "meeting notes")]
    [InlineData("a\"b", "a\\\"b")]
    [InlineData("ends\\", "ends\\\\")]
    [InlineData("a\\\"b", "a\\\\\\\"b")]
    public void EscapeInsideQuotes_KeepsTheValueOneArgument(string raw, string expected)
        => Assert.Equal(expected, ProcessArgumentEscaper.EscapeInsideQuotes(raw));
}
