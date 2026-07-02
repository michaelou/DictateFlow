using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DictateFlow.App.Services;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.ViewModels;

/// <summary>
/// View model backing the tray icon context menu. Dictate, History and Cost Dashboard
/// are stubs until later milestones (M2+, M6).
/// </summary>
public partial class TrayViewModel : ObservableObject
{
    private readonly IWindowService _windowService;
    private readonly IShutdownService _shutdownService;
    private readonly ILogger<TrayViewModel> _logger;

    /// <summary>Initializes a new instance of the <see cref="TrayViewModel"/> class.</summary>
    /// <param name="windowService">Opens application windows.</param>
    /// <param name="shutdownService">Performs clean application shutdown.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public TrayViewModel(IWindowService windowService, IShutdownService shutdownService, ILogger<TrayViewModel> logger)
    {
        _windowService = windowService;
        _shutdownService = shutdownService;
        _logger = logger;
    }

    /// <summary>Starts a dictation session (stub until M2).</summary>
    [RelayCommand]
    private void Dictate()
        => _logger.LogInformation("Dictate requested — not implemented yet (arrives with M2/M3)");

    /// <summary>Opens the Settings window.</summary>
    [RelayCommand]
    private void OpenSettings()
        => _windowService.ShowSettingsWindow();

    /// <summary>Opens the dictation history (stub until M6).</summary>
    [RelayCommand]
    private void OpenHistory()
        => _logger.LogInformation("History requested — not implemented yet (arrives with M6)");

    /// <summary>Opens the cost dashboard (stub until M6).</summary>
    [RelayCommand]
    private void OpenCostDashboard()
        => _logger.LogInformation("Cost Dashboard requested — not implemented yet (arrives with M6)");

    /// <summary>Shuts the application down cleanly.</summary>
    [RelayCommand]
    private void Exit()
    {
        _logger.LogInformation("Exit requested from tray menu");
        _shutdownService.Shutdown();
    }
}
