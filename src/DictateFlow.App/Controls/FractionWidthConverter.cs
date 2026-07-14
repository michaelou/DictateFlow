using System.Globalization;
using System.Windows.Data;

namespace DictateFlow.App.Controls;

/// <summary>
/// Multiplies a fraction (0..1) by an available pixel width to size a proportional bar segment.
/// Bind two values: <c>[0]</c> the fraction, <c>[1]</c> the track's <c>ActualWidth</c>. Returns a
/// non-negative pixel width, so a zero fraction collapses the segment and leaves the track showing.
/// </summary>
public sealed class FractionWidthConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double fraction || values[1] is not double width)
        {
            return 0.0;
        }

        if (double.IsNaN(fraction) || double.IsNaN(width))
        {
            return 0.0;
        }

        return Math.Max(0.0, fraction * width);
    }

    /// <inheritdoc />
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
