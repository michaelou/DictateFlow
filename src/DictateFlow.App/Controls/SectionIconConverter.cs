using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DictateFlow.App.Controls;

/// <summary>
/// Maps a Settings navigation-section name to its Fluent icon geometry (a key in Icons.xaml),
/// so the nav rail can show a glyph beside each label. Unknown sections fall back to the gear.
/// </summary>
public sealed class SectionIconConverter : IValueConverter
{
    private static readonly Dictionary<string, string> IconKeys = new(StringComparer.Ordinal)
    {
        ["General"] = "Icon.Settings",
        ["Speech"] = "Icon.Mic",
        ["Local Models"] = "Icon.Cube",
        ["LLM"] = "Icon.Sparkle",
        ["Prompts"] = "Icon.Chat",
        ["Dictionary"] = "Icon.Document",
        ["Replacements"] = "Icon.ArrowSwap",
        ["Rules"] = "Icon.AppsList",
        ["Output"] = "Icon.Keyboard",
        ["Voice Commands"] = "Icon.Wand",
        ["History"] = "Icon.History",
        ["Cloud Recordings"] = "Icon.Download",
        ["Pricing"] = "Icon.Money",
        ["Backup"] = "Icon.Save",
        ["Diagnostics"] = "Icon.Wrench",
    };

    /// <summary>Resolves the Icons.xaml resource key for a section name; unknown names fall back to the gear.</summary>
    public static string ResolveKey(string? section)
        => section is not null && IconKeys.TryGetValue(section, out var mapped) ? mapped : "Icon.Settings";

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Application.Current?.TryFindResource(ResolveKey(value as string)) as Geometry;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
