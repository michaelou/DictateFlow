using System.Windows;
using System.Windows.Input;
using DictateFlow.App.ViewModels;
using DictateFlow.Core.Models;

namespace DictateFlow.App.Views;

/// <summary>
/// Settings window shell. All behavior lives in
/// <see cref="SettingsViewModel"/>; the code-behind only initializes the view and
/// translates raw key events from the hotkey-capture textbox into view-model calls.
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>Initializes a new instance of the <see cref="SettingsWindow"/> class.</summary>
    public SettingsWindow()
    {
        InitializeComponent();
    }

    /// <summary>Captures a chord for the push-to-talk hotkey box.</summary>
    private void OnPushToTalkHotkeyPreviewKeyDown(object sender, KeyEventArgs e)
        => CaptureHotkey(e, (vm, mods, vk) => vm.CapturePushToTalkHotkey(mods, vk));

    /// <summary>Captures a chord for the toggle hotkey box.</summary>
    private void OnToggleHotkeyPreviewKeyDown(object sender, KeyEventArgs e)
        => CaptureHotkey(e, (vm, mods, vk) => vm.CaptureToggleHotkey(mods, vk));

    /// <summary>
    /// Captures the chord pressed while a hotkey textbox has focus and forwards it to the
    /// view model via <paramref name="apply"/>. Pure modifier presses are ignored — the chord
    /// completes with a main key.
    /// </summary>
    private void CaptureHotkey(KeyEventArgs e, Action<SettingsViewModel, HotkeyModifiers, uint> apply)
    {
        e.Handled = true;

        // Alt combinations arrive as Key.System with the real key in SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin)
        {
            return;
        }

        if (DataContext is SettingsViewModel viewModel)
        {
            // WPF ModifierKeys uses the same flag values as HotkeyModifiers (MOD_* constants).
            var modifiers = (HotkeyModifiers)Keyboard.Modifiers;
            var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
            apply(viewModel, modifiers, virtualKey);
        }
    }
}
