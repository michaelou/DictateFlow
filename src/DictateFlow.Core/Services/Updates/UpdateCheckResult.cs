namespace DictateFlow.Core.Services.Updates;

/// <summary>The outcome of a manual update check.</summary>
public enum UpdateCheckStatus
{
    /// <summary>The installed version is the latest published release.</summary>
    UpToDate,

    /// <summary>A newer release is available on GitHub.</summary>
    UpdateAvailable,

    /// <summary>The check could not be completed (offline, network error, GitHub unavailable).</summary>
    Failed,
}

/// <summary>
/// The result of an <see cref="IUpdateService.CheckForUpdatesAsync"/> call. Immutable and
/// self-describing so the UI can render every outcome — update available, up to date and
/// failure — from one object.
/// </summary>
public sealed record UpdateCheckResult
{
    /// <summary>The outcome of the check.</summary>
    public required UpdateCheckStatus Status { get; init; }

    /// <summary>The currently installed application version.</summary>
    public string CurrentVersion { get; init; } = "";

    /// <summary>The latest published version (without a leading <c>v</c>), when known.</summary>
    public string? LatestVersion { get; init; }

    /// <summary>The latest release's notes (GitHub release body), when available.</summary>
    public string? ReleaseNotes { get; init; }

    /// <summary>The URL of the latest release page, when available.</summary>
    public string? ReleaseUrl { get; init; }

    /// <summary>
    /// The direct download URL of the installer asset (<c>DictateFlowSetup-v*.exe</c>) for the
    /// latest release, when the release publishes one. <see langword="null"/> means the app can
    /// only link to the release page (e.g. a release with no installer asset, or a portable
    /// install). Enables the in-app "Download &amp; install" action.
    /// </summary>
    public string? InstallerUrl { get; init; }

    /// <summary>The installer asset's size in bytes, used for download progress and to verify
    /// the download completed; <c>0</c> when <see cref="InstallerUrl"/> is <see langword="null"/>.</summary>
    public long InstallerSize { get; init; }

    /// <summary>A human-readable message describing a failure; <see langword="null"/> on success.</summary>
    public string? Message { get; init; }

    /// <summary>Whether a newer release is available.</summary>
    public bool IsUpdateAvailable => Status == UpdateCheckStatus.UpdateAvailable;

    /// <summary>Whether the latest release exposes an installer asset the app can download.</summary>
    public bool HasInstaller => IsUpdateAvailable && !string.IsNullOrWhiteSpace(InstallerUrl);

    /// <summary>Creates an "up to date" result.</summary>
    public static UpdateCheckResult UpToDate(string currentVersion, string? latestVersion) => new()
    {
        Status = UpdateCheckStatus.UpToDate,
        CurrentVersion = currentVersion,
        LatestVersion = latestVersion,
    };

    /// <summary>Creates an "update available" result.</summary>
    public static UpdateCheckResult Available(
        string currentVersion,
        string latestVersion,
        string? releaseNotes,
        string? releaseUrl,
        string? installerUrl = null,
        long installerSize = 0) => new()
    {
        Status = UpdateCheckStatus.UpdateAvailable,
        CurrentVersion = currentVersion,
        LatestVersion = latestVersion,
        ReleaseNotes = releaseNotes,
        ReleaseUrl = releaseUrl,
        InstallerUrl = installerUrl,
        InstallerSize = installerSize,
    };

    /// <summary>Creates a "failed" result carrying a user-facing <paramref name="message"/>.</summary>
    public static UpdateCheckResult Failed(string currentVersion, string message) => new()
    {
        Status = UpdateCheckStatus.Failed,
        CurrentVersion = currentVersion,
        Message = message,
    };
}
