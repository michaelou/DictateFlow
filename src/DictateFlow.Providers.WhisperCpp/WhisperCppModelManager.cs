using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Models;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Providers.WhisperCpp;

/// <summary>
/// <see cref="IModelManager"/> for the whisper.cpp engine and its ggml models. Installs into
/// <c>Engines\Whisper</c> (engine archive, extracted) and <c>Models\Whisper</c> (one
/// <c>.bin</c> per model) under <see cref="IAppPaths"/>. Every download is streamed to a
/// temporary file while its SHA-256 is computed, verified against the pinned catalog value
/// (plus exact size), and only then moved into place — so a component is either fully
/// installed or absent, and installations are effective without a restart.
/// </summary>
public sealed class WhisperCppModelManager : IModelManager
{
    /// <summary>Name of the <c>IHttpClientFactory</c> client used for downloads.</summary>
    public const string HttpClientName = "WhisperCppDownloads";

    /// <summary>File recording the installed engine version, written after successful extraction.</summary>
    private const string EngineManifestFileName = "engine.json";

    /// <summary>Suffix of in-flight downloads, so partial files are never mistaken for installed ones.</summary>
    private const string DownloadSuffix = ".download";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAppPaths _appPaths;
    private readonly ILogger<WhisperCppModelManager> _logger;

