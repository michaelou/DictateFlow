using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Audio;

/// <summary>
/// Converts between the <c>"Ctrl+Alt+D"</c> hotkey string format used in settings and
/// <see cref="HotkeyChord"/> (modifier flags + Win32 virtual-key code). Parsing is
/// case-insensitive and tolerates whitespace around the <c>+</c> separators.
/// </summary>
public static class HotkeyParser
{
    private static readonly Dictionary<string, HotkeyModifiers> ModifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = HotkeyModifiers.Control,
        ["Control"] = HotkeyModifiers.Control,
        ["Alt"] = HotkeyModifiers.Alt,
        ["Shift"] = HotkeyModifiers.Shift,
        ["Win"] = HotkeyModifiers.Windows,
        ["Windows"] = HotkeyModifiers.Windows,
        ["Meta"] = HotkeyModifiers.Windows,
        ["Super"] = HotkeyModifiers.Windows,
    };

    /// <summary>Key name → virtual-key code. The first name registered per code is canonical.</summary>
    private static readonly Dictionary<string, uint> KeyNames = BuildKeyNames();

    /// <summary>Virtual-key code → canonical key name, derived from <see cref="KeyNames"/>.</summary>
    private static readonly Dictionary<uint, string> CanonicalNames = BuildCanonicalNames();

    /// <summary>Parses a hotkey string such as <c>"Ctrl+Alt+D"</c>.</summary>
    /// <param name="text">The hotkey string.</param>
    /// <returns>The parsed chord.</returns>
    /// <exception cref="FormatException"><paramref name="text"/> is not a valid hotkey.</exception>
    public static HotkeyChord Parse(string text)
        => TryParse(text, out var chord)
            ? chord
            : throw new FormatException($"'{text}' is not a valid hotkey. Expected e.g. 'Ctrl+Alt+D'.");

    /// <summary>Attempts to parse a hotkey string such as <c>"Ctrl+Alt+D"</c>.</summary>
    /// <param name="text">The hotkey string.</param>
    /// <param name="chord">The parsed chord on success.</param>
    /// <returns><see langword="true"/> when <paramref name="text"/> is a valid hotkey.</returns>
    public static bool TryParse(string? text, out HotkeyChord chord)
    {
        chord = null!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        uint virtualKey = 0;
        string? keyName = null;

        var parts = text.Split('+');
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            if (part.Length == 0)
            {
                return false;
            }

            if (ModifierNames.TryGetValue(part, out var modifier))
            {
                modifiers |= modifier;
                continue;
            }

            // The main key must be the last (non-modifier) token — at most one is allowed.
            if (keyName is not null || i != parts.Length - 1 || !KeyNames.TryGetValue(part, out virtualKey))
            {
                return false;
            }

            keyName = CanonicalNames[virtualKey];
        }

        if (keyName is null)
        {
            return false;
        }

        chord = new HotkeyChord(modifiers, virtualKey, keyName);
        return true;
    }

    /// <summary>
    /// Builds a chord from raw modifier flags and a virtual-key code — used by the settings
    /// hotkey-capture textbox, which receives key events rather than strings.
    /// </summary>
    /// <param name="modifiers">The modifier flags held down.</param>
    /// <param name="virtualKey">The virtual-key code of the main key.</param>
    /// <param name="chord">The chord with its canonical key name on success.</param>
    /// <returns><see langword="false"/> when <paramref name="virtualKey"/> is not a supported main key.</returns>
    public static bool TryFromVirtualKey(HotkeyModifiers modifiers, uint virtualKey, out HotkeyChord chord)
    {
        if (CanonicalNames.TryGetValue(virtualKey, out var name))
        {
            chord = new HotkeyChord(modifiers, virtualKey, name);
            return true;
        }

        chord = null!;
        return false;
    }

    private static Dictionary<string, uint> BuildKeyNames()
    {
        var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        for (var c = 'A'; c <= 'Z'; c++)
        {
            map[c.ToString()] = (uint)c; // VK_A..VK_Z match ASCII
        }

        for (var d = 0; d <= 9; d++)
        {
            map[d.ToString()] = (uint)(0x30 + d); // VK_0..VK_9 match ASCII
            map["D" + d] = (uint)(0x30 + d);
        }

        for (var f = 1; f <= 24; f++)
        {
            map["F" + f] = (uint)(0x70 + f - 1); // VK_F1 = 0x70
        }

        for (var n = 0; n <= 9; n++)
        {
            map["NumPad" + n] = (uint)(0x60 + n); // VK_NUMPAD0 = 0x60
        }

        map["Space"] = 0x20;
        map["Tab"] = 0x09;
        map["Enter"] = 0x0D;
        map["Return"] = 0x0D;
        map["Escape"] = 0x1B;
        map["Esc"] = 0x1B;
        map["Backspace"] = 0x08;
        map["Delete"] = 0x2E;
        map["Del"] = 0x2E;
        map["Insert"] = 0x2D;
        map["Ins"] = 0x2D;
        map["Home"] = 0x24;
        map["End"] = 0x23;
        map["PageUp"] = 0x21;
        map["PgUp"] = 0x21;
        map["PageDown"] = 0x22;
        map["PgDn"] = 0x22;
        map["Up"] = 0x26;
        map["Down"] = 0x28;
        map["Left"] = 0x25;
        map["Right"] = 0x27;
        map["Pause"] = 0x13;
        map["PrintScreen"] = 0x2C;
        map["CapsLock"] = 0x14;
        map["ScrollLock"] = 0x91;
        map["NumLock"] = 0x90;
        map["Multiply"] = 0x6A;
        map["Add"] = 0x6B;
        map["Subtract"] = 0x6D;
        map["Decimal"] = 0x6E;
        map["Divide"] = 0x6F;
        map["OemSemicolon"] = 0xBA;
        map["OemPlus"] = 0xBB;
        map["OemComma"] = 0xBC;
        map["OemMinus"] = 0xBD;
        map["OemPeriod"] = 0xBE;
        map["OemQuestion"] = 0xBF;
        map["OemTilde"] = 0xC0;
        map["OemOpenBrackets"] = 0xDB;
        map["OemPipe"] = 0xDC;
        map["OemCloseBrackets"] = 0xDD;
        map["OemQuotes"] = 0xDE;
        map["OemBackslash"] = 0xE2;

        return map;
    }

    private static Dictionary<uint, string> BuildCanonicalNames()
    {
        // First name registered per virtual-key wins, so insertion order in
        // BuildKeyNames defines the canonical spelling (e.g. "Enter" over "Return").
        var map = new Dictionary<uint, string>();
        foreach (var (name, vk) in KeyNames)
        {
            map.TryAdd(vk, name.Length == 1 ? name.ToUpperInvariant() : name);
        }

        return map;
    }
}
