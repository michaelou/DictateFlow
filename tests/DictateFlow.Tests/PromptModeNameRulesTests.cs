using DictateFlow.Core.Services.Prompts;

namespace DictateFlow.Tests;

/// <summary>Tests for <see cref="PromptModeNameRules"/>.</summary>
public sealed class PromptModeNameRulesTests
{
    [Theory]
    [InlineData("Email")]
    [InlineData("My Mode")]
    [InlineData("Notes-2")]
    [InlineData("  Trimmed  ")] // validated after trimming
    [InlineData("Console")] // contains but is not a reserved name
    public void Validate_ValidNames_ReturnNull(string name)
        => Assert.Null(PromptModeNameRules.Validate(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData(".hidden")]
    [InlineData("dots.")]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("COM3")]
    [InlineData("lpt9")]
    public void Validate_InvalidNames_ReturnAnError(string? name)
        => Assert.NotNull(PromptModeNameRules.Validate(name));

    [Fact]
    public void Validate_NameOverMaxLength_ReturnsAnError()
        => Assert.NotNull(PromptModeNameRules.Validate(new string('a', PromptModeNameRules.MaxLength + 1)));
}
