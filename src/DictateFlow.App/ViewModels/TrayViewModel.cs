using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DictateFlow.App.Services;
using DictateFlow.Core.Services.Audio;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.ViewModels;

/// <summary>
/// View model backing the tray icon context menu.
/// </summary>
public partial class TrayViewModel : ObservableObject
{
    private readonly IWindowService _windowService;
    private readonly IShutdownService _shutdownService;
    private readonly IDictationController _dictationController;
    private readonly ILogger<TrayViewModel> _logger;

    /// <summary>Initializes a new instance of the <see cref="TrayViewModel"/> class.</summary>
    /// <param name="windowService">Opens application windows.</param>
    /// <param name="shutdownService">Performs clean application shutdown.</param>
    /// <param name="dictationController">Starts and stops dictation recordings.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public TrayViewModel(
        IWindowService windowService,
        IShutdownService shutdownService,
        IDictationController dictationController,
        ILogger<TrayViewModel> logger)
    {
        _windowService = windowService;
        _shutdownService = shutdownService;
        _dictationController = dictationController;
        _logger = logger;
    }

    /// <summary>Toggles a dictation recording: starts one, or stops the one in progress.</summary>
    [RelayCommand]
    private async Task DictateAsync()
    {
        if (_dictationController.IsRecording)
        {
            await _dictationController.StopRecordingAsync();
        }
        else
        {
            await _dictationController.StartRecordingAsync();
        }
    }

    /// <summary>Opens the Settings window.</summary>
    [RelayCommand]
    private void OpenSettings()
        => _windowService.ShowSettingsWindow();

    /// <summary>Opens the History window.</summary>
    [RelayCommand]
    private void OpenHistory()
        => _windowService.ShowHistoryWindow();

    /// <summary>Opens the Cost Dashboard window.</summary>
    [RelayCommand]
    private void OpenCostDashboard()
        => _windowService.ShowCostDashboardWindow();

    /// <summary>Shuts the application down cleanly.</summary>
    [RelayCommand]
    private void Exit()
    {
        _logger.LogInformation("Exit requested from tray menu");
        _shutdownService.Shutdown();
    }
}
