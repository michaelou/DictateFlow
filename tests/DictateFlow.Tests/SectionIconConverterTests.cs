using DictateFlow.App.Controls;

namespace DictateFlow.Tests;

/// <summary>Verifies every Settings nav section maps to an icon key, with a gear fallback.</summary>
public sealed class SectionIconConverterTests
{
    [Theory]
    [InlineData("General", "Icon.Settings")]
    [InlineData("Speech", "Icon.Mic")]
    [InlineData("Local Models", "Icon.Cube")]
    [InlineData("LLM", "Icon.Sparkle")]
    [InlineData("Prompts", "Icon.Chat")]
    [InlineData("Dictionary", "Icon.Document")]
    [InlineData("Replacements", "Icon.ArrowSwap")]
    [InlineData("Rules", "Icon.AppsList")]
    [InlineData("Output", "Icon.Keyboard")]
    [InlineData("Voice Commands", "Icon.Wand")]
    [InlineData("History", "Icon.History")]
    [InlineData("Pricing", "Icon.Money")]
    [InlineData("Backup", "Icon.Save")]
    [InlineData("Diagnostics", "Icon.Wrench")]
    public void ResolveKey_MapsKnownSections(string section, string expected)
        => Assert.Equal(expected, SectionIconConverter.ResolveKey(section));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Nonexistent")]
    public void ResolveKey_FallsBackToGear(string? section)
        => Assert.Equal("Icon.Settings", SectionIconConverter.ResolveKey(section));

    [Fact]
    public void ResolveKey_GivesEverySectionADistinctIcon()
    {
        // Distinct keys prove each section has its own mapping (no accidental fallback/duplicate).
        string[] sections =
        [
            "General", "Speech", "Local Models", "LLM", "Prompts", "Dictionary", "Replacements",
            "Rules", "Output", "Voice Commands", "History", "Pricing", "Backup", "Diagnostics",
        ];

        var keys = sections.Select(SectionIconConverter.ResolveKey).ToList();
        Assert.Equal(sections.Length, keys.Distinct().Count());
    }
}
