using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Audio;

/// <summary>
/// Pure matching logic for the keyboard-hook hotkey listener: decides whether a set of
/// currently-pressed modifier keys (identified by their side-specific Win32 virtual-key codes)
/// satisfies a chord's modifier requirements. Kept free of Win32 P/Invoke so it can be unit
/// tested in isolation.
/// </summary>
public static class HotkeyMatcher
{
    /// <summary>Left Shift virtual-key code (<c>VK_LSHIFT</c>).</summary>
    public const uint VkLShift = 0xA0;

    /// <summary>Right Shift virtual-key code (<c>VK_RSHIFT</c>).</summary>
    public const uint VkRShift = 0xA1;

    /// <summary>Left Ctrl virtual-key code (<c>VK_LCONTROL</c>).</summary>
    public const uint VkLControl = 0xA2;

    /// <summary>Right Ctrl virtual-key code (<c>VK_RCONTROL</c>).</summary>
    public const uint VkRControl = 0xA3;

    /// <summary>Left Alt virtual-key code (<c>VK_LMENU</c>).</summary>
    public const uint VkLAlt = 0xA4;

    /// <summary>Right Alt virtual-key code (<c>VK_RMENU</c>).</summary>
    public const uint VkRAlt = 0xA5;

    /// <summary>Left Windows virtual-key code (<c>VK_LWIN</c>).</summary>
    public const uint VkLWin = 0x5B;

    /// <summary>Right Windows virtual-key code (<c>VK_RWIN</c>).</summary>
    public const uint VkRWin = 0x5C;

    /// <summary>Determines whether <paramref name="vkCode"/> is one of the side-specific modifier keys.</summary>
    /// <param name="vkCode">A Win32 virtual-key code.</param>
    /// <returns><see langword="true"/> when the code is a left/right Ctrl, Alt, Shift or Win key.</returns>
    public static bool IsModifierKey(uint vkCode)
        => Decode(vkCode) is not null;

    /// <summary>
    /// Determines whether the currently-pressed modifier keys satisfy <paramref name="chord"/>'s
    /// modifier requirements.
    /// </summary>
    /// <param name="chord">The chord whose <see cref="HotkeyChord.Modifiers"/> are checked.</param>
    /// <param name="pressedModifierKeys">Side-specific virtual-key codes of the modifiers currently held.</param>
    /// <param name="exact">
    /// When <see langword="true"/>, no modifier beyond those required may be held — used for
    /// modifier-only chords so, for example, <c>Ctrl+Shift+A</c> does not fire a bare
    /// <c>Ctrl+Win</c>. When <see langword="false"/> (main-key chords), extra modifiers are
    /// tolerated.
    /// </param>
    /// <returns><see langword="true"/> when the requirements are met.</returns>
    public static bool ModifiersSatisfied(
        HotkeyChord chord, IReadOnlyCollection<uint> pressedModifierKeys, bool exact)
    {
        foreach (var requirement in chord.Modifiers)
        {
            var met = false;
            foreach (var vk in pressedModifierKeys)
            {
                if (Matches(requirement, vk))
                {
                    met = true;
                    break;
                }
            }

            if (!met)
            {
                return false;
            }
        }

        if (!exact)
        {
            return true;
        }

        // Exact: every pressed modifier must be accounted for by some requirement.
        foreach (var vk in pressedModifierKeys)
        {
            var accounted = false;
            foreach (var requirement in chord.Modifiers)
            {
                if (Matches(requirement, vk))
                {
                    accounted = true;
                    break;
                }
            }

            if (!accounted)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Determines whether the pressed key <paramref name="vkCode"/> satisfies <paramref name="requirement"/>.</summary>
    /// <param name="requirement">A single modifier requirement of a chord.</param>
    /// <param name="vkCode">A side-specific modifier virtual-key code that is currently pressed.</param>
    /// <returns><see langword="true"/> when the key matches the requirement's kind and side.</returns>
    public static bool Matches(HotkeyModifier requirement, uint vkCode)
    {
        if (Decode(vkCode) is not { } decoded || decoded.Kind != requirement.Kind)
        {
            return false;
        }

        return requirement.Side == ModifierSide.Any || requirement.Side == decoded.Side;
    }

    /// <summary>Decodes a side-specific modifier virtual-key code into its kind and side.</summary>
    private static (HotkeyModifiers Kind, ModifierSide Side)? Decode(uint vkCode) => vkCode switch
    {
        VkLShift => (HotkeyModifiers.Shift, ModifierSide.Left),
        VkRShift => (HotkeyModifiers.Shift, ModifierSide.Right),
        VkLControl => (HotkeyModifiers.Control, ModifierSide.Left),
        VkRControl => (HotkeyModifiers.Control, ModifierSide.Right),
        VkLAlt => (HotkeyModifiers.Alt, ModifierSide.Left),
        VkRAlt => (HotkeyModifiers.Alt, ModifierSide.Right),
        VkLWin => (HotkeyModifiers.Windows, ModifierSide.Left),
        VkRWin => (HotkeyModifiers.Windows, ModifierSide.Right),
        _ => null,
    };
}
