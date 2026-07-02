namespace DictateFlow.Core.Models;

/// <summary>
/// Modifier keys of a hotkey chord. The numeric values match the Win32
/// <c>MOD_*</c> constants used by <c>RegisterHotKey</c> (and, coincidentally,
/// WPF's <c>ModifierKeys</c>), so they can be passed to the API directly.
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

/// <summary>
/// A parsed hotkey combination: modifier flags plus the main key as a Win32 virtual-key code.
/// </summary>
/// <param name="Modifiers">Modifier keys that must be held.</param>
/// <param name="VirtualKey">Win32 virtual-key code of the main key.</param>
/// <param name="KeyName">Canonical display name of the main key (e.g. <c>"D"</c>, <c>"F12"</c>).</param>
public sealed record HotkeyChord(HotkeyModifiers Modifiers, uint VirtualKey, string KeyName)
{
    /// <summary>Formats the chord in the canonical <c>"Ctrl+Alt+D"</c> settings format.</summary>
    /// <returns>The canonical string representation.</returns>
    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(KeyName);
        return string.Join('+', parts);
    }
}
