namespace DictateFlow.App.Services.CloudRecordings;

/// <summary>
/// Shows a persistent bottom-right notification when new cloud recordings are transcribed. The
/// notification stays on screen until the user clicks it (which opens the Cloud Recordings review
/// window) or dismisses it, unlike the transient tray balloons.
/// </summary>
public interface ICloudRecordingNotifier
{
    /// <summary>
    /// Shows (or refreshes) the new-recordings notification. Safe to call from any thread; the
    /// work is marshalled onto the UI dispatcher.
    /// </summary>
    /// <param name="count">The number of recordings newly transcribed in the last check.</param>
    void ShowNewRecordings(int count);
}
