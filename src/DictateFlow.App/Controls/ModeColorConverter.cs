using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DictateFlow.App.Controls;

/// <summary>
/// Maps a prompt-mode name to a stable colour so History mode pills are scannable at a glance.
/// The same name always yields the same colour (deterministic hash over a curated palette that
/// stays legible on the dark surface). Pass <c>"bg"</c> as the converter parameter for a faint
/// tinted fill and <c>"fg"</c> (the default) for the solid text/border colour.
/// </summary>
public sealed class ModeColorConverter : IValueConverter
{
    /// <summary>Palette of foreground colours, each chosen to read clearly on the dark surface.</summary>
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x4C, 0x9A, 0xFF), // blue
        Color.FromRgb(0x3F, 0xD0, 0xB8), // teal
        Color.FromRgb(0xA7, 0x8B, 0xFA), // purple
        Color.FromRgb(0xF0, 0xA7, 0x3B), // amber
        Color.FromRgb(0xF4, 0x72, 0xB6), // rose
        Color.FromRgb(0x56, 0xD3, 0x64), // green
        Color.FromRgb(0x38, 0xBD, 0xF8), // cyan
        Color.FromRgb(0xFB, 0x92, 0x3C), // orange
    ];

    /// <summary>Neutral colour used for the placeholder / unknown mode ("—").</summary>
    private static readonly Color Neutral = Color.FromRgb(0xA0, 0xA0, 0xA0);

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value as string;
        var wantBackground = string.Equals(parameter as string, "bg", StringComparison.OrdinalIgnoreCase);

        var color = string.IsNullOrEmpty(name) || name == "—" ? Neutral : Palette[StableIndex(name)];
        if (wantBackground)
        {
            // Faint tint over the dark surface — same hue, low alpha.
            color = Color.FromArgb(0x33, color.R, color.G, color.B);
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>Deterministic FNV-1a hash folded into a palette index (stable across processes).</summary>
    private static int StableIndex(string name)
    {
        uint hash = 2166136261;
        foreach (var ch in name)
        {
            hash = (hash ^ ch) * 16777619;
        }

        return (int)(hash % (uint)Palette.Length);
    }
}
