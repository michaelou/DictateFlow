using DictateFlow.Core.Services.Updates;

namespace DictateFlow.App.Services;

/// <summary>
/// Opens application windows, enforcing single-instance behavior where required.
/// </summary>
public interface IWindowService
{
    /// <summary>
    /// Shows the Settings window. If it is already open, the existing instance is
    /// restored and focused instead of creating a second one.
    /// </summary>
    void ShowSettingsWindow();

    /// <summary>
    /// Shows the Settings window scrolled to <paramref name="section"/>. If it is already open,
    /// the existing instance is focused and switched to that section.
    /// </summary>
    /// <param name="section">The navigation section to select (e.g. <c>Voice Commands</c>).</param>
    void ShowSettingsWindow(string section);

    /// <summary>
    /// Shows the History window. If it is already open, the existing instance is
    /// restored and focused instead of creating a second one.
    /// </summary>
    void ShowHistoryWindow();

    /// <summary>
    /// Shows the Cost Dashboard window. If it is already open, the existing instance is
    /// restored and focused instead of creating a second one.
    /// </summary>
    void ShowCostDashboardWindow();

    /// <summary>
    /// Shows the DictatePad window. If it is already open, the existing instance is
    /// restored and focused instead of creating a second one.
    /// </summary>
    void ShowDictatePadWindow();

    /// <summary>
    /// Shows the Cloud Recordings review window. If it is already open, the existing instance
    /// is restored and focused instead of creating a second one.
    /// </summary>
    void ShowCloudRecordingsWindow();

    /// <summary>
    /// Shows the "Check for updates" dialog for the given check <paramref name="result"/>.
    /// If it is already open, the existing instance is replaced with the new result.
    /// </summary>
    /// <param name="result">The update-check outcome to present.</param>
    void ShowUpdateWindow(UpdateCheckResult result);

    /// <summary>
    /// If the DictatePad is open, captures its current text and persists settings
    /// synchronously. Called on app shutdown so the scratchpad survives even when the process
    /// exits before the window's normal close-time save can flush. A no-op when the pad is closed.
    /// </summary>
    void SaveDictatePadState();
}
