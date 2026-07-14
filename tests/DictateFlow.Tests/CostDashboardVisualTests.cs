using System.Globalization;
using System.Windows.Media;
using DictateFlow.App.Controls;
using DictateFlow.App.ViewModels;
using DictateFlow.Core.Models;

namespace DictateFlow.Tests;

/// <summary>
/// Covers the pure logic behind the cost-dashboard visuals: magnitude colour thresholds, the
/// proportional-bar width converter, and the cost-fraction computation on <see cref="CostPeriodItem"/>.
/// </summary>
public sealed class CostDashboardVisualTests
{
    private static readonly Color Neutral = Color.FromRgb(0xA0, 0xA0, 0xA0);
    private static readonly Color Low = Color.FromRgb(0x3F, 0xB9, 0x50);
    private static readonly Color Medium = Color.FromRgb(0xE8, 0xCE, 0x6A);
    private static readonly Color High = Color.FromRgb(0xFF, 0x6B, 0x68);

    private static Color Magnitude(double cost)
    {
        var brush = (SolidColorBrush)new CostMagnitudeBrushConverter()
            .Convert(cost, typeof(Brush), null, CultureInfo.InvariantCulture);
        return brush.Color;
    }

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(-5.0, false)]
    [InlineData(0.0203, true)]
    [InlineData(0.9999, true)]
    public void Magnitude_ZeroOrLow(double cost, bool low)
        => Assert.Equal(low ? Low : Neutral, Magnitude(cost));

    [Theory]
    [InlineData(1.0, true)]
    [InlineData(9.999, true)]
    [InlineData(10.0, false)]
    [InlineData(24.134, false)]
    public void Magnitude_MediumOrHigh(double cost, bool medium)
        => Assert.Equal(medium ? Medium : High, Magnitude(cost));

    [Fact]
    public void Magnitude_BrushIsFrozen()
    {
        var brush = (SolidColorBrush)new CostMagnitudeBrushConverter()
            .Convert(1.0, typeof(Brush), null, CultureInfo.InvariantCulture);
        Assert.True(brush.IsFrozen);
    }

    [Theory]
    [InlineData(0.5, 200.0, 100.0)]
    [InlineData(0.0, 200.0, 0.0)]
    [InlineData(1.0, 0.0, 0.0)]
    public void FractionWidth_MultipliesFractionByWidth(double fraction, double width, double expected)
    {
        var result = new FractionWidthConverter()
            .Convert([fraction, width], typeof(double), null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, (double)result);
    }

    [Fact]
    public void FractionWidth_GuardsMissingOrNaNValues()
    {
        var converter = new FractionWidthConverter();
        Assert.Equal(0.0, (double)converter.Convert([0.5], typeof(double), null, CultureInfo.InvariantCulture));
        Assert.Equal(0.0, (double)converter.Convert([double.NaN, 200.0], typeof(double), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void CostPeriodItem_SplitsCostIntoFractions()
    {
        var period = new CostPeriod(
            SpeechRequests: 1, SpeechMinutes: 1, SpeechCost: 0.75,
            LlmRequests: 1, PromptTokens: 0, CompletionTokens: 0, LlmCost: 0.25, Words: 10);

        var item = new CostPeriodItem("Today", period, "USD");

        Assert.Equal(1.0, item.TotalCostValue, 6);
        Assert.Equal(0.75, item.SpeechCostFraction, 6);
        Assert.Equal(0.25, item.LlmCostFraction, 6);
        Assert.Equal("USD", item.Currency);
    }

    [Fact]
    public void CostPeriodItem_EmptyPeriod_HasZeroFractions()
    {
        var item = new CostPeriodItem("Today", CostPeriod.Empty, "USD");

        Assert.Equal(0.0, item.TotalCostValue);
        Assert.Equal(0.0, item.SpeechCostFraction);
        Assert.Equal(0.0, item.LlmCostFraction);
    }
}
