using System.Windows;
using System.Windows.Input;
using DictateFlow.App.ViewModels;
using DictateFlow.Core.Models;

namespace DictateFlow.App.Views;

/// <summary>
/// Settings window shell. All behavior lives in
/// <see cref="SettingsViewModel"/>; the code-behind only initializes the view and
/// translates raw key events from the hotkey-capture textboxes into view-model calls.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly HotkeyCapture _pushToTalkCapture = new();
    private readonly HotkeyCapture _toggleCapture = new();

    /// <summary>Initializes a new instance of the <see cref="SettingsWindow"/> class.</summary>
    public SettingsWindow()
    {
        InitializeComponent();
    }

    /// <summary>Accumulates a chord for the push-to-talk hotkey box.</summary>
    private void OnPushToTalkHotkeyPreviewKeyDown(object sender, KeyEventArgs e)
        => OnHotkeyKeyDown(_pushToTalkCapture, e, ApplyPushToTalk);

    /// <summary>Completes a modifier-only chord for the push-to-talk hotkey box on release.</summary>
    private void OnPushToTalkHotkeyPreviewKeyUp(object sender, KeyEventArgs e)
        => OnHotkeyKeyUp(_pushToTalkCapture, e, ApplyPushToTalk);

    /// <summary>Accumulates a chord for the toggle hotkey box.</summary>
    private void OnToggleHotkeyPreviewKeyDown(object sender, KeyEventArgs e)
        => OnHotkeyKeyDown(_toggleCapture, e, ApplyToggle);

    /// <summary>Completes a modifier-only chord for the toggle hotkey box on release.</summary>
    private void OnToggleHotkeyPreviewKeyUp(object sender, KeyEventArgs e)
        => OnHotkeyKeyUp(_toggleCapture, e, ApplyToggle);

    private void ApplyPushToTalk(IReadOnlyList<HotkeyModifier> modifiers, uint? virtualKey)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.CapturePushToTalkHotkey(modifiers, virtualKey);
        }
    }

    private void ApplyToggle(IReadOnlyList<HotkeyModifier> modifiers, uint? virtualKey)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.CaptureToggleHotkey(modifiers, virtualKey);
        }
    }

    /// <summary>
    /// Handles a key-down while a hotkey box has focus. Modifier presses accumulate; the first
    /// non-modifier key completes a main-key chord (e.g. <c>Ctrl+Alt+D</c>). Further input is
    /// ignored until the keys are released, so keyboard auto-repeat cannot overwrite the chord.
    /// </summary>
    private static void OnHotkeyKeyDown(
        HotkeyCapture capture, KeyEventArgs e, Action<IReadOnlyList<HotkeyModifier>, uint?> apply)
    {
        e.Handled = true;
        if (capture.Finalized)
        {
            return;
        }

        // Alt combinations arrive as Key.System with the real key in SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (TryModifier(key) is { } modifier)
        {
            if (!capture.Held.Contains(modifier))
            {
                capture.Held.Add(modifier);
            }

            return;
        }

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        apply([.. capture.Held], virtualKey);
        capture.Finalized = true;
    }

    /// <summary>
    /// Handles a key-up while a hotkey box has focus. Releasing a key when two or more modifiers
    /// are held completes a modifier-only chord (e.g. <c>RCtrl+RShift</c>); otherwise it ends the
    /// current capture sequence so the box can be re-armed.
    /// </summary>
    private static void OnHotkeyKeyUp(
        HotkeyCapture capture, KeyEventArgs e, Action<IReadOnlyList<HotkeyModifier>, uint?> apply)
    {
        e.Handled = true;

        if (capture.Finalized)
        {
            // A chord was already committed on key-down; the first release re-arms capture.
            capture.Reset();
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (capture.Held.Count >= 2)
        {
            apply([.. capture.Held], null);
            capture.Finalized = true;
            return;
        }

        if (TryModifier(key) is { } modifier)
        {
            capture.Held.Remove(modifier);
        }
    }

    /// <summary>Maps a WPF modifier key to its side-specific <see cref="HotkeyModifier"/>, or <see langword="null"/> for non-modifiers.</summary>
    private static HotkeyModifier? TryModifier(Key key) => key switch
    {
        Key.LeftCtrl => new(HotkeyModifiers.Control, ModifierSide.Left),
        Key.RightCtrl => new(HotkeyModifiers.Control, ModifierSide.Right),
        Key.LeftAlt => new(HotkeyModifiers.Alt, ModifierSide.Left),
        Key.RightAlt => new(HotkeyModifiers.Alt, ModifierSide.Right),
        Key.LeftShift => new(HotkeyModifiers.Shift, ModifierSide.Left),
        Key.RightShift => new(HotkeyModifiers.Shift, ModifierSide.Right),
        Key.LWin => new(HotkeyModifiers.Windows, ModifierSide.Left),
        Key.RWin => new(HotkeyModifiers.Windows, ModifierSide.Right),
        _ => null,
    };

    /// <summary>Per-textbox capture state accumulated across key-down/up events.</summary>
    private sealed class HotkeyCapture
    {
        /// <summary>Gets the modifiers currently held, in press order.</summary>
        public List<HotkeyModifier> Held { get; } = [];

        /// <summary>Gets or sets a value indicating whether a chord has been committed and input is ignored until release.</summary>
        public bool Finalized { get; set; }

        /// <summary>Clears the capture state so a new chord can be recorded.</summary>
        public void Reset()
        {
            Held.Clear();
            Finalized = false;
        }
    }
}
