namespace DictateFlow.Core.Services.Audio;

/// <summary>
/// Orchestrates hotkey → recorder → overlay. The dictation entry point: hotkey events,
/// the tray menu and (later) other triggers all funnel through this controller.
/// </summary>
public interface IDictationController
{
    /// <summary>Gets a value indicating whether a dictation recording is in progress.</summary>
    bool IsRecording { get; }

    /// <summary>
    /// Gets the WAV stream of the most recently completed recording, or <see langword="null"/>
    /// when nothing has been captured yet. Owned by the controller (replaced and disposed on
    /// the next capture); in M2 it exists for debugging, in M3 it feeds transcription.
    /// </summary>
    Stream? LastCapture { get; }

    /// <summary>Starts a recording session. A no-op when one is already running.</summary>
    Task StartRecordingAsync();

    /// <summary>
    /// Stops the running recording session and stores the capture in <see cref="LastCapture"/>.
    /// A no-op when nothing is recording.
    /// </summary>
    Task StopRecordingAsync();
}
