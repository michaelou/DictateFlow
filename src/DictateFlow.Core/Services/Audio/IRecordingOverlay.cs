namespace DictateFlow.Core.Services.Audio;

/// <summary>
/// The on-screen dictation overlay as seen by the orchestration layer. Implementations
/// must be callable from any thread and must never steal focus from the foreground app.
/// </summary>
public interface IRecordingOverlay
{
    /// <summary>Shows the overlay in the Listening state.</summary>
    /// <param name="promptMode">
    /// The name of the prompt mode selected for this dictation, shown above the status line;
    /// an empty string hides the mode line.
    /// </param>
    void ShowListening(string promptMode);

    /// <summary>Updates the live input level indicator (0..1).</summary>
    /// <param name="level">Normalized peak level of the latest audio buffer.</param>
    void UpdateLevel(float level);

    /// <summary>
    /// Updates the partial transcript shown while listening (streaming transcription only).
    /// The text replaces any previously shown transcript.
    /// </summary>
    /// <param name="text">The transcript recognized so far.</param>
    void UpdateTranscript(string text);

    /// <summary>Shows the overlay in the Processing state (captured audio is being transcribed).</summary>
    void ShowProcessing();

    /// <summary>Shows the overlay in the Success state (the dictated text was delivered).</summary>
    void ShowSuccess();

    /// <summary>Shows the overlay in the Error state (the dictation pipeline failed).</summary>
    /// <param name="message">
    /// Short user-presentable failure summary shown on the overlay, or <see langword="null"/>
    /// for the generic "Dictation failed" text. Never raw exception text.
    /// </param>
    void ShowError(string? message = null);

    /// <summary>Hides the overlay.</summary>
    void Hide();
}
