using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DictateFlow.App.Controls;

/// <summary>
/// Colour-codes an estimated cost by magnitude so a glance conveys spend: nothing spent reads
/// neutral, small amounts green, growing amounts amber then red. Thresholds are a currency-neutral
/// heuristic (interpreted in the display currency's own units).
/// </summary>
public sealed class CostMagnitudeBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Neutral = Frozen(0xA0, 0xA0, 0xA0);
    private static readonly SolidColorBrush Low = Frozen(0x3F, 0xB9, 0x50);     // green
    private static readonly SolidColorBrush Medium = Frozen(0xE8, 0xCE, 0x6A);  // amber
    private static readonly SolidColorBrush High = Frozen(0xFF, 0x6B, 0x68);    // red

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var cost = value switch
        {
            double d => d,
            float f => f,
            _ => 0.0,
        };

        return cost switch
        {
            <= 0 => Neutral,
            < 1 => Low,
            < 10 => Medium,
            _ => High,
        };
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
