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

        Assert.Equal("Ctrl+Alt+D", chord.ToString());
        Assert.Equal(0x44u, chord.VirtualKey); // VK_D
        Assert.Equal("D", chord.KeyName);
        Assert.False(chord.IsModifierOnly);
    }

    [Theory]
    [InlineData("ctrl + alt + d")]
    [InlineData("CONTROL+ALT+D")]
    [InlineData(" Ctrl+Alt+D ")]
    public void Parse_IsCaseAndWhitespaceInsensitive(string text)
    {
        var chord = HotkeyParser.Parse(text);

        Assert.Equal("Ctrl+Alt+D", chord.ToString());
        Assert.Equal(0x44u, chord.VirtualKey);
    }

    [Theory]
    [InlineData("F12", "F12", 0x7Bu)]
    [InlineData("Shift+Space", "Shift+Space", 0x20u)]
    [InlineData("Win+Enter", "Win+Enter", 0x0Du)]
    [InlineData("Ctrl+Shift+NumPad5", "Ctrl+Shift+NumPad5", 0x65u)]
    [InlineData("Alt+OemTilde", "Alt+OemTilde", 0xC0u)]
    public void Parse_SupportsCommonKeys(string text, string expectedCanonical, uint expectedVirtualKey)
    {
        var chord = HotkeyParser.Parse(text);

        Assert.Equal(expectedCanonical, chord.ToString());
        Assert.Equal(expectedVirtualKey, chord.VirtualKey);
    }

    [Theory]
    [InlineData("RCtrl+RShift")]
    [InlineData("Ctrl+Win")]
    [InlineData("LCtrl+RShift")]
    [InlineData("Ctrl+Alt+Shift+Win")]
    public void Parse_ModifierOnlyChords_HaveNoMainKey(string text)
    {
        var chord = HotkeyParser.Parse(text);

        Assert.True(chord.IsModifierOnly);
        Assert.Null(chord.VirtualKey);
        Assert.Null(chord.KeyName);
        Assert.Equal(text, chord.ToString());
    }

    [Fact]
    public void Parse_SideSpecificModifierWithMainKey_ReturnsExpectedChord()
    {
        var chord = HotkeyParser.Parse("LCtrl+Shift+D");

        Assert.Equal("LCtrl+Shift+D", chord.ToString());
        Assert.Equal(0x44u, chord.VirtualKey);
        Assert.Contains(new HotkeyModifier(HotkeyModifiers.Control, ModifierSide.Left), chord.Modifiers);
        Assert.Contains(new HotkeyModifier(HotkeyModifiers.Shift, ModifierSide.Any), chord.Modifiers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl")]                // lone modifier
    [InlineData("RCtrl")]              // lone side-specific modifier
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
        Assert.Throws<FormatException>(() => HotkeyParser.Parse("Ctrl"));
    }

    [Theory]
    [InlineData("Ctrl+Alt+D")]
    [InlineData("Ctrl+Alt+Shift+Win+F12")]
    [InlineData("Space")]
    [InlineData("Shift+OemComma")]
    [InlineData("RCtrl+RShift")]
    [InlineData("Ctrl+Win")]
    [InlineData("LCtrl+Shift+D")]
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
    public void TryFromCapture_KnownKey_ProducesCanonicalChord()
    {
        var modifiers = new[]
        {
            new HotkeyModifier(HotkeyModifiers.Control, ModifierSide.Any),
            new HotkeyModifier(HotkeyModifiers.Alt, ModifierSide.Any),
        };

        var found = HotkeyParser.TryFromCapture(modifiers, 0x44, out var chord);

        Assert.True(found);
        Assert.Equal("Ctrl+Alt+D", chord.ToString());
    }

    [Fact]
    public void TryFromCapture_ModifierOnly_ProducesCanonicalChord()
    {
        var modifiers = new[]
        {
            new HotkeyModifier(HotkeyModifiers.Control, ModifierSide.Right),
            new HotkeyModifier(HotkeyModifiers.Shift, ModifierSide.Right),
        };

        var found = HotkeyParser.TryFromCapture(modifiers, null, out var chord);

        Assert.True(found);
        Assert.Equal("RCtrl+RShift", chord.ToString());
    }

    [Fact]
    public void TryFromCapture_SingleModifierNoMainKey_ReturnsFalse()
    {
        var modifiers = new[] { new HotkeyModifier(HotkeyModifiers.Control, ModifierSide.Right) };

        Assert.False(HotkeyParser.TryFromCapture(modifiers, null, out _));
    }

    [Fact]
    public void TryFromCapture_UnknownKey_ReturnsFalse()
    {
        var modifiers = new[] { new HotkeyModifier(HotkeyModifiers.Control, ModifierSide.Any) };

        Assert.False(HotkeyParser.TryFromCapture(modifiers, 0xFF, out _));
    }
}
