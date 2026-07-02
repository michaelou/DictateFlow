using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DictateFlow.Core.Services;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.ViewModels;

/// <summary>
/// View model backing the Settings window shell. For M1 it exposes the section navigation
/// skeleton plus Save/Cancel wired to <see cref="ISettingsService"/>; the section pages are
/// filled in by later milestones.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<SettingsViewModel> _logger;

    /// <summary>Initializes a new instance of the <see cref="SettingsViewModel"/> class.</summary>
    /// <param name="settingsService">Persists and reloads application settings.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public SettingsViewModel(ISettingsService settingsService, ILogger<SettingsViewModel> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <summary>Gets the navigation sections shown on the left side of the window.</summary>
    public IReadOnlyList<string> Sections { get; } =
        ["General", "Speech", "LLM", "Prompts", "Output", "History"];

    /// <summary>Gets or sets the currently selected navigation section.</summary>
    [ObservableProperty]
    private string _selectedSection = "General";

    /// <summary>Raised when the window hosting this view model should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Persists the current settings and closes the window.</summary>
    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await _settingsService.SaveAsync(cancellationToken);
        _logger.LogInformation("Settings saved from Settings window");
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Discards unsaved edits by reloading settings from disk, then closes the window.</summary>
    [RelayCommand]
    private async Task CancelAsync(CancellationToken cancellationToken)
    {
        await _settingsService.LoadAsync(cancellationToken);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