    /// <summary>Initializes a new instance of the <see cref="WhisperCppModelManager"/> class.</summary>
    /// <param name="httpClientFactory">Supplies the download HTTP client per download, so this singleton never holds a stale handler.</param>
    /// <param name="appPaths">Supplies the engine and model root directories.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public WhisperCppModelManager(
        IHttpClientFactory httpClientFactory,
        IAppPaths appPaths,
        ILogger<WhisperCppModelManager> logger)
    {
        _httpClientFactory = httpClientFactory;
        _appPaths = appPaths;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ModelDefinition> AvailableComponents => WhisperCppModelCatalog.All;

    /// <summary>Gets the directory the engine is extracted into.</summary>
    public string EngineDirectory => Path.Combine(_appPaths.EnginesDirectory, WhisperCppModelCatalog.EngineName);

    /// <summary>Gets the directory that holds the ggml model files.</summary>
    public string ModelDirectory => Path.Combine(_appPaths.ModelsDirectory, WhisperCppModelCatalog.EngineName);

    /// <inheritdoc />
    public Task<IReadOnlyList<InstalledModel>> GetInstalledModelsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<InstalledModel> installed = [.. AvailableComponents
            .Where(IsInstalled)
            .Select(definition =>
            {
                var path = GetInstallPath(definition);
                var size = definition.Kind == ModelComponentKind.Model ? new FileInfo(path).Length : definition.SizeBytes;
                return new InstalledModel(definition, path, size);
            })];
        return Task.FromResult(installed);
    }

    /// <inheritdoc />
    public bool IsInstalled(ModelDefinition model)
        => model.Kind == ModelComponentKind.Engine
            ? GetEngineExecutablePath() is not null
            : File.Exists(GetModelPath(model)) && new FileInfo(GetModelPath(model)).Length == model.SizeBytes;

    /// <inheritdoc />
    public async Task DownloadAsync(ModelDefinition model, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var targetDirectory = model.Kind == ModelComponentKind.Engine ? EngineDirectory : ModelDirectory;
        Directory.CreateDirectory(targetDirectory);
        var downloadPath = Path.Combine(targetDirectory, model.Id + DownloadSuffix);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var sha256 = await DownloadToFileAsync(model, downloadPath, progress, cancellationToken).ConfigureAwait(false);
            VerifyDownload(model, downloadPath, sha256);

            if (model.Kind == ModelComponentKind.Engine)
            {
                InstallEngine(model, downloadPath);
            }
            else
            {
                File.Move(downloadPath, GetModelPath(model), overwrite: true);
            }

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
                WhisperCppProviders.RegistrationName,
                $"Could not download {model.DisplayName} ({ex.Message}). Check your internet connection and try again.",
                ex);
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(ModelDefinition model)
    {
        if (model.Kind == ModelComponentKind.Engine)
        {
            if (Directory.Exists(EngineDirectory))
            {
                Directory.Delete(EngineDirectory, recursive: true);
            }
        }
        else
        {
            TryDelete(GetModelPath(model));
        }

        _logger.LogInformation("Removed {DisplayName}", model.DisplayName);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> VerifyAsync(ModelDefinition model, CancellationToken cancellationToken)
    {
        if (model.Kind == ModelComponentKind.Engine)
        {
            // The archive is discarded after extraction, so re-hashing it is impossible;
            // the manifest records the verified archive hash and the executable must exist.
            return GetEngineExecutablePath() is not null
                && ReadEngineManifest() is { } manifest
                && string.Equals(manifest.Sha256, model.Sha256, StringComparison.OrdinalIgnoreCase);
        }

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
    /// Finds the installed whisper.cpp executable, searching the engine directory
    /// recursively (release archives nest the binaries in a subfolder).
    /// </summary>
    /// <returns>The full executable path, or <see langword="null"/> when not installed.</returns>
    public string? GetEngineExecutablePath()
    {
        if (!Directory.Exists(EngineDirectory))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(EngineDirectory, WhisperCppModelCatalog.EngineExecutableName, SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    /// <summary>Reads the installed engine version from the manifest, or <see langword="null"/> when not installed.</summary>
    public string? GetInstalledEngineVersion() => ReadEngineManifest()?.Version;

    /// <summary>Gets the on-disk path of a model file (whether or not it is installed).</summary>
    /// <param name="model">The model definition.</param>
    public string GetModelPath(ModelDefinition model) => Path.Combine(ModelDirectory, model.Id + ".bin");

    /// <summary>The install location reported for one component.</summary>
    private string GetInstallPath(ModelDefinition definition)
        => definition.Kind == ModelComponentKind.Engine ? EngineDirectory : GetModelPath(definition);

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
                WhisperCppProviders.RegistrationName,
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
                WhisperCppProviders.RegistrationName,
                $"The downloaded {model.DisplayName} file is corrupted (checksum mismatch) and was rejected. Try again.");
        }
    }

    /// <summary>
    /// Replaces any previous engine installation with the verified archive's contents and
    /// registers it by writing the manifest. Extraction must yield the CLI executable.
    /// </summary>
    private void InstallEngine(ModelDefinition model, string archivePath)
    {
        // Clear the previous installation, keeping only the just-verified archive.
        foreach (var entry in Directory.EnumerateFileSystemEntries(EngineDirectory))
        {
            if (string.Equals(entry, archivePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }
        }

        ZipFile.ExtractToDirectory(archivePath, EngineDirectory, overwriteFiles: true);
        File.Delete(archivePath);

        if (GetEngineExecutablePath() is null)
        {
            Directory.Delete(EngineDirectory, recursive: true);
            throw new ProviderException(
                WhisperCppProviders.RegistrationName,
                $"The {model.DisplayName} archive did not contain {WhisperCppModelCatalog.EngineExecutableName}; the installation was rolled back.");
        }

        var manifest = new EngineManifest(WhisperCppModelCatalog.EngineVersion, model.Sha256, DateTime.UtcNow);
        File.WriteAllText(
            Path.Combine(EngineDirectory, EngineManifestFileName),
            JsonSerializer.Serialize(manifest, ManifestJsonOptions));
    }

    /// <summary>Reads the engine manifest; <see langword="null"/> when missing or unreadable.</summary>
    private EngineManifest? ReadEngineManifest()
    {
        var path = Path.Combine(EngineDirectory, EngineManifestFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<EngineManifest>(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            return null;
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

    /// <summary>What <c>engine.json</c> records about the installed engine.</summary>
    /// <param name="Version">The extracted whisper.cpp release version.</param>
    /// <param name="Sha256">The verified SHA-256 of the release archive.</param>
    /// <param name="InstalledUtc">When the installation completed.</param>
    private sealed record EngineManifest(string Version, string Sha256, DateTime InstalledUtc);
}
