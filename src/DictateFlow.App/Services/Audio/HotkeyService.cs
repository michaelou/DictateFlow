using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DictateFlow.App.Interop;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Audio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.Services.Audio;

/// <summary>
/// Global hotkey listener. Toggle mode uses <c>RegisterHotKey</c> against a hidden
/// message-only window (<c>WM_HOTKEY</c>); push-to-talk mode installs a low-level
/// keyboard hook (<c>WH_KEYBOARD_LL</c>) because <c>RegisterHotKey</c> cannot report
/// key-up. <see cref="Apply"/> tears down the previous registration and re-arms, so
/// settings changes take effect without a restart.
/// </summary>
public sealed class HotkeyService : IHotkeyService
{
    private const int HotkeyId = 0xD1C7;

    private readonly ILogger<HotkeyService> _logger;

    // Resolved lazily: TrayIconService (via TrayViewModel → DictationController) depends
    // on this service, so a constructor-injected ITrayIconService would be a cycle.
    private readonly IServiceProvider _serviceProvider;

    // Rooted in a field so the GC cannot collect the delegate while the hook is installed.
    private readonly NativeMethods.LowLevelKeyboardProc _hookProc;

    private readonly HashSet<uint> _pressedModifierKeys = [];

    private Dispatcher? _dispatcher;
    private HwndSource? _hotkeyWindow;
    private IntPtr _hookHandle;
    private HotkeyChord? _chord;
    private bool _mainKeyDown;

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
    public event EventHandler? HotkeyPressed;

    /// <inheritdoc />
    public event EventHandler? HotkeyReleased;

    /// <inheritdoc />
    public void Apply(RecordingSettings settings)
        => OnDispatcher(() => ApplyCore(settings));

    /// <inheritdoc />
    public void Dispose()
        => OnDispatcher(TearDown);

    /// <summary>Runs <paramref name="action"/> on the UI thread — hotkey registration and hooks need its message loop.</summary>
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

        if (!HotkeyParser.TryParse(settings.Hotkey, out var chord))
        {
            _logger.LogWarning("Hotkey '{Hotkey}' could not be parsed; no hotkey is active", settings.Hotkey);
            NotifyHotkeyProblem($"'{settings.Hotkey}' is not a valid hotkey. Fix it in Settings.");
            return;
        }

        _chord = chord;

        if (string.Equals(settings.Mode, RecordingModes.Toggle, StringComparison.OrdinalIgnoreCase))
        {
            RegisterToggleHotkey(chord);
        }
        else
        {
            InstallPushToTalkHook(chord);
        }
    }

    private void RegisterToggleHotkey(HotkeyChord chord)
    {
        var parameters = new HwndSourceParameters("DictateFlowHotkeyWindow")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0,
            ParentWindow = NativeMethods.HwndMessage,
        };

        _hotkeyWindow = new HwndSource(parameters);
        _hotkeyWindow.AddHook(OnWindowMessage);

        if (NativeMethods.RegisterHotKey(_hotkeyWindow.Handle, HotkeyId, (uint)chord.Modifiers, chord.VirtualKey))
        {
            _logger.LogInformation("Toggle hotkey {Chord} registered", chord);
        }
        else
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogWarning("RegisterHotKey failed for {Chord} (Win32 error {Error}); the hotkey may be in use", chord, error);
            NotifyHotkeyProblem($"Could not register '{chord}' — it may be in use by another application.");
        }
    }

    private void InstallPushToTalkHook(HotkeyChord chord)
    {
        _pressedModifierKeys.Clear();
        _mainKeyDown = false;
        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl,
            _hookProc,
            NativeMethods.GetModuleHandle(null),
            0);

        if (_hookHandle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogWarning("SetWindowsHookEx failed (Win32 error {Error}); push-to-talk hotkey is unavailable", error);
            NotifyHotkeyProblem("Could not install the push-to-talk keyboard hook.");
        }
        else
        {
            _logger.LogInformation("Push-to-talk hotkey {Chord} armed via keyboard hook", chord);
        }
    }

    private void TearDown()
    {
        if (_hotkeyWindow is not null)
        {
            NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, HotkeyId);
            _hotkeyWindow.RemoveHook(OnWindowMessage);
            _hotkeyWindow.Dispose();
            _hotkeyWindow = null;
        }

        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        _chord = null;
        _mainKeyDown = false;
        _pressedModifierKeys.Clear();
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && wParam.ToInt64() == HotkeyId)
        {
            RaiseAsync(HotkeyPressed);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private IntPtr OnKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _chord is { } chord)
        {
            var info = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);
            var message = wParam.ToInt64();
            var isDown = message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
            var isUp = message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;

            UpdateModifierState(info.VkCode, isDown);

            if (info.VkCode == chord.VirtualKey)
            {
                if (isDown)
                {
                    // _mainKeyDown filters keyboard auto-repeat: only the first down fires.
                    if (!_mainKeyDown && (PressedModifiers() & chord.Modifiers) == chord.Modifiers)
                    {
                        _mainKeyDown = true;
                        RaiseAsync(HotkeyPressed);
                    }

                    if (_mainKeyDown)
                    {
                        return 1; // swallow so the key does not type into the focused app
                    }
                }
                else if (isUp && _mainKeyDown)
                {
                    _mainKeyDown = false;
                    RaiseAsync(HotkeyReleased);
                    return 1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void UpdateModifierState(uint vkCode, bool isDown)
    {
        // The LL hook reports left/right-specific codes: LSHIFT/RSHIFT (A0/A1),
        // LCTRL/RCTRL (A2/A3), LALT/RALT (A4/A5), LWIN/RWIN (5B/5C).
        var isModifier = vkCode is >= 0xA0 and <= 0xA5 or 0x5B or 0x5C;
        if (!isModifier)
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

    private HotkeyModifiers PressedModifiers()
    {
        var modifiers = HotkeyModifiers.None;
        foreach (var vk in _pressedModifierKeys)
        {
            modifiers |= vk switch
            {
                0xA0 or 0xA1 => HotkeyModifiers.Shift,
                0xA2 or 0xA3 => HotkeyModifiers.Control,
                0xA4 or 0xA5 => HotkeyModifiers.Alt,
                0x5B or 0x5C => HotkeyModifiers.Windows,
                _ => HotkeyModifiers.None,
            };
        }

        return modifiers;
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
}
