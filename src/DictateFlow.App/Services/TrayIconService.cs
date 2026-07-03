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
    private Action? _pendingNotificationAction;

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

        _trayIcon.TrayBalloonTipClicked += OnBalloonTipClicked;
        _trayIcon.TrayBalloonTipClosed += OnBalloonTipClosed;

        _trayIcon.ForceCreate();
        _logger.LogDebug("Tray icon created");
    }

    /// <inheritdoc />
    public void ShowErrorNotification(string title, string message, Action? onClick = null)
        => ShowNotificationSafe(title, message, NotificationIcon.Error, onClick);

    /// <inheritdoc />
    public void ShowWarningNotification(string title, string message)
        => ShowNotificationSafe(title, message, NotificationIcon.Warning, onClick: null);

    private void ShowNotificationSafe(string title, string message, NotificationIcon icon, Action? onClick)
    {
        try
        {
            _pendingNotificationAction = onClick;
            _trayIcon?.ShowNotification(title, message, icon);
        }
        catch (Exception ex)
        {
            // Never let a notification take the app down.
            _pendingNotificationAction = null;
            _logger.LogWarning(ex, "Failed to show tray notification");
        }
    }

    private void OnBalloonTipClicked(object sender, System.Windows.RoutedEventArgs e)
    {
        var action = _pendingNotificationAction;
        _pendingNotificationAction = null;
        if (action is null)
        {
            return;
        }

        try
        {
            _logger.LogDebug("Tray notification clicked; running its action");
            action();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tray notification click action failed");
        }
    }

    private void OnBalloonTipClosed(object sender, System.Windows.RoutedEventArgs e)
        => _pendingNotificationAction = null;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.TrayBalloonTipClicked -= OnBalloonTipClicked;
            _trayIcon.TrayBalloonTipClosed -= OnBalloonTipClosed;
        }

        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
