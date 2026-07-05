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

    /// <summary>
    /// Shows the overlay in the command-executing state while a recognized voice command runs,
    /// distinct from the normal Processing state (issue #30).
    /// </summary>
    /// <param name="commandName">The display name of the command being executed.</param>
    void ShowCommandExecuting(string commandName);

    /// <summary>Shows the overlay in the command-success state with the command's outcome message (issue #30).</summary>
    /// <param name="message">The user-presentable success message (e.g. <c>Opening Notepad</c>).</param>
    void ShowCommandSuccess(string message);

    /// <summary>
    /// Shows the overlay in the command-error state — a command failed, or the utterance matched
    /// no command and nothing executed — with the outcome message (issue #30).
    /// </summary>
    /// <param name="message">The user-presentable failure or unknown-command message.</param>
    void ShowCommandError(string message);

    /// <summary>Hides the overlay.</summary>
    void Hide();
}
