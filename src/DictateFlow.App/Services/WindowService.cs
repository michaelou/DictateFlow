using System.Windows;
using DictateFlow.App.ViewModels;
using DictateFlow.App.Views;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Updates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.Services;

/// <summary>
/// Default <see cref="IWindowService"/> implementation. Tracks each open window so a
/// second request focuses the existing instance instead of creating a new one, and
/// remembers window size/position across sessions (persisted in settings under
/// <c>WindowState</c>).
/// </summary>
public sealed class WindowService : IWindowService
{
    private readonly IServiceProvider _serviceProvider;
    private SettingsWindow? _settingsWindow;
    private HistoryWindow? _historyWindow;
    private CostDashboardWindow? _costDashboardWindow;
    private DictatePadWindow? _dictatePadWindow;
    private UpdateWindow? _updateWindow;

    /// <summary>Initializes a new instance of the <see cref="WindowService"/> class.</summary>
    /// <param name="serviceProvider">Used as a factory for window view models.</param>
    public WindowService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public void ShowSettingsWindow() => OpenSettings(section: null);

    /// <inheritdoc />
    public void ShowSettingsWindow(string section) => OpenSettings(section);

    /// <summary>Shared implementation: opens or focuses Settings, optionally selecting a section.</summary>
    /// <param name="section">The navigation section to select, or <see langword="null"/> to keep the current one.</param>
    private void OpenSettings(string? section)
    {
        if (TryActivate(_settingsWindow))
        {
            if (section is not null && _settingsWindow!.DataContext is SettingsViewModel existing)
            {
                existing.SelectedSection = section;
            }

            return;
        }

        var viewModel = _serviceProvider.GetRequiredService<SettingsViewModel>();
        if (section is not null)
        {
            viewModel.SelectedSection = section;
        }

        var window = new SettingsWindow { DataContext = viewModel };
        viewModel.CloseRequested += (_, _) => window.Close();
        window.Closed += (_, _) => _settingsWindow = null;
        TrackPlacement(window, "Settings");

        _settingsWindow = window;
        window.Show();
        window.Activate();
    }

    /// <inheritdoc />
    public void ShowHistoryWindow()
    {
        if (TryActivate(_historyWindow))
        {
            return;
        }

        var viewModel = _serviceProvider.GetRequiredService<HistoryViewModel>();
        var window = new HistoryWindow { DataContext = viewModel };
        window.Closed += (_, _) => _historyWindow = null;
        TrackPlacement(window, "History");

        _historyWindow = window;
        window.Show();
        window.Activate();
        viewModel.RefreshCommand.Execute(null);
    }

    /// <inheritdoc />
    public void ShowCostDashboardWindow()
    {
        if (TryActivate(_costDashboardWindow))
        {
            return;
        }

        var viewModel = _serviceProvider.GetRequiredService<CostDashboardViewModel>();
        var window = new CostDashboardWindow { DataContext = viewModel };
        window.Closed += (_, _) => _costDashboardWindow = null;
        TrackPlacement(window, "CostDashboard");

        _costDashboardWindow = window;
        window.Show();
        window.Activate();
        viewModel.RefreshCommand.Execute(null);
    }

    /// <inheritdoc />
    public void ShowDictatePadWindow()
    {
        if (TryActivate(_dictatePadWindow))
        {
            return;
        }

        var viewModel = _serviceProvider.GetRequiredService<DictatePadViewModel>();
        var window = new DictatePadWindow { DataContext = viewModel };
        window.Closed += (_, _) => _dictatePadWindow = null;
        TrackPlacement(window, "DictatePad");

        _dictatePadWindow = window;
        window.Show();
        window.Activate();
    }

    /// <inheritdoc />
    public void ShowUpdateWindow(UpdateCheckResult result)
    {
        // A fresh result supersedes any dialog already open, so close and rebuild it.
        _updateWindow?.Close();

        var viewModel = new UpdateViewModel(
            result,
            _serviceProvider.GetService<IUpdateDownloader>(),
            _serviceProvider.GetService<IShutdownService>(),
            _serviceProvider.GetService<ILogger<UpdateViewModel>>());
        var window = new UpdateWindow { DataContext = viewModel };
        viewModel.CloseRequested += (_, _) => window.Close();
        window.Closed += (_, _) => _updateWindow = null;

        _updateWindow = window;
        window.Show();
        window.Activate();
    }

    /// <summary>
    /// Restores the window's remembered placement (when it is still on-screen) and saves
    /// the placement back to settings when the window closes.
    /// </summary>
    /// <param name="window">The window to track; must not be shown yet.</param>
    /// <param name="key">The stable name the placement is stored under.</param>
    private void TrackPlacement(Window window, string key)
    {
        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();

        if (settingsService.Current.WindowState.TryGetValue(key, out var placement)
            && IsOnScreen(placement))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = placement.Left;
            window.Top = placement.Top;
            window.Width = placement.Width;
            window.Height = placement.Height;
        }

        window.Closing += (_, _) =>
        {
            // RestoreBounds gives the normal-state rectangle even when closing maximized/minimized.
            var bounds = window.WindowState == WindowState.Normal
                ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
                : window.RestoreBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0 || double.IsNaN(bounds.Left) || double.IsNaN(bounds.Top))
            {
                return;
            }

            settingsService.Current.WindowState[key] = new WindowPlacement
            {
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
            };

            // Fire-and-forget: a failed placement write must never block closing the window.
            _ = SavePlacementAsync(settingsService);
        };
    }

    private async Task SavePlacementAsync(ISettingsService settingsService)
    {
        try
        {
            await settingsService.SaveAsync();
        }
        catch (Exception ex)
        {
            _serviceProvider.GetRequiredService<ILogger<WindowService>>()
                .LogWarning(ex, "Could not persist the window placement");
        }
    }

    /// <summary>Whether a remembered placement still intersects the virtual screen (monitors change).</summary>
    private static bool IsOnScreen(WindowPlacement placement)
    {
        if (placement.Width <= 0 || placement.Height <= 0)
        {
            return false;
        }

        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        return virtualScreen.IntersectsWith(
            new Rect(placement.Left, placement.Top, placement.Width, placement.Height));
    }

    /// <summary>Restores and focuses an already-open window; <see langword="false"/> when there is none.</summary>
    private static bool TryActivate(Window? window)
    {
        if (window is null)
        {
            return false;
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        return true;
    }
}
