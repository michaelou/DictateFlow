using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Audio;

/// <summary>
/// Listens for the global dictation hotkeys. The push-to-talk and toggle hotkeys are
/// independent and both active at once: toggle uses <c>RegisterHotKey</c>, push-to-talk uses a
/// low-level keyboard hook (it needs key-up notifications). Either can be disabled by leaving
/// its hotkey empty.
/// </summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>Raised when the toggle hotkey is pressed.</summary>
    event EventHandler? TogglePressed;

    /// <summary>Raised when the push-to-talk hotkey chord goes down.</summary>
    event EventHandler? PushToTalkPressed;

    /// <summary>Raised when the push-to-talk chord's main key goes up.</summary>
    event EventHandler? PushToTalkReleased;

    /// <summary>
    /// Registers the hotkeys from <paramref name="settings"/>, replacing any previous
    /// registration. Re-callable whenever settings change. Registration failures are
    /// logged and surfaced as a tray notification; they never throw.
    /// </summary>
    /// <param name="settings">The recording settings holding both hotkeys.</param>
    void Apply(RecordingSettings settings);
}
