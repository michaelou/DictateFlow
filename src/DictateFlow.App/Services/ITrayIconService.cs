namespace DictateFlow.App.Services;

/// <summary>
/// Manages the system tray icon: creation, error notifications and disposal.
/// </summary>
public interface ITrayIconService : IDisposable
{
    /// <summary>Creates and shows the tray icon defined in application resources.</summary>
    void Show();

    /// <summary>Shows a non-blocking error balloon/toast from the tray icon.</summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification body.</param>
    void ShowErrorNotification(string title, string message);

    /// <summary>Shows a non-blocking warning balloon/toast from the tray icon.</summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification body.</param>
    void ShowWarningNotification(string title, string message);
}
