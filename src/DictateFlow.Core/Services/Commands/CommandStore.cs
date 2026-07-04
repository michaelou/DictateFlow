using System.Text;
using System.Text.Json;
using DictateFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// The user-defined command store: one JSON file per command under
/// <see cref="IAppPaths.CommandsDirectory"/>, exposed to the matcher as an
/// <see cref="ICommandDefinitionSource"/>. Modeled on <c>PromptModeStore</c> — thread-safe,
/// example files seeded on first run, malformed or rule-violating files logged and skipped so
/// one bad edit never breaks the feature. The directory is re-read when its files change (by
/// name, size and write time), so adding or editing a command takes effect without a restart.
/// </summary>
/// <remarks>
/// Every loaded definition is validated against the action allowlist and the action type's own
/// rules (<see cref="ICommandActionValidator"/>): an unknown action type, empty phrases, or a
/// <c>{{Argument}}</c> placeholder used where it is not allowed all cause the file to be
/// skipped — it can never be matched or executed.
/// </remarks>
public sealed class CommandStore : ICommandDefinitionSource
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IAppPaths _appPaths;
    private readonly ICommandActionResolver _actionResolver;
    private readonly ILogger<CommandStore> _logger;
    private readonly object _gate = new();
    private IReadOnlyList<CommandDefinition> _cache = [];
    private string? _signature;

    /// <summary>Initializes a new instance of the <see cref="CommandStore"/> class.</summary>
    /// <param name="appPaths">Supplies the commands directory.</param>
    /// <param name="actionResolver">Validates action types against the registered allowlist and per-action rules.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public CommandStore(IAppPaths appPaths, ICommandActionResolver actionResolver, ILogger<CommandStore> logger)
    {
        _appPaths = appPaths;
        _actionResolver = actionResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<CommandDefinition> GetDefinitions()
    {
        lock (_gate)
        {
            var signature = ComputeSignature();
            if (signature != _signature)
            {
                _cache = Load();
                _signature = ComputeSignature(); // recompute so first-run seeding is captured.
            }

            return _cache;
        }
    }

    /// <summary>Forces the next <see cref="GetDefinitions"/> to re-read the directory.</summary>
    public void Reload()
    {
        lock (_gate)
        {
            _signature = null;
        }
    }

    /// <summary>Seeds the example files if needed, then loads every valid command file.</summary>
    private IReadOnlyList<CommandDefinition> Load()
    {
        var directory = _appPaths.CommandsDirectory;
        try
        {
            Directory.CreateDirectory(directory);
            SeedExamplesIfEmpty(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not prepare the commands directory '{Directory}'", directory);
            return [];
        }

        var definitions = new List<CommandDefinition>();
        var seenPhrases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var definition = TryReadDefinition(file);
            if (definition is null)
            {
                continue;
            }

            WarnOnDuplicatePhrases(definition, seenPhrases);
            definitions.Add(definition);
        }

        _logger.LogInformation("Loaded {Count} user commands from '{Directory}'", definitions.Count, directory);
        return definitions;
    }

    /// <summary>Reads and validates one command file; returns <see langword="null"/> (with a warning) when unusable.</summary>
    private CommandDefinition? TryReadDefinition(string file)
    {
        CommandDefinitionFile? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<CommandDefinitionFile>(File.ReadAllText(file), ReadOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Skipping malformed command file '{File}'", file);
            return null;
        }

        if (parsed is null)
        {
            _logger.LogWarning("Skipping command file '{File}': empty or null content", file);
            return null;
        }

        var definition = parsed.ToDefinition();
        if (definition.Phrases.Count == 0)
        {
            _logger.LogWarning("Skipping command file '{File}': at least one phrase is required", file);
            return null;
        }

        if (string.IsNullOrWhiteSpace(definition.ActionType))
        {
            _logger.LogWarning("Skipping command file '{File}': action.type is required", file);
            return null;
        }

        if (!_actionResolver.TryResolve(definition.ActionType, out var action))
        {
            _logger.LogWarning(
                "Skipping command file '{File}': unknown action type '{ActionType}' (valid: {ValidTypes})",
                file, definition.ActionType, string.Join(", ", _actionResolver.GetActionTypes()));
            return null;
        }

        if (action is ICommandActionValidator validator && validator.Validate(definition) is { } error)
        {
            _logger.LogWarning("Skipping command file '{File}': {Error}", file, error);
            return null;
        }

        return definition;
    }

    /// <summary>Logs a warning for any phrase already claimed by an earlier-loaded command (first wins).</summary>
    private void WarnOnDuplicatePhrases(CommandDefinition definition, HashSet<string> seenPhrases)
    {
        foreach (var phrase in definition.Phrases)
        {
            var normalized = NormalizePhrase(phrase);
            if (normalized.Length == 0)
            {
                continue;
            }

            if (!seenPhrases.Add(normalized))
            {
                _logger.LogWarning(
                    "Command '{CommandName}' repeats the phrase '{Phrase}' already used by an earlier command; the first wins",
                    definition.Name, phrase);
            }
        }
    }

    /// <summary>Normalizes a phrase the same way the matcher does, so duplicate detection matches its semantics.</summary>
    private static string NormalizePhrase(string phrase)
        => string.Join(' ', CommandTokenizer.Tokenize(phrase).Select(t => t.Normalized));

    /// <summary>Writes the example files, but only when no <c>.json</c> file exists yet.</summary>
    private void SeedExamplesIfEmpty(string directory)
    {
        if (Directory.EnumerateFiles(directory, "*.json").Any())
        {
            return;
        }

        foreach (var (fileName, command) in DefaultCommandFiles.All)
        {
            File.WriteAllText(
                Path.Combine(directory, $"{fileName}.json"),
                JsonSerializer.Serialize(command, WriteOptions));
        }

        _logger.LogInformation(
            "Seeded {Count} example commands into '{Directory}'", DefaultCommandFiles.All.Count, directory);
    }

    /// <summary>
    /// A cheap fingerprint of the directory's <c>.json</c> files (name, size, last write time),
    /// so an unchanged directory is served from cache and any add/edit/delete triggers a reload.
    /// </summary>
    private string ComputeSignature()
    {
        var directory = _appPaths.CommandsDirectory;
        if (!Directory.Exists(directory))
        {
            return "";
        }

        var builder = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var info = new FileInfo(file);
                builder.Append(file).Append('|').Append(info.Length).Append('|')
                    .Append(info.LastWriteTimeUtc.Ticks).Append('\n');
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file we cannot stat still contributes its name, so its later removal is noticed.
                builder.Append(file).Append("|?\n");
            }
        }

        return builder.ToString();
    }
}
