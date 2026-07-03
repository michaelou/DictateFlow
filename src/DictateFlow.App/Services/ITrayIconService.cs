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
    /// <param name="onClick">
    /// Optional action invoked when the user clicks the notification (e.g. opening the
    /// Settings window for configuration errors). Only the most recent notification's
    /// action is armed at any time.
    /// </param>
    void ShowErrorNotification(string title, string message, Action? onClick = null);

    /// <summary>Shows a non-blocking warning balloon/toast from the tray icon.</summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification body.</param>
    /// <param name="onClick"><inheritdoc cref="ShowErrorNotification" path="/param[@name='onClick']"/></param>
    void ShowWarningNotification(string title, string message, Action? onClick = null);

    /// <summary>Shows a non-blocking informational balloon/toast from the tray icon.</summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification body.</param>
    /// <param name="onClick"><inheritdoc cref="ShowErrorNotification" path="/param[@name='onClick']"/></param>
    void ShowInfoNotification(string title, string message, Action? onClick = null);
}
