using System.Globalization;
using System.Windows.Media;
using DictateFlow.App.Controls;

namespace DictateFlow.Tests;

/// <summary>
/// Verifies the History mode-pill colour mapping: stable per name, distinct "fg" vs faint "bg",
/// and a neutral colour for the unknown/placeholder mode.
/// </summary>
public sealed class ModeColorConverterTests
{
    private readonly ModeColorConverter _converter = new();

    private Color Convert(string? name, string? parameter = null)
    {
        var result = _converter.Convert(name, typeof(Brush), parameter, CultureInfo.InvariantCulture);
        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.True(brush.IsFrozen, "Brush should be frozen so it is safe to share across the UI.");
        return brush.Color;
    }

    [Fact]
    public void Convert_SameName_YieldsSameColour()
    {
        Assert.Equal(Convert("Email"), Convert("Email"));
    }

    [Fact]
    public void Convert_DifferentNames_MapAcrossThePalette()
    {
        // Not a guarantee of all-distinct (hash collisions exist), but these curated names
        // should not all collapse to one colour — proves the mapping actually varies.
        var colours = new[] { "Email", "Note", "Code", "Default", "Casual" }
            .Select(n => Convert(n))
            .Distinct()
            .Count();
        Assert.True(colours >= 3, $"Expected a spread of pill colours, got {colours}.");
    }

    [Fact]
    public void Convert_BackgroundParameter_IsFaintTintOfForeground()
    {
        var fg = Convert("Code");
        var bg = Convert("Code", "bg");

        Assert.Equal(0x33, bg.A);            // low alpha → subtle tint on the dark surface
        Assert.Equal(0xFF, fg.A);            // solid foreground
        Assert.Equal(fg.R, bg.R);            // same hue
        Assert.Equal(fg.G, bg.G);
        Assert.Equal(fg.B, bg.B);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("—")]
    public void Convert_UnknownMode_UsesNeutralGrey(string? name)
    {
        var color = Convert(name);
        Assert.Equal(Color.FromRgb(0xA0, 0xA0, 0xA0), color);
    }
}
