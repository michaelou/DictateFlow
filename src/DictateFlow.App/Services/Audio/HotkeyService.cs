using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using DictateFlow.App.Interop;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Audio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.Services.Audio;

/// <summary>
/// Global hotkey listener. Both the toggle and push-to-talk hotkeys are watched through a
/// single low-level keyboard hook (<c>WH_KEYBOARD_LL</c>): the hook is the only mechanism that
/// reports key-up (needed by push-to-talk) and left/right-specific modifier keys (needed for
/// side-specific and modifier-only chords, which <c>RegisterHotKey</c> cannot express). Both
/// chords are matched independently on every key event; either is skipped when its hotkey is
/// empty. <see cref="Apply"/> tears down the previous registration and re-arms, so settings
/// changes take effect without a restart.
/// </summary>
/// <remarks>
/// Only a main key is ever swallowed (so it does not type into the focused app). Modifier keys
/// are never swallowed — their key-down reaches the OS before a chord can complete, so swallowing
/// the matching key-up would strand the modifier "down". A consequence is that modifier-only
/// chords (e.g. <c>Ctrl+Win</c>) are detected but not suppressed: any incidental system behavior
/// of that combination still occurs.
/// </remarks>
public sealed class HotkeyService : IHotkeyService
{
    private readonly ILogger<HotkeyService> _logger;

    // Resolved lazily: TrayIconService (via TrayViewModel → DictationController) depends
    // on this service, so a constructor-injected ITrayIconService would be a cycle.
    private readonly IServiceProvider _serviceProvider;

    // Rooted in a field so the GC cannot collect the delegate while the hook is installed.
    private readonly NativeMethods.LowLevelKeyboardProc _hookProc;

    private readonly HashSet<uint> _pressedModifierKeys = [];

    private Dispatcher? _dispatcher;
    private IntPtr _hookHandle;
    private ChordState? _toggleState;
    private ChordState? _pttState;

