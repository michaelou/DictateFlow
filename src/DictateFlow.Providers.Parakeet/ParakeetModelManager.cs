using System.Diagnostics;
using System.Security.Cryptography;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Models;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Providers.Parakeet;

/// <summary>
/// <see cref="IModelManager"/> for the Parakeet TDT v3 model files. Installs into
/// <c>Models\Parakeet</c> under <see cref="IAppPaths"/> — there is no engine component
/// because inference runs in-process through the sherpa-onnx NuGet runtime. Every download
/// is streamed to a temporary file while its SHA-256 is computed, verified against the
/// pinned catalog value (plus exact size), and only then moved into place — so a component
/// is either fully installed or absent, and installations are effective without a restart.
/// </summary>
public sealed class ParakeetModelManager : IModelManager
{
    /// <summary>Name of the <c>IHttpClientFactory</c> client used for downloads.</summary>
    public const string HttpClientName = "ParakeetDownloads";

    /// <summary>Suffix of in-flight downloads, so partial files are never mistaken for installed ones.</summary>
    private const string DownloadSuffix = ".download";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAppPaths _appPaths;
    private readonly ILogger<ParakeetModelManager> _logger;

    /// <summary>Initializes a new instance of the <see cref="ParakeetModelManager"/> class.</summary>
    /// <param name="httpClientFactory">Supplies the download HTTP client per download, so this singleton never holds a stale handler.</param>
    /// <param name="appPaths">Supplies the model root directory.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public ParakeetModelManager(
        IHttpClientFactory httpClientFactory,
        IAppPaths appPaths,
        ILogger<ParakeetModelManager> logger)
    {
        _httpClientFactory = httpClientFactory;
        _appPaths = appPaths;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ModelDefinition> AvailableComponents => ParakeetModelCatalog.All;

    /// <summary>Gets the directory that holds the model files.</summary>
    public string ModelDirectory => Path.Combine(_appPaths.ModelsDirectory, ParakeetModelCatalog.EngineName);

    /// <inheritdoc />
    public Task<IReadOnlyList<InstalledModel>> GetInstalledModelsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<InstalledModel> installed = [.. AvailableComponents
            .Where(IsInstalled)
            .Select(definition => new InstalledModel(
                definition, GetModelPath(definition), new FileInfo(GetModelPath(definition)).Length))];
        return Task.FromResult(installed);
    }

    /// <inheritdoc />
    public bool IsInstalled(ModelDefinition model)
        => File.Exists(GetModelPath(model)) && new FileInfo(GetModelPath(model)).Length == model.SizeBytes;

    /// <summary>Checks whether every component of the model is installed.</summary>
    public bool IsFullyInstalled() => AvailableComponents.All(IsInstalled);

    /// <inheritdoc />
    public async Task DownloadAsync(ModelDefinition model, IProgress<double> progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ModelDirectory);
        var downloadPath = GetModelPath(model) + DownloadSuffix;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var sha256 = await DownloadToFileAsync(model, downloadPath, progress, cancellationToken).ConfigureAwait(false);
            VerifyDownload(model, downloadPath, sha256);
            File.Move(downloadPath, GetModelPath(model), overwrite: true);

            stopwatch.Stop();
            _logger.LogInformation(
                "Installed {DisplayName} ({SizeBytes} bytes) in {ElapsedMs} ms; verification: SHA-256 OK",
                model.DisplayName, model.SizeBytes, stopwatch.ElapsedMilliseconds);
        }
        catch (ProviderException)
        {
            TryDelete(downloadPath);
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryDelete(downloadPath);
            _logger.LogInformation("Download of {DisplayName} cancelled after {ElapsedMs} ms", model.DisplayName, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(downloadPath);
            _logger.LogWarning(ex, "Download of {DisplayName} failed after {ElapsedMs} ms", model.DisplayName, stopwatch.ElapsedMilliseconds);
            throw new ProviderException(
                ParakeetProviders.RegistrationName,
                $"Could not download {model.DisplayName} ({ex.Message}). Check your internet connection and try again.",
                ex);
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(ModelDefinition model)
    {
        TryDelete(GetModelPath(model));
        _logger.LogInformation("Removed {DisplayName}", model.DisplayName);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> VerifyAsync(ModelDefinition model, CancellationToken cancellationToken)
    {
        var path = GetModelPath(model);
        if (!File.Exists(path) || new FileInfo(path).Length != model.SizeBytes)
        {
            return false;
        }

        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        var matches = string.Equals(hash, model.Sha256, StringComparison.OrdinalIgnoreCase);
        _logger.LogDebug("Verified {DisplayName}: {Result}", model.DisplayName, matches ? "OK" : "checksum mismatch");
        return matches;
    }

    /// <summary>
    /// Gets the on-disk path of a component file (whether or not it is installed). The
    /// catalog id doubles as the file name, matching the upstream repository layout.
    /// </summary>
    /// <param name="model">The component definition.</param>
    public string GetModelPath(ModelDefinition model) => Path.Combine(ModelDirectory, model.Id);

    /// <summary>Streams the download to <paramref name="downloadPath"/>, hashing on the fly.</summary>
    /// <returns>The lowercase hex SHA-256 of the downloaded bytes.</returns>
    private async Task<string> DownloadToFileAsync(
        ModelDefinition model, string downloadPath, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client
            .GetAsync(model.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderException(
                ParakeetProviders.RegistrationName,
                $"The download of {model.DisplayName} failed with HTTP {(int)response.StatusCode}. Try again later.");
        }

        var totalBytes = response.Content.Headers.ContentLength ?? model.SizeBytes;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(
            downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1 << 16, useAsync: true);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer = new byte[1 << 16];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            sha256.AppendData(buffer, 0, read);
            written += read;
            if (totalBytes > 0)
            {
                progress.Report(Math.Min(1.0, (double)written / totalBytes));
            }
        }

        return Convert.ToHexStringLower(sha256.GetHashAndReset());
    }

    /// <summary>Rejects a download whose size or checksum does not match the pinned catalog values.</summary>
    private void VerifyDownload(ModelDefinition model, string downloadPath, string sha256)
    {
        var actualSize = new FileInfo(downloadPath).Length;
        if (actualSize != model.SizeBytes || !string.Equals(sha256, model.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Rejected corrupted download of {DisplayName}: size {ActualSize}/{ExpectedSize}, SHA-256 {ActualSha}",
                model.DisplayName, actualSize, model.SizeBytes, sha256);
            throw new ProviderException(
                ParakeetProviders.RegistrationName,
                $"The downloaded {model.DisplayName} file is corrupted (checksum mismatch) and was rejected. Try again.");
        }
    }

    /// <summary>Best-effort file removal for cleanup paths.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Leftover partial downloads are ignored by IsInstalled and replaced on retry.
        }
    }
}
