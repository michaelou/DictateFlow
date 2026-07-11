using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Replacements;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DictateFlow.Tests;

/// <summary>Tests for <see cref="TextReplacementService"/> (issue #35).</summary>
public sealed class TextReplacementServiceTests
{
    private readonly AppSettings _settings = new();

    private TextReplacementService CreateService()
    {
        var settingsService = new Mock<ISettingsService>();
        settingsService.SetupGet(s => s.Current).Returns(_settings);
        return new TextReplacementService(settingsService.Object, NullLogger<TextReplacementService>.Instance);
    }

    [Fact]
    public void Apply_NoRules_ReturnsTextUnchanged()
    {
        var result = CreateService().Apply("Marco went to the store.");
        Assert.Equal("Marco went to the store.", result);
    }

    [Fact]
    public void Apply_SimpleRule_ReplacesEveryWholeWordOccurrence()
    {
        _settings.Replacements = [new ReplacementRule { From = "Marco", To = "Marko" }];

        var result = CreateService().Apply("Marco called Marco again.");

        Assert.Equal("Marko called Marko again.", result);
    }

    [Fact]
    public void Apply_CaseInsensitiveByDefault_MatchesRegardlessOfCasing()
    {
        _settings.Replacements = [new ReplacementRule { From = "marco", To = "Marko" }];

        var result = CreateService().Apply("MARCO and marco and Marco.");

        Assert.Equal("Marko and Marko and Marko.", result);
    }

    [Fact]
    public void Apply_CaseSensitive_OnlyMatchesExactCasing()
    {
        _settings.Replacements = [new ReplacementRule { From = "Marco", To = "Marko", CaseSensitive = true }];

        var result = CreateService().Apply("Marco and marco.");

        Assert.Equal("Marko and marco.", result);
    }

    [Fact]
    public void Apply_WholeWord_DoesNotMatchInsideOtherWords()
    {
        _settings.Replacements = [new ReplacementRule { From = "Marco", To = "Marko", WholeWord = true }];

        var result = CreateService().Apply("Marco drives a Marconi radio.");

        Assert.Equal("Marko drives a Marconi radio.", result);
    }

    [Fact]
    public void Apply_WholeWordOff_MatchesInsideWords()
    {
        _settings.Replacements = [new ReplacementRule { From = "co", To = "ko", WholeWord = false, CaseSensitive = true }];

        var result = CreateService().Apply("Marco");

        Assert.Equal("Marko", result);
    }

    [Fact]
    public void Apply_RulesAppliedInOrder()
    {
        _settings.Replacements =
        [
            new ReplacementRule { From = "A", To = "B", CaseSensitive = true },
            new ReplacementRule { From = "B", To = "C", CaseSensitive = true },
        ];

        // The first rule turns A into B, then the second turns every B (including the new one) into C.
        var result = CreateService().Apply("A B");

        Assert.Equal("C C", result);
    }

    [Fact]
    public void Apply_ReplacementWithDollarSign_InsertedLiterally()
    {
        _settings.Replacements = [new ReplacementRule { From = "price", To = "$5" }];

        var result = CreateService().Apply("the price");

        Assert.Equal("the $5", result);
    }

    [Fact]
    public void Apply_PhraseWithSpaces_Replaced()
    {
        _settings.Replacements = [new ReplacementRule { From = "New York", To = "NYC" }];

        var result = CreateService().Apply("I love New York.");

        Assert.Equal("I love NYC.", result);
    }

    [Fact]
    public void Apply_EmptyFromRule_Skipped()
    {
        _settings.Replacements = [new ReplacementRule { From = "", To = "x" }];

        var result = CreateService().Apply("unchanged");

        Assert.Equal("unchanged", result);
    }

    [Fact]
    public void Apply_ToEmpty_DeletesMatches()
    {
        _settings.Replacements = [new ReplacementRule { From = "um ", To = "", WholeWord = false }];

        var result = CreateService().Apply("um so um yeah");

        Assert.Equal("so yeah", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Apply_NullOrEmptyText_ReturnedAsIs(string? text)
    {
        _settings.Replacements = [new ReplacementRule { From = "a", To = "b" }];

        var result = CreateService().Apply(text!);

        Assert.Equal(text, result);
    }
}
