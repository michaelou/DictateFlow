using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Audio;

/// <summary>
/// Listens for the global dictation hotkey. Depending on <see cref="RecordingSettings.Mode"/>
/// this is implemented via <c>RegisterHotKey</c> (toggle) or a low-level keyboard hook
/// (push-to-talk, which needs key-up notifications).
/// </summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>Raised when the hotkey chord goes down.</summary>
    event EventHandler? HotkeyPressed;

    /// <summary>Raised when the chord's main key goes up (push-to-talk mode only).</summary>
    event EventHandler? HotkeyReleased;

    /// <summary>
    /// Registers the hotkey from <paramref name="settings"/>, replacing any previous
    /// registration. Re-callable whenever settings change. Registration failures are
    /// logged and surfaced as a tray notification; they never throw.
    /// </summary>
    /// <param name="settings">The recording settings holding hotkey and mode.</param>
    void Apply(RecordingSettings settings);
}
