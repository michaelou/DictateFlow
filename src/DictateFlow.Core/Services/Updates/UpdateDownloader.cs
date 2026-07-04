using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Updates;

/// <summary>
/// Default <see cref="IUpdateDownloader"/>: streams the installer to
/// <c>%TEMP%\DictateFlow\</c> so it survives the app shutting down before the setup wizard
/// runs, reporting progress as it goes and verifying the finished size.
/// </summary>
public sealed class UpdateDownloader : IUpdateDownloader
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UpdateDownloader> _logger;

    /// <summary>Initializes a new instance of the <see cref="UpdateDownloader"/> class.</summary>
    /// <param name="httpClient">The typed HTTP client (User-Agent and no-timeout are configured at registration).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public UpdateDownloader(HttpClient httpClient, ILogger<UpdateDownloader> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> DownloadAsync(
        string url,
        long expectedSize,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(Path.GetTempPath(), "DictateFlow");
        Directory.CreateDirectory(directory);

        // Keep the .exe name so the installer keeps its identity (Inno's AppId does the matching,
        // but a recognizable name is friendlier if the user finds it in temp).
        var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            fileName = "DictateFlowSetup.exe";
        }

        var destinationPath = Path.Combine(directory, fileName);
        _logger.LogInformation("Downloading update from {Url} to {Path}", url, destinationPath);

        using var response = await _httpClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // Prefer the server's content length for progress; fall back to the size from the
        // release metadata when the response is chunked / omits it.
        var total = response.Content.Headers.ContentLength ?? (expectedSize > 0 ? expectedSize : 0);

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var destination = new FileStream(
            destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                downloaded += read;
                if (progress is not null && total > 0)
                {
                    progress.Report(Math.Clamp((double)downloaded / total, 0, 1));
                }
            }
        }

        if (expectedSize > 0)
        {
            var actualSize = new FileInfo(destinationPath).Length;
            if (actualSize != expectedSize)
            {
                TryDelete(destinationPath);
                throw new InvalidOperationException(
                    $"The downloaded installer was {actualSize} bytes but {expectedSize} were expected. The download may be corrupt.");
            }
        }

        progress?.Report(1);
        _logger.LogInformation("Update download complete: {Path}", destinationPath);
        return destinationPath;
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete the incomplete installer download at {Path}", path);
        }
    }
}
