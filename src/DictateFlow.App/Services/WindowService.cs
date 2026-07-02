using System.Windows;
using DictateFlow.App.ViewModels;
using DictateFlow.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DictateFlow.App.Services;

/// <summary>
/// Default <see cref="IWindowService"/> implementation. Tracks the open Settings window
/// so a second request focuses the existing instance instead of creating a new one.
/// </summary>
public sealed class WindowService : IWindowService
{
    private readonly IServiceProvider _serviceProvider;
    private SettingsWindow? _settingsWindow;

    /// <summary>Initializes a new instance of the <see cref="WindowService"/> class.</summary>
    /// <param name="serviceProvider">Used as a factory for window view models.</param>
    public WindowService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public void ShowSettingsWindow()
    {
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.Activate();
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
}
