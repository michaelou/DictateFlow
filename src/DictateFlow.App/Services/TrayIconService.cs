using System.Windows;
using DictateFlow.App.ViewModels;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.Services;

/// <summary>
/// Default <see cref="ITrayIconService"/> implementation. Materializes the
/// <see cref="TaskbarIcon"/> declared in <c>App.xaml</c> resources, binds its context menu
/// to the <see cref="TrayViewModel"/> and removes the icon on disposal.
/// </summary>
public sealed class TrayIconService : ITrayIconService
{
    private readonly TrayViewModel _viewModel;
    private readonly ILogger<TrayIconService> _logger;
    private TaskbarIcon? _trayIcon;

    /// <summary>Initializes a new instance of the <see cref="TrayIconService"/> class.</summary>
    /// <param name="viewModel">The view model the tray menu commands bind to.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public TrayIconService(TrayViewModel viewModel, ILogger<TrayIconService> logger)
    {
        _viewModel = viewModel;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Show()
    {
        if (_trayIcon is not null)
        {
            return;
        }

        _trayIcon = (TaskbarIcon)Application.Current.FindResource("TrayIcon");
        _trayIcon.DataContext = _viewModel;
        if (_trayIcon.ContextMenu is not null)
        {
            _trayIcon.ContextMenu.DataContext = _viewModel;
        }

        _trayIcon.ForceCreate();
        _logger.LogDebug("Tray icon created");
    }

    /// <inheritdoc />
    public void ShowErrorNotification(string title, string message)
        => ShowNotificationSafe(title, message, NotificationIcon.Error);

    /// <inheritdoc />
    public void ShowWarningNotification(string title, string message)
        => ShowNotificationSafe(title, message, NotificationIcon.Warning);

    private void ShowNotificationSafe(string title, string message, NotificationIcon icon)
    {
        try
        {
            _trayIcon?.ShowNotification(title, message, icon);
        }
        catch (Exception ex)
        {
            // Never let a notification take the app down.
            _logger.LogWarning(ex, "Failed to show tray notification");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
