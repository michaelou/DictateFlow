namespace DictateFlow.Core.Services.Audio;

/// <summary>
/// Orchestrates hotkey → recorder → overlay → dictation pipeline. The dictation entry point:
/// hotkey events, the tray menu and (later) other triggers all funnel through this controller.
/// </summary>
public interface IDictationController
{
    /// <summary>Gets a value indicating whether a dictation recording is in progress.</summary>
    bool IsRecording { get; }

    /// <summary>
    /// Gets the WAV stream of the most recently completed recording, or <see langword="null"/>
    /// when nothing has been captured yet. Owned by the controller (replaced and disposed on
    /// the next capture).
    /// </summary>
    Stream? LastCapture { get; }

    /// <summary>
    /// Raised when the dictation pipeline failed, with a user-presentable message.
    /// Successful dictations end in the target application (and the Success overlay) —
    /// there is no completion event to consume.
    /// </summary>
    event EventHandler<string>? DictationFailed;

    /// <summary>Starts a recording session. A no-op when one is already running.</summary>
    Task StartRecordingAsync();

    /// <summary>
    /// Stops the running recording session, stores the capture in <see cref="LastCapture"/>
    /// and runs it through the dictation pipeline (transcribe → enhance → history → output).
    /// A no-op when nothing is recording.
    /// </summary>
    Task StopRecordingAsync();
}
