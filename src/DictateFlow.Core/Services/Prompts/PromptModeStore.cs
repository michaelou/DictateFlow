using System.Text.Json;
using DictateFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Prompts;

/// <summary>
/// Default <see cref="IPromptModeStore"/> implementation. Loads all <c>*.json</c> files from
/// <see cref="IAppPaths.PromptsDirectory"/> lazily on first access, seeding the default modes
/// when the directory contains no <c>.json</c> files. Malformed files are logged and skipped
/// so one bad edit never takes the whole feature down.
/// </summary>
public sealed class PromptModeStore : IPromptModeStore
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly IAppPaths _appPaths;
    private readonly ILogger<PromptModeStore> _logger;
    private readonly object _gate = new();
    private IReadOnlyList<PromptMode>? _modes;

    /// <summary>Initializes a new instance of the <see cref="PromptModeStore"/> class.</summary>
    /// <param name="appPaths">Supplies the prompts directory.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public PromptModeStore(IAppPaths appPaths, ILogger<PromptModeStore> logger)
    {
        _appPaths = appPaths;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<PromptMode> GetAll()
    {
        lock (_gate)
        {
            return _modes ??= Load();
        }
    }

    /// <inheritdoc />
    public PromptMode? GetByName(string name)
        => GetAll().FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public void Reload()
    {
        lock (_gate)
        {
            _modes = Load();
        }
    }

    /// <inheritdoc />
    public void Save(PromptMode mode)
    {
        if (PromptModeNameRules.Validate(mode.Name) is { } error)
        {
            throw new ArgumentException(error, nameof(mode));
        }

        var name = mode.Name.Trim();
        lock (_gate)
        {
            var directory = _appPaths.PromptsDirectory;
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, $"{name}.json"),
                JsonSerializer.Serialize(mode with { Name = name }, WriteOptions));
            _logger.LogInformation("Saved prompt mode '{Name}'", name);
            _modes = Load();
        }
    }

    /// <inheritdoc />
    public void Delete(string name)
    {
        lock (_gate)
        {
            var directory = _appPaths.PromptsDirectory;
            if (!Directory.Exists(directory))
            {
                return;
            }

            // Match by filename so a Name/filename casing mismatch still deletes the file.
            var file = Directory.EnumerateFiles(directory, "*.json").FirstOrDefault(
                f => string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase));
            if (file is null)
            {
                return;
            }

            File.Delete(file);
            _logger.LogInformation("Deleted prompt mode file '{File}'", file);
            _modes = Load();
        }
    }

    /// <summary>Seeds defaults if needed, then loads every parseable mode file.</summary>
    private IReadOnlyList<PromptMode> Load()
    {
        var directory = _appPaths.PromptsDirectory;
        try
        {
            Directory.CreateDirectory(directory);
            SeedDefaultsIfEmpty(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not prepare the prompts directory '{Directory}'", directory);
            return DefaultPromptModes.All.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        var modes = new List<PromptMode>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            var mode = TryReadMode(file);
            if (mode is null)
            {
                continue;
            }

            if (modes.Any(m => string.Equals(m.Name, mode.Name, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("Skipping '{File}': a mode named '{Name}' is already loaded", file, mode.Name);
                continue;
            }

            modes.Add(mode);
        }

        _logger.LogInformation("Loaded {Count} prompt modes from '{Directory}'", modes.Count, directory);
        return modes.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Reads one mode file; returns <see langword="null"/> (with a warning) when unusable.</summary>
    private PromptMode? TryReadMode(string file)
    {
        try
        {
            var mode = JsonSerializer.Deserialize<PromptMode>(File.ReadAllText(file), ReadOptions);
            // A mode that skips the LLM never uses its system prompt, so it may be empty.
            if (mode is null
                || string.IsNullOrWhiteSpace(mode.Name)
                || (mode.LlmEnabled && string.IsNullOrWhiteSpace(mode.SystemPrompt)))
            {
                _logger.LogWarning(
                    "Skipping prompt file '{File}': Name is required, and SystemPrompt is required unless LlmEnabled is false", file);
                return null;
            }

            return mode with { Description = mode.Description ?? "", SystemPrompt = mode.SystemPrompt ?? "" };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Skipping malformed prompt file '{File}'", file);
            return null;
        }
    }

    /// <summary>Writes the five default mode files, but only when no <c>.json</c> file exists yet.</summary>
    private void SeedDefaultsIfEmpty(string directory)
    {
        if (Directory.EnumerateFiles(directory, "*.json").Any())
        {
            return;
        }

        foreach (var mode in DefaultPromptModes.All)
        {
            File.WriteAllText(
                Path.Combine(directory, $"{mode.Name}.json"),
                JsonSerializer.Serialize(mode, WriteOptions));
        }

        _logger.LogInformation("Seeded {Count} default prompt modes into '{Directory}'", DefaultPromptModes.All.Count, directory);
    }
}
