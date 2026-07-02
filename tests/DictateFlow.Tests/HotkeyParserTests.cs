using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Audio;

namespace DictateFlow.Tests;

/// <summary>Tests for <see cref="HotkeyParser"/>.</summary>
public sealed class HotkeyParserTests
{
    [Fact]
    public void Parse_CtrlAltD_ReturnsExpectedChord()
    {
        var chord = HotkeyParser.Parse("Ctrl+Alt+D");

        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, chord.Modifiers);
        Assert.Equal(0x44u, chord.VirtualKey); // VK_D
        Assert.Equal("D", chord.KeyName);
    }

    [Theory]
    [InlineData("ctrl + alt + d")]
    [InlineData("CONTROL+ALT+D")]
    [InlineData(" Ctrl+Alt+D ")]
    public void Parse_IsCaseAndWhitespaceInsensitive(string text)
    {
        var chord = HotkeyParser.Parse(text);

        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, chord.Modifiers);
        Assert.Equal(0x44u, chord.VirtualKey);
    }

    [Theory]
    [InlineData("F12", HotkeyModifiers.None, 0x7Bu)]
    [InlineData("Shift+Space", HotkeyModifiers.Shift, 0x20u)]
    [InlineData("Win+Enter", HotkeyModifiers.Windows, 0x0Du)]
    [InlineData("Ctrl+Shift+NumPad5", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x65u)]
    [InlineData("Alt+OemTilde", HotkeyModifiers.Alt, 0xC0u)]
    public void Parse_SupportsCommonKeys(string text, HotkeyModifiers expectedModifiers, uint expectedVirtualKey)
    {
        var chord = HotkeyParser.Parse(text);

        Assert.Equal(expectedModifiers, chord.Modifiers);
        Assert.Equal(expectedVirtualKey, chord.VirtualKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]           // no main key
    [InlineData("Ctrl+NotAKey")]
    [InlineData("Foo+D")]
    [InlineData("Ctrl+D+F")]           // two main keys
    [InlineData("D+Ctrl")]             // main key must come last
    [InlineData("+")]
    public void TryParse_InvalidInput_ReturnsFalse(string? text)
    {
        Assert.False(HotkeyParser.TryParse(text, out _));
    }

    [Fact]
    public void Parse_InvalidInput_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => HotkeyParser.Parse("Ctrl+Alt"));
    }

    [Theory]
    [InlineData("Ctrl+Alt+D")]
    [InlineData("Ctrl+Alt+Shift+Win+F12")]
    [InlineData("Space")]
    [InlineData("Shift+OemComma")]
    public void ToString_RoundTripsCanonicalStrings(string text)
    {
        var chord = HotkeyParser.Parse(text);

        Assert.Equal(text, chord.ToString());
    }

    [Fact]
    public void ToString_NormalizesAliasesAndOrder()
    {
        // "Control"/"Return" are aliases; modifier order is normalized to Ctrl, Alt, Shift, Win.
        Assert.Equal("Ctrl+Alt+Enter", HotkeyParser.Parse("alt+control+return").ToString());
    }

    [Fact]
    public void ToString_RoundTripsThroughParse()
    {
        var original = HotkeyParser.Parse("Ctrl+Shift+K");

        var reparsed = HotkeyParser.Parse(original.ToString());

        Assert.Equal(original, reparsed);
    }

    [Fact]
    public void TryFromVirtualKey_KnownKey_ProducesCanonicalChord()
    {
        var found = HotkeyParser.TryFromVirtualKey(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x44, out var chord);

        Assert.True(found);
        Assert.Equal("Ctrl+Alt+D", chord.ToString());
    }

    [Fact]
    public void TryFromVirtualKey_UnknownKey_ReturnsFalse()
    {
        Assert.False(HotkeyParser.TryFromVirtualKey(HotkeyModifiers.Control, 0xFF, out _));
    }
}
