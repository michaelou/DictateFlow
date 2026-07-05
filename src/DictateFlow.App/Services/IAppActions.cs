namespace DictateFlow.App.Services;

/// <summary>
/// The internal application operations that both the tray menu and voice commands invoke, so
/// the two surfaces run the same code. Every member marshals to the UI thread, so callers on a
/// background thread (the voice command pipeline) can invoke them directly.
/// </summary>
public interface IAppActions
{
    /// <summary>Opens (or focuses) the Settings window.</summary>
    void OpenSettings();

    /// <summary>Opens (or focuses) the Settings window scrolled to <paramref name="section"/>.</summary>
    /// <param name="section">The navigation section to select (e.g. <c>Voice Commands</c>).</param>
    void OpenSettings(string section);

    /// <summary>Opens (or focuses) the History window.</summary>
    void ShowHistory();

    /// <summary>Opens (or focuses) the Cost Dashboard window.</summary>
    void OpenCostDashboard();

    /// <summary>
    /// Checks GitHub for a newer release and shows the result dialog. The check never throws;
    /// offline/network failures come back as a graceful message in the dialog.
    /// </summary>
    Task CheckForUpdatesAsync();
}
