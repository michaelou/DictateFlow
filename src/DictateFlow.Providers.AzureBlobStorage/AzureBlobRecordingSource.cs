using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.CloudRecordings;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Providers.AzureBlobStorage;

/// <summary>
/// <see cref="ICloudRecordingSource"/> backed by Azure Blob Storage. Reads the connection
/// string, container name and prefix from <see cref="CloudRecordingsSettings"/> on every call
/// so settings changes apply without a restart, and maps all storage failures to
/// <see cref="ProviderException"/> so callers can present a safe message. Blobs are only read —
/// nothing in the container is modified.
/// </summary>
public sealed class AzureBlobRecordingSource : ICloudRecordingSource
{
    /// <summary>Name used in <see cref="ProviderException"/> messages.</summary>
    private const string ProviderName = "AzureBlobStorage";

    /// <summary>Only audio recordings are considered; the mobile app uploads <c>.m4a</c>.</summary>
    private const string RecordingExtension = ".m4a";

    private readonly ISettingsService _settingsService;
    private readonly ILogger<AzureBlobRecordingSource> _logger;

    /// <summary>Initializes a new instance of the <see cref="AzureBlobRecordingSource"/> class.</summary>
    /// <param name="settingsService">Supplies the connection string, container and prefix, read per call.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public AzureBlobRecordingSource(ISettingsService settingsService, ILogger<AzureBlobRecordingSource> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CloudRecordingBlob>> ListAsync(CancellationToken cancellationToken)
    {
        var container = CreateContainerClient();
        var settings = _settingsService.Current.CloudRecordings;
        var prefix = string.IsNullOrWhiteSpace(settings.BlobPrefix) ? null : settings.BlobPrefix.Trim();

        var recordings = new List<CloudRecordingBlob>();
        try
        {
            await foreach (var item in container.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken)
                .ConfigureAwait(false))
            {
                if (!item.Name.EndsWith(RecordingExtension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                recordings.Add(new CloudRecordingBlob(
                    item.Name,
                    item.Properties.LastModified?.UtcDateTime,
                    item.Properties.ContentLength,
                    item.Properties.ContentType));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RequestFailedException ex)
        {
            throw MapFailure(ex);
        }
        catch (Exception ex)
        {
            throw new ProviderException(ProviderName, $"Could not list recordings: {ex.Message}", ex);
        }

        _logger.LogDebug("Listed {Count} recording blob(s) from container '{Container}'", recordings.Count, container.Name);

        // Newest first when the service reports modification times.
        return [.. recordings.OrderByDescending(r => r.LastModifiedUtc ?? DateTime.MinValue)];
    }

    /// <inheritdoc />
    public async Task DownloadToFileAsync(string blobName, string destinationPath, CancellationToken cancellationToken)
    {
        var container = CreateContainerClient();
        var blob = container.GetBlobClient(blobName);

        try
        {
            await blob.DownloadToAsync(destinationPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RequestFailedException ex)
        {
            throw MapFailure(ex);
        }
        catch (Exception ex)
        {
            throw new ProviderException(ProviderName, $"Could not download recording '{blobName}': {ex.Message}", ex);
        }

        _logger.LogDebug("Downloaded recording '{BlobName}' to '{Path}'", blobName, destinationPath);
    }

    /// <summary>Builds a container client from settings, validating the connection string and container name.</summary>
    private BlobContainerClient CreateContainerClient()
    {
        var settings = _settingsService.Current.CloudRecordings;

        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            throw new ProviderException(
                ProviderName,
                "No Azure Storage connection string is configured. Enter one in Settings → Cloud Recordings.",
                isConfigurationError: true);
        }

        if (string.IsNullOrWhiteSpace(settings.ContainerName))
        {
            throw new ProviderException(
                ProviderName,
                "No container name is configured. Enter one in Settings → Cloud Recordings.",
                isConfigurationError: true);
        }

        try
        {
            return new BlobContainerClient(settings.ConnectionString, settings.ContainerName.Trim());
        }
        catch (Exception ex)
        {
            throw new ProviderException(
                ProviderName,
                "The Azure Storage connection string is not valid. Check it in Settings → Cloud Recordings.",
                ex,
                isConfigurationError: true);
        }
    }

    /// <summary>Maps a storage request failure to a user-presentable <see cref="ProviderException"/>.</summary>
    private static ProviderException MapFailure(RequestFailedException ex)
        => ex.Status switch
        {
            401 or 403 => new ProviderException(
                ProviderName,
                "Azure Storage rejected the credentials. Check the connection string in Settings → Cloud Recordings.",
                ex,
                isConfigurationError: true),
            404 => new ProviderException(
                ProviderName,
                "The configured container was not found. Check the container name in Settings → Cloud Recordings.",
                ex,
                isConfigurationError: true),
            _ => new ProviderException(ProviderName, $"Azure Storage request failed: {ex.Message}", ex),
        };
}
