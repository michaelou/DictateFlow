using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DictateFlow.Core.Services.Updates;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.ViewModels;

/// <summary>
/// View model backing the "Check for updates" dialog. Renders any
/// <see cref="UpdateCheckResult"/> — update available, up to date or failure — and, when a
/// release page URL is present, opens it in the default browser. It never downloads or
/// installs anything.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly UpdateCheckResult _result;
    private readonly ILogger<UpdateViewModel>? _logger;

    /// <summary>Initializes a new instance of the <see cref="UpdateViewModel"/> class.</summary>
    /// <param name="result">The check outcome to present.</param>
    /// <param name="logger">Receives diagnostic output; optional.</param>
    public UpdateViewModel(UpdateCheckResult result, ILogger<UpdateViewModel>? logger = null)
    {
        _result = result;
        _logger = logger;
    }

    /// <summary>The window title / headline for the current outcome.</summary>
    public string Headline => _result.Status switch
    {
        UpdateCheckStatus.UpdateAvailable => "A new version is available",
        UpdateCheckStatus.UpToDate => "You're up to date",
        _ => "Couldn't check for updates",
    };

    /// <summary>The installed version line.</summary>
    public string CurrentVersionText => $"Current version: {_result.CurrentVersion}";

    /// <summary>The latest version line; empty when the latest version is unknown.</summary>
    public string LatestVersionText => string.IsNullOrWhiteSpace(_result.LatestVersion)
        ? string.Empty
        : $"Latest version: {_result.LatestVersion}";

    /// <summary>Whether a latest-version line should be shown.</summary>
    public bool HasLatestVersion => !string.IsNullOrWhiteSpace(_result.LatestVersion);

    /// <summary>The release notes to display (an update is available and notes were provided).</summary>
    public string ReleaseNotes => string.IsNullOrWhiteSpace(_result.ReleaseNotes)
        ? "(No release notes were provided.)"
        : _result.ReleaseNotes.Trim();

    /// <summary>Whether the release-notes section should be shown.</summary>
    public bool HasReleaseNotes => _result.IsUpdateAvailable;

    /// <summary>A failure message, when the check failed.</summary>
    public string StatusMessage => _result.Message ?? string.Empty;

    /// <summary>Whether the failure message should be shown.</summary>
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(_result.Message);

    /// <summary>Whether the "Open release page" button should be enabled/shown.</summary>
    public bool HasReleaseUrl => !string.IsNullOrWhiteSpace(_result.ReleaseUrl);

    /// <summary>Raised when the dialog should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Opens the GitHub release page in the default browser.</summary>
    [RelayCommand(CanExecute = nameof(HasReleaseUrl))]
    private void OpenReleasePage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_result.ReleaseUrl!) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not open the release page");
        }
    }

    /// <summary>Closes the dialog.</summary>
    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
