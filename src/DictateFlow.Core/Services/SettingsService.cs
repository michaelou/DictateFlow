using System.Text.Json;
using System.Text.Json.Nodes;
using DictateFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services;

/// <summary>
/// Default <see cref="ISettingsService"/> implementation that persists <see cref="AppSettings"/>
/// as indented JSON at <see cref="IAppPaths.SettingsFilePath"/>. Missing or corrupt files never
/// crash the application; defaults are used instead and a warning is logged. Loading applies
/// the registered <see cref="ISettingsMigration"/>s to the raw JSON first and writes the file
/// back when one of them changed it.
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
    private readonly IEnumerable<ISettingsMigration> _migrations;
    private readonly ILogger<SettingsService> _logger;

    /// <summary>Initializes a new instance of the <see cref="SettingsService"/> class.</summary>
    /// <param name="appPaths">Resolves the location of <c>settings.json</c>.</param>
    /// <param name="migrations">Schema migrations applied to the raw JSON at load time.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public SettingsService(IAppPaths appPaths, IEnumerable<ISettingsMigration> migrations, ILogger<SettingsService> logger)
    {
        _appPaths = appPaths;
        _migrations = migrations;
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
                var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                Current = await ParseAndMigrateAsync(path, text, cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc />
    public Task ReplaceAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Current = settings;
        return SaveAsync(cancellationToken);
    }

    /// <summary>
    /// Parses the file content, runs the schema migrations on the raw JSON (writing the file
    /// back when one changed it) and deserializes the result. Migrating at the JSON level
    /// preserves fields the current schema does not know about.
    /// </summary>
    private async Task<AppSettings> ParseAndMigrateAsync(string path, string text, CancellationToken cancellationToken)
    {
        if (JsonNode.Parse(text) is not JsonObject root)
        {
            throw new JsonException("The settings file does not contain a JSON object.");
        }

        var migrated = false;
        foreach (var migration in _migrations)
        {
            migrated |= migration.TryMigrate(root);
        }

        if (migrated)
        {
            await File.WriteAllTextAsync(path, root.ToJsonString(SerializerOptions), cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation("Settings file {Path} migrated to the current schema and written back", path);
        }

        return root.Deserialize<AppSettings>(SerializerOptions) ?? new AppSettings();
    }
}
