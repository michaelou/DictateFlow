namespace DictateFlow.Core.Services.Updates;

/// <summary>
/// Downloads a release installer asset to a local temporary file so the app can launch it.
/// Separate from <see cref="IUpdateService"/> (which only reports what is available) because
/// downloading is an explicit, user-initiated action with its own progress and failure modes.
/// </summary>
public interface IUpdateDownloader
{
    /// <summary>
    /// Downloads the installer at <paramref name="url"/> to a temporary file and returns its
    /// full path. Verifies the finished file against <paramref name="expectedSize"/> to catch a
    /// truncated download.
    /// </summary>
    /// <param name="url">The direct installer download URL (from <see cref="UpdateCheckResult.InstallerUrl"/>).</param>
    /// <param name="expectedSize">
    /// The expected size in bytes (from <see cref="UpdateCheckResult.InstallerSize"/>). Used to
    /// report progress and to verify the download; pass <c>0</c> to skip the size check.
    /// </param>
    /// <param name="progress">Receives download progress as a fraction from 0 to 1; may be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The full path of the downloaded installer.</returns>
    /// <exception cref="HttpRequestException">The download could not be completed.</exception>
    /// <exception cref="InvalidOperationException">The finished file did not match <paramref name="expectedSize"/>.</exception>
    Task<string> DownloadAsync(
        string url,
        long expectedSize,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default);
}
