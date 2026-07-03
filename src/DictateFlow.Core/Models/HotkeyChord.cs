namespace DictateFlow.Core.Models;

/// <summary>
/// Modifier keys of a hotkey chord. The numeric values match the Win32
/// <c>MOD_*</c> constants (and, coincidentally, WPF's <c>ModifierKeys</c>). Side
/// information is carried separately by <see cref="ModifierSide"/> — these flags name
/// only <em>which</em> modifier, not which physical key.
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>The Alt key (<c>MOD_ALT</c>).</summary>
    Alt = 1,

    /// <summary>The Ctrl key (<c>MOD_CONTROL</c>).</summary>
    Control = 2,

    /// <summary>The Shift key (<c>MOD_SHIFT</c>).</summary>
    Shift = 4,

    /// <summary>The Windows key (<c>MOD_WIN</c>).</summary>
    Windows = 8,
}

/// <summary>Which physical instance of a modifier key a requirement matches.</summary>
public enum ModifierSide
{
    /// <summary>Either the left or the right key satisfies the requirement.</summary>
    Any,

    /// <summary>Only the left key satisfies the requirement.</summary>
    Left,

    /// <summary>Only the right key satisfies the requirement.</summary>
    Right,
}

/// <summary>
/// A single modifier requirement of a chord: which modifier, and which side. A chord such as
/// <c>Ctrl+Win</c> holds two <see cref="ModifierSide.Any"/> requirements; <c>RCtrl+RShift</c>
/// holds two <see cref="ModifierSide.Right"/> requirements.
/// </summary>
/// <param name="Kind">Which modifier key is required.</param>
/// <param name="Side">Which physical side satisfies the requirement.</param>
public readonly record struct HotkeyModifier(HotkeyModifiers Kind, ModifierSide Side);

/// <summary>
/// A parsed hotkey combination: an ordered set of modifier requirements plus an optional main
/// key (a Win32 virtual-key code). A chord is either a <em>main-key chord</em> (a main key,
/// with any number of modifiers, e.g. <c>Ctrl+Alt+D</c>) or a <em>modifier-only chord</em>
/// (two or more modifiers and no main key, e.g. <c>RCtrl+RShift</c>). Lone single modifiers
/// are not representable as a valid chord — the parser rejects them.
/// </summary>
public sealed class HotkeyChord : IEquatable<HotkeyChord>
{
    /// <summary>Initializes a new instance of the <see cref="HotkeyChord"/> class.</summary>
    /// <param name="modifiers">The modifier requirements that must be held.</param>
    /// <param name="virtualKey">Win32 virtual-key code of the main key, or <see langword="null"/> for a modifier-only chord.</param>
    /// <param name="keyName">Canonical display name of the main key (e.g. <c>"D"</c>), or <see langword="null"/> for a modifier-only chord.</param>
    public HotkeyChord(IReadOnlyList<HotkeyModifier> modifiers, uint? virtualKey, string? keyName)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
        KeyName = keyName;
    }

    /// <summary>Gets the modifier requirements that must be held.</summary>
    public IReadOnlyList<HotkeyModifier> Modifiers { get; }

    /// <summary>Gets the Win32 virtual-key code of the main key, or <see langword="null"/> for a modifier-only chord.</summary>
    public uint? VirtualKey { get; }

    /// <summary>Gets the canonical display name of the main key, or <see langword="null"/> for a modifier-only chord.</summary>
    public string? KeyName { get; }

    /// <summary>Gets a value indicating whether this chord has no main key (modifiers only).</summary>
    public bool IsModifierOnly => VirtualKey is null;

    /// <summary>Formats the chord in the canonical <c>"Ctrl+Alt+D"</c> / <c>"RCtrl+RShift"</c> settings format.</summary>
    /// <returns>The canonical string representation.</returns>
    public override string ToString()
    {
        var parts = new List<string>(Modifiers.Count + 1);
        foreach (var modifier in OrderedModifiers())
        {
            parts.Add(Spell(modifier));
        }

        if (KeyName is not null)
        {
            parts.Add(KeyName);
        }

        return string.Join('+', parts);
    }

    /// <inheritdoc />
    public bool Equals(HotkeyChord? other)
        => other is not null && string.Equals(ToString(), other.ToString(), StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as HotkeyChord);

    /// <inheritdoc />
    public override int GetHashCode() => ToString().GetHashCode(StringComparison.Ordinal);

    /// <summary>Compares two chords by their canonical representation.</summary>
    /// <param name="left">The first chord.</param>
    /// <param name="right">The second chord.</param>
    /// <returns><see langword="true"/> when both are <see langword="null"/> or represent the same chord.</returns>
    public static bool operator ==(HotkeyChord? left, HotkeyChord? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Compares two chords by their canonical representation.</summary>
    /// <param name="left">The first chord.</param>
    /// <param name="right">The second chord.</param>
    /// <returns><see langword="true"/> when the chords differ.</returns>
    public static bool operator !=(HotkeyChord? left, HotkeyChord? right) => !(left == right);

    /// <summary>Orders the modifiers into the canonical Ctrl, Alt, Shift, Win sequence (left before right within a kind).</summary>
    private IEnumerable<HotkeyModifier> OrderedModifiers()
        => Modifiers
            .Distinct()
            .OrderBy(m => KindOrder(m.Kind))
            .ThenBy(m => (int)m.Side);

    private static int KindOrder(HotkeyModifiers kind) => kind switch
    {
        HotkeyModifiers.Control => 0,
        HotkeyModifiers.Alt => 1,
        HotkeyModifiers.Shift => 2,
        HotkeyModifiers.Windows => 3,
        _ => 4,
    };

    private static string Spell(HotkeyModifier modifier)
    {
        var prefix = modifier.Side switch
        {
            ModifierSide.Left => "L",
            ModifierSide.Right => "R",
            _ => "",
        };

        var name = modifier.Kind switch
        {
            HotkeyModifiers.Control => "Ctrl",
            HotkeyModifiers.Alt => "Alt",
            HotkeyModifiers.Shift => "Shift",
            HotkeyModifiers.Windows => "Win",
            _ => "",
        };

        return prefix + name;
    }
}
