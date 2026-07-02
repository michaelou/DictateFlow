using System.Text.Json;
using DictateFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services;

/// <summary>
/// Default <see cref="ISettingsService"/> implementation that persists <see cref="AppSettings"/>
/// as indented JSON at <see cref="IAppPaths.SettingsFilePath"/>. Missing or corrupt files never
/// crash the application; defaults are used instead and a warning is logged.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    /// <summary>Serializer options used for both reading and writing <c>settings.json</c>.</summary>
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IAppPaths _appPaths;
    private readonly ILogger<SettingsService> _logger;

    /// <summary>Initializes a new instance of the <see cref="SettingsService"/> class.</summary>
    /// <param name="appPaths">Resolves the location of <c>settings.json</c>.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public SettingsService(IAppPaths appPaths, ILogger<SettingsService> logger)
    {
        _appPaths = appPaths;
        _logger = logger;
    }

    /// <inheritdoc />
    public AppSettings Current { get; private set; } = new();

    /// <inheritdoc />
    public event EventHandler<AppSettings>? SettingsChanged;

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var path = _appPaths.SettingsFilePath;

        if (!File.Exists(path))
        {
            _logger.LogInformation("Settings file {Path} not found; using defaults", path);
            Current = new AppSettings();
        }
        else
        {
            try
            {
                await using var stream = File.OpenRead(path);
                var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken)
                             .ConfigureAwait(false);
                Current = loaded ?? new AppSettings();
                _logger.LogInformation("Settings loaded from {Path}", path);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Settings file {Path} could not be read; falling back to defaults", path);
                Current = new AppSettings();
            }
        }

        SettingsChanged?.Invoke(this, Current);
    }

    /// <inheritdoc />
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var path = _appPaths.SettingsFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using (var stream = File.Create(path))
        {
            await JsonSerializer.SerializeAsync(stream, Current, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        _logger.LogInformation("Settings saved to {Path}", path);
        SettingsChanged?.Invoke(this, Current);
    }
}
