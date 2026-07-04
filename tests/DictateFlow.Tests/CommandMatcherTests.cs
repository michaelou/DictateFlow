using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Commands;
using Microsoft.Extensions.Logging;
using Moq;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="CommandMatcher"/>: exact and prefix matching, argument extraction,
/// the deterministic tie-breaks and the never-match cases.
/// </summary>
public sealed class CommandMatcherTests
{
    private readonly CommandMatcher _matcher = new(Mock.Of<ILogger<CommandMatcher>>());

    private static CommandDefinition Command(string name, params string[] phrases)
        => new() { Name = name, Phrases = [.. phrases], ActionType = "Mock" };

    [Theory]
    [InlineData("open notepad")]
    [InlineData("Open Notepad")]
    [InlineData("open notepad.")]
    [InlineData("Open, Notepad!")]
    public void Match_ExactPhraseVariants_AllHit(string commandText)
    {
        var match = _matcher.Match(commandText, [Command("Open Notepad", "open notepad")]);

        Assert.NotNull(match);
        Assert.Equal("Open Notepad", match.Definition.Name);
        Assert.Equal("", match.Argument);
    }

    [Fact]
    public void Match_PrefixPhrase_RemainderBecomesTheArgumentVerbatim()
    {
        var match = _matcher.Match(
            "remind me in 10 minutes to call Marko, please!",
            [Command("Reminder", "remind me")]);

        Assert.NotNull(match);
        Assert.Equal("Reminder", match.Definition.Name);
        Assert.Equal("in 10 minutes to call Marko, please!", match.Argument);
    }

    [Fact]
    public void Match_ExactBeatsPrefix()
    {
        var exact = Command("Open Notepad Plus", "open notepad plus plus");
        var prefix = Command("Open Notepad", "open notepad");

        var match = _matcher.Match("open notepad plus plus", [prefix, exact]);

        Assert.Equal("Open Notepad Plus", match?.Definition.Name);
    }

    [Fact]
    public void Match_LongestPrefixWins()
    {
        var shortPhrase = Command("Note", "note");
        var longPhrase = Command("Note Tomorrow", "note tomorrow");

        var match = _matcher.Match("note tomorrow buy milk", [shortPhrase, longPhrase]);

        Assert.Equal("Note Tomorrow", match?.Definition.Name);
        Assert.Equal("buy milk", match?.Argument);
    }

    [Fact]
    public void Match_EqualCandidates_FirstInSourceOrderWins()
    {
        var first = Command("First", "open notepad");
        var second = Command("Second", "open notepad");

        var match = _matcher.Match("open notepad", [first, second]);

        Assert.Equal("First", match?.Definition.Name);
    }

    [Fact]
    public void Match_DisabledCommand_NeverMatches()
    {
        var disabled = Command("Open Notepad", "open notepad");
        disabled.Enabled = false;

        Assert.Null(_matcher.Match("open notepad", [disabled]));
    }

    [Fact]
    public void Match_NoPhraseMatches_ReturnsNull()
    {
        Assert.Null(_matcher.Match("delete all my files", [Command("Open Notepad", "open notepad")]));
    }

    [Fact]
    public void Match_PhraseMatchingMidUtteranceOnly_DoesNotHit()
    {
        // Phrases anchor at the start of the command text; "please open notepad" is not "open notepad".
        Assert.Null(_matcher.Match("please open notepad", [Command("Open Notepad", "open notepad")]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public void Match_EmptyCommandText_ReturnsNull(string commandText)
    {
        Assert.Null(_matcher.Match(commandText, [Command("Open Notepad", "open notepad")]));
    }

    [Fact]
    public void Match_EmptyPhrase_NeverMatchesAnything()
    {
        Assert.Null(_matcher.Match("open notepad", [Command("Broken", "", "   ")]));
    }
}
