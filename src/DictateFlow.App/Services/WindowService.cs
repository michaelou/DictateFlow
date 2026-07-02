using System.Windows;
using DictateFlow.App.ViewModels;
using DictateFlow.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DictateFlow.App.Services;

/// <summary>
/// Default <see cref="IWindowService"/> implementation. Tracks each open window so a
/// second request focuses the existing instance instead of creating a new one.
/// </summary>
public sealed class WindowService : IWindowService
{
    private readonly IServiceProvider _serviceProvider;
    private SettingsWindow? _settingsWindow;
    private HistoryWindow? _historyWindow;
    private CostDashboardWindow? _costDashboardWindow;

    /// <summary>Initializes a new instance of the <see cref="WindowService"/> class.</summary>
    /// <param name="serviceProvider">Used as a factory for window view models.</param>
    public WindowService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public void ShowSettingsWindow()
    {
        if (TryActivate(_settingsWindow))
        {
            return;
        }

        var viewModel = _serviceProvider.GetRequiredService<SettingsViewModel>();
        var window = new SettingsWindow { DataContext = viewModel };
        viewModel.CloseRequested += (_, _) => window.Close();
        window.Closed += (_, _) => _settingsWindow = null;

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

        _costDashboardWindow = window;
        window.Show();
        window.Activate();
        viewModel.RefreshCommand.Execute(null);
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
