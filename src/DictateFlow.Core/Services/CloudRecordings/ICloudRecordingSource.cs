using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.CloudRecordings;

/// <summary>
/// Reads recording blobs from the configured cloud storage container. The implementation
/// reads its connection string, container and prefix from
/// <see cref="Models.CloudRecordingsSettings"/> on every call, so settings changes apply
/// without a restart. All failures surface as <see cref="ProviderException"/> so callers can
/// present a safe message.
/// </summary>
public interface ICloudRecordingSource
{
    /// <summary>
    /// Lists the recording blobs in the configured container (filtered by the configured
    /// prefix), newest first when the service reports modification times.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The recordings currently present in the container.</returns>
    /// <exception cref="ProviderException">Listing failed (bad connection string, missing container, auth, network).</exception>
    Task<IReadOnlyList<CloudRecordingBlob>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Downloads one blob to a local file path, overwriting any existing file.
    /// </summary>
    /// <param name="blobName">The blob name within the container.</param>
    /// <param name="destinationPath">The local file path to write to.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <exception cref="ProviderException">The download failed.</exception>
    Task DownloadToFileAsync(string blobName, string destinationPath, CancellationToken cancellationToken);
}
