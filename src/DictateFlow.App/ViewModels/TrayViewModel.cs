using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DictateFlow.App.Services;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.Prompts;
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
    private readonly ISettingsService _settingsService;
    private readonly IPromptModeStore _promptModeStore;
    private readonly ILogger<TrayViewModel> _logger;

    /// <summary>Initializes a new instance of the <see cref="TrayViewModel"/> class.</summary>
    /// <param name="windowService">Opens application windows.</param>
    /// <param name="shutdownService">Performs clean application shutdown.</param>
    /// <param name="dictationController">Starts and stops dictation recordings.</param>
    /// <param name="settingsService">Reads and persists the active prompt mode.</param>
    /// <param name="promptModeStore">Supplies the available prompt modes.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public TrayViewModel(
        IWindowService windowService,
        IShutdownService shutdownService,
        IDictationController dictationController,
        ISettingsService settingsService,
        IPromptModeStore promptModeStore,
        ILogger<TrayViewModel> logger)
    {
        _windowService = windowService;
        _shutdownService = shutdownService;
        _dictationController = dictationController;
        _settingsService = settingsService;
        _promptModeStore = promptModeStore;
        _logger = logger;
    }

    /// <summary>The prompt modes shown in the tray's "Prompt Mode" submenu; the active one is checked.</summary>
    public ObservableCollection<PromptModeMenuItem> PromptModes { get; } = [];

    /// <summary>
    /// Rebuilds <see cref="PromptModes"/> from the prompts directory and marks the active mode.
    /// Called each time the context menu opens so newly added or edited mode files show up
    /// without a restart.
    /// </summary>
    public void RefreshPromptModes()
    {
        _promptModeStore.Reload();
        var active = _settingsService.Current.ActivePromptMode;

        PromptModes.Clear();
        foreach (var mode in _promptModeStore.GetAll())
        {
            PromptModes.Add(new PromptModeMenuItem(
                mode.Name,
                mode.Description,
                string.Equals(mode.Name, active, StringComparison.OrdinalIgnoreCase),
                SelectPromptModeAsync));
        }
    }

    /// <summary>Makes <paramref name="modeName"/> the active prompt mode and persists it.</summary>
    private async Task SelectPromptModeAsync(string modeName)
    {
        if (string.Equals(_settingsService.Current.ActivePromptMode, modeName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _logger.LogInformation("Active prompt mode changed to '{PromptMode}' from tray menu", modeName);
        _settingsService.Current.ActivePromptMode = modeName;
        await _settingsService.SaveAsync();

        foreach (var item in PromptModes)
        {
            item.IsActive = string.Equals(item.Name, modeName, StringComparison.OrdinalIgnoreCase);
        }
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
