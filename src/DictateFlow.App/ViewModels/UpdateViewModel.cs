using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DictateFlow.App.Services;
using DictateFlow.Core.Services.Updates;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.ViewModels;

/// <summary>
/// View model backing the "Check for updates" dialog. Renders any
/// <see cref="UpdateCheckResult"/> — update available, up to date or failure. When the latest
/// release ships an installer it can download it (with progress) and launch the setup wizard,
/// closing the app so the wizard can replace it; otherwise it falls back to opening the
/// release page in the browser.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly UpdateCheckResult _result;
    private readonly IUpdateDownloader? _downloader;
    private readonly IShutdownService? _shutdownService;
    private readonly ILogger<UpdateViewModel>? _logger;

    /// <summary>Initializes a new instance of the <see cref="UpdateViewModel"/> class.</summary>
    /// <param name="result">The check outcome to present.</param>
    /// <param name="downloader">Downloads the installer for the "Download &amp; install" action; optional.</param>
    /// <param name="shutdownService">Closes the app after launching the installer; optional.</param>
    /// <param name="logger">Receives diagnostic output; optional.</param>
    public UpdateViewModel(
        UpdateCheckResult result,
        IUpdateDownloader? downloader = null,
        IShutdownService? shutdownService = null,
        ILogger<UpdateViewModel>? logger = null)
    {
        _result = result;
        _downloader = downloader;
        _shutdownService = shutdownService;
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

    /// <summary>
    /// Whether the "Download &amp; install" button should be shown: the latest release has an
    /// installer asset and the app was given the services needed to download and relaunch.
    /// </summary>
    public bool CanDownloadInstall => _result.HasInstaller && _downloader is not null && _shutdownService is not null;

    /// <summary>Whether an installer download is currently in progress.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadAndInstallCommand))]
    private bool _isDownloading;

    /// <summary>Download progress as a percentage (0–100) while <see cref="IsDownloading"/> is true.</summary>
    [ObservableProperty]
    private double _downloadProgress;

    /// <summary>A short status line shown during and after a download (progress or an error).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDownloadStatus))]
    private string _downloadStatus = string.Empty;

    /// <summary>Whether the download status line should be shown.</summary>
    public bool HasDownloadStatus => !string.IsNullOrWhiteSpace(DownloadStatus);

    /// <summary>Raised when the dialog should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Whether the "Download &amp; install" command may run right now.</summary>
    private bool CanExecuteDownloadInstall => CanDownloadInstall && !IsDownloading;

    /// <summary>
    /// Downloads the installer with progress, launches the setup wizard, and closes the app so
    /// the wizard can overwrite the running files. On failure the error is surfaced and the
    /// "Open release page" fallback remains available.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExecuteDownloadInstall))]
    private async Task DownloadAndInstallAsync()
    {
        if (_downloader is null || _shutdownService is null)
        {
            return;
        }

        IsDownloading = true;
        DownloadProgress = 0;
        DownloadStatus = "Starting download…";

        try
        {
            var progress = new Progress<double>(fraction =>
            {
                DownloadProgress = fraction * 100;
                DownloadStatus = $"Downloading… {DownloadProgress:0}%";
            });

            var installerPath = await _downloader
                .DownloadAsync(_result.InstallerUrl!, _result.InstallerSize, progress)
                .ConfigureAwait(true);

            DownloadStatus = "Launching installer…";
            Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });

            _logger?.LogInformation("Installer launched; shutting down for update");
            _shutdownService.Shutdown();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Downloading or launching the installer failed");
            IsDownloading = false;
            DownloadStatus = "The update could not be downloaded. Use \"Open release page\" to download it manually.";
        }
    }

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
