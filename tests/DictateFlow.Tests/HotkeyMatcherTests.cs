using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Audio;

namespace DictateFlow.Tests;

/// <summary>Tests for <see cref="HotkeyMatcher"/>.</summary>
public sealed class HotkeyMatcherTests
{
    [Theory]
    [InlineData(HotkeyMatcher.VkLControl, true)]
    [InlineData(HotkeyMatcher.VkRWin, true)]
    [InlineData(0x44u, false)] // VK_D
    public void IsModifierKey_IdentifiesModifiers(uint vk, bool expected)
    {
        Assert.Equal(expected, HotkeyMatcher.IsModifierKey(vk));
    }

    [Fact]
    public void ModifiersSatisfied_AnySide_MatchesEitherPhysicalKey()
    {
        var chord = HotkeyParser.Parse("Ctrl+Win");

        Assert.True(HotkeyMatcher.ModifiersSatisfied(
            chord, [HotkeyMatcher.VkLControl, HotkeyMatcher.VkLWin], exact: true));
        Assert.True(HotkeyMatcher.ModifiersSatisfied(
            chord, [HotkeyMatcher.VkRControl, HotkeyMatcher.VkRWin], exact: true));
    }

    [Fact]
    public void ModifiersSatisfied_RightSide_RejectsLeftKey()
    {
        var chord = HotkeyParser.Parse("RCtrl+RShift");

        Assert.True(HotkeyMatcher.ModifiersSatisfied(
            chord, [HotkeyMatcher.VkRControl, HotkeyMatcher.VkRShift], exact: true));
        Assert.False(HotkeyMatcher.ModifiersSatisfied(
            chord, [HotkeyMatcher.VkLControl, HotkeyMatcher.VkRShift], exact: true));
    }

    [Fact]
    public void ModifiersSatisfied_Exact_RejectsExtraModifier()
    {
        var chord = HotkeyParser.Parse("Ctrl+Win");

        Assert.False(HotkeyMatcher.ModifiersSatisfied(
            chord,
            [HotkeyMatcher.VkLControl, HotkeyMatcher.VkLWin, HotkeyMatcher.VkLShift],
            exact: true));
    }

    [Fact]
    public void ModifiersSatisfied_NonExact_ToleratesExtraModifier()
    {
        // Main-key chord modifiers are matched as a subset, so an extra held modifier is fine.
        var chord = HotkeyParser.Parse("Ctrl+Alt+D");

        Assert.True(HotkeyMatcher.ModifiersSatisfied(
            chord,
            [HotkeyMatcher.VkLControl, HotkeyMatcher.VkLAlt, HotkeyMatcher.VkLShift],
            exact: false));
    }

    [Fact]
    public void ModifiersSatisfied_MissingRequiredModifier_ReturnsFalse()
    {
        var chord = HotkeyParser.Parse("Ctrl+Win");

        Assert.False(HotkeyMatcher.ModifiersSatisfied(
            chord, [HotkeyMatcher.VkLControl], exact: true));
    }

    [Theory]
    [InlineData(HotkeyMatcher.VkRControl, true)]
    [InlineData(HotkeyMatcher.VkLControl, false)]
    [InlineData(HotkeyMatcher.VkRShift, false)]
    public void Matches_RespectsKindAndSide(uint vk, bool expected)
    {
        var requirement = new HotkeyModifier(HotkeyModifiers.Control, ModifierSide.Right);

        Assert.Equal(expected, HotkeyMatcher.Matches(requirement, vk));
    }
}
