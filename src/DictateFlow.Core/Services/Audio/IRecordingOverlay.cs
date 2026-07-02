namespace DictateFlow.Core.Services.Audio;

/// <summary>
/// The on-screen dictation overlay as seen by the orchestration layer. Implementations
/// must be callable from any thread and must never steal focus from the foreground app.
/// </summary>
public interface IRecordingOverlay
{
    /// <summary>Shows the overlay in the Listening state.</summary>
    void ShowListening();

    /// <summary>Updates the live input level indicator (0..1).</summary>
    /// <param name="level">Normalized peak level of the latest audio buffer.</param>
    void UpdateLevel(float level);

    /// <summary>Shows the overlay in the Processing state (captured audio is being transcribed).</summary>
    void ShowProcessing();

    /// <summary>Shows the overlay in the Error state (the dictation pipeline failed).</summary>
    void ShowError();

    /// <summary>Hides the overlay.</summary>
    void Hide();
}