    /// <summary>Initializes a new instance of the <see cref="HotkeyService"/> class.</summary>
    /// <param name="serviceProvider">Used to resolve the tray icon service for failure notifications.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public HotkeyService(IServiceProvider serviceProvider, ILogger<HotkeyService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _hookProc = OnKeyboardHook;
    }

    /// <inheritdoc />
    public event EventHandler? TogglePressed;

    /// <inheritdoc />
    public event EventHandler? PushToTalkPressed;

    /// <inheritdoc />
    public event EventHandler? PushToTalkReleased;

    /// <inheritdoc />
    public void Apply(RecordingSettings settings)
        => OnDispatcher(() => ApplyCore(settings));

    /// <inheritdoc />
    public void Dispose()
        => OnDispatcher(TearDown);

    /// <summary>Runs <paramref name="action"/> on the UI thread — the keyboard hook needs its message loop.</summary>
    private void OnDispatcher(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    private void ApplyCore(RecordingSettings settings)
    {
        TearDown();
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        // The two hotkeys are independent: a failure or empty value in one never affects the other.
        _toggleState = BuildState(settings.ToggleHotkey, "toggle", isToggle: true);
        _pttState = BuildState(settings.PushToTalkHotkey, "push-to-talk", isToggle: false);

        if (_toggleState is not null || _pttState is not null)
        {
            InstallHook();
        }
    }

    /// <summary>Parses <paramref name="hotkey"/> into a chord state; empty is skipped, unparseable is reported.</summary>
    private ChordState? BuildState(string? hotkey, string label, bool isToggle)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return null;
        }

        if (!HotkeyParser.TryParse(hotkey, out var chord))
        {
            _logger.LogWarning("The {Label} hotkey '{Hotkey}' could not be parsed; it is not active", label, hotkey);
            NotifyHotkeyProblem($"'{hotkey}' is not a valid {label} hotkey. Fix it in Settings.");
            return null;
        }

        _logger.LogInformation("{Label} hotkey {Chord} armed via keyboard hook", label, chord);
        return new ChordState(chord, isToggle);
    }

    private void InstallHook()
    {
        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl,
            _hookProc,
            NativeMethods.GetModuleHandle(null),
            0);

        if (_hookHandle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogWarning("SetWindowsHookEx failed (Win32 error {Error}); hotkeys are unavailable", error);
            NotifyHotkeyProblem("Could not install the keyboard hook; hotkeys are unavailable.");
        }
    }

    private void TearDown()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        _toggleState = null;
        _pttState = null;
        _pressedModifierKeys.Clear();
    }

    private IntPtr OnKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);
            var message = wParam.ToInt64();
            var isDown = message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
            var isUp = message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;

            if (isDown || isUp)
            {
                UpdateModifierState(info.VkCode, isDown);

                // Evaluate both chords; either may want to swallow this key so it does not
                // reach the focused application.
                var swallow = ProcessChord(_toggleState, info.VkCode, isDown, isUp);
                swallow |= ProcessChord(_pttState, info.VkCode, isDown, isUp);
                if (swallow)
                {
                    return 1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    /// <summary>
    /// Advances one chord's state for the current key event and returns whether the event should
    /// be swallowed (kept from the focused application).
    /// </summary>
    private bool ProcessChord(ChordState? state, uint vkCode, bool isDown, bool isUp)
    {
        if (state is null)
        {
            return false;
        }

        var chord = state.Chord;

        // Track the main key's up/down state for main-key chords.
        if (chord.VirtualKey is { } mainKey && vkCode == mainKey)
        {
            if (isDown)
            {
                state.MainKeyDown = true;
            }
            else if (isUp)
            {
                state.MainKeyDown = false;
            }
        }

        var satisfied = IsSatisfied(state);

        // Swallow only the chord's main key, so it never types into the focused app. Modifier
        // keys are deliberately never swallowed: their key-down was already delivered before the
        // chord completed, so swallowing the matching key-up would leave a stuck modifier.
        // Modifier-only chords therefore pass through (best-effort — see class remarks).
        var swallow = !chord.IsModifierOnly && chord.VirtualKey == vkCode;

        if (satisfied && !state.Active)
        {
            state.Active = true;
            Raise(state, pressed: true);
            return swallow;
        }

        if (!satisfied && state.Active)
        {
            state.Active = false;
            Raise(state, pressed: false);
            return swallow;
        }

        // While engaged, swallow the main key's auto-repeat so it does not type.
        return state.Active && swallow;
    }

    private bool IsSatisfied(ChordState state)
    {
        var chord = state.Chord;
        if (chord.IsModifierOnly)
        {
            // Exact match so extra modifiers do not fire a modifier-only chord.
            return HotkeyMatcher.ModifiersSatisfied(chord, _pressedModifierKeys, exact: true);
        }

        return state.MainKeyDown
            && HotkeyMatcher.ModifiersSatisfied(chord, _pressedModifierKeys, exact: false);
    }

    private void Raise(ChordState state, bool pressed)
    {
        if (state.IsToggle)
        {
            // Toggle fires only on the rising edge; there is no release event.
            if (pressed)
            {
                RaiseAsync(TogglePressed);
            }

            return;
        }

        RaiseAsync(pressed ? PushToTalkPressed : PushToTalkReleased);
    }

    private void UpdateModifierState(uint vkCode, bool isDown)
    {
        if (!HotkeyMatcher.IsModifierKey(vkCode))
        {
            return;
        }

        if (isDown)
        {
            _pressedModifierKeys.Add(vkCode);
        }
        else
        {
            _pressedModifierKeys.Remove(vkCode);
        }
    }

    /// <summary>
    /// Raises a hotkey event asynchronously on the UI thread. The hook callback must
    /// return within milliseconds or Windows silently removes the hook, so subscribers
    /// are never invoked inline.
    /// </summary>
    private void RaiseAsync(EventHandler? handler)
    {
        if (handler is null)
        {
            return;
        }

        if (_dispatcher is not null)
        {
            _dispatcher.BeginInvoke(() => handler(this, EventArgs.Empty));
        }
        else
        {
            handler(this, EventArgs.Empty);
        }
    }

    private void NotifyHotkeyProblem(string message)
    {
        try
        {
            _serviceProvider.GetService<ITrayIconService>()?.ShowWarningNotification("DictateFlow hotkey", message);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not show hotkey warning notification");
        }
    }

    /// <summary>Per-chord runtime state tracked across key events.</summary>
    private sealed class ChordState(HotkeyChord chord, bool isToggle)
    {
        /// <summary>Gets the chord being watched.</summary>
        public HotkeyChord Chord { get; } = chord;

        /// <summary>Gets a value indicating whether this is the toggle chord (fires once on press) rather than push-to-talk.</summary>
        public bool IsToggle { get; } = isToggle;

        /// <summary>Gets or sets a value indicating whether the chord's main key is currently held (main-key chords only).</summary>
        public bool MainKeyDown { get; set; }

        /// <summary>Gets or sets a value indicating whether the chord is currently engaged (fired, awaiting release).</summary>
        public bool Active { get; set; }
    }
}
