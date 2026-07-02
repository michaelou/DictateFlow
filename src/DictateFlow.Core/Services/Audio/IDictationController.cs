using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Audio;

/// <summary>
/// Orchestrates hotkey → recorder → overlay → transcription. The dictation entry point:
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

    /// <summary>Raised when a capture has been transcribed successfully.</summary>
    event EventHandler<TranscriptionResult>? TranscriptionCompleted;

    /// <summary>Raised when transcription of a capture failed with a user-actionable error.</summary>
    event EventHandler<ProviderException>? TranscriptionFailed;

    /// <summary>Starts a recording session. A no-op when one is already running.</summary>
    Task StartRecordingAsync();

    /// <summary>
    /// Stops the running recording session, stores the capture in <see cref="LastCapture"/>
    /// and transcribes it (raising <see cref="TranscriptionCompleted"/> or
    /// <see cref="TranscriptionFailed"/>). A no-op when nothing is recording.
    /// </summary>
    Task StopRecordingAsync();
}
