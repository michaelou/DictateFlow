using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Transfer;

/// <summary>
/// Default <see cref="IPromptsArchive"/> implementation over
/// <see cref="IAppPaths.PromptsDirectory"/> and <see cref="System.IO.Compression"/>.
/// </summary>
public sealed class PromptsArchive : IPromptsArchive
{
    private readonly IAppPaths _appPaths;
    private readonly ILogger<PromptsArchive> _logger;

    /// <summary>Initializes a new instance of the <see cref="PromptsArchive"/> class.</summary>
    /// <param name="appPaths">Supplies the prompts directory.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public PromptsArchive(IAppPaths appPaths, ILogger<PromptsArchive> logger)
    {
        _appPaths = appPaths;
        _logger = logger;
    }

    /// <inheritdoc />
    public int ExportZip(string zipFilePath)
    {
        using var stream = File.Create(zipFilePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        var count = 0;
        foreach (var file in EnumeratePromptFiles())
        {
            archive.CreateEntryFromFile(file, Path.GetFileName(file));
            count++;
        }

        _logger.LogInformation("Exported {Count} prompt files to '{Zip}'", count, zipFilePath);
        return count;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetConflictingFiles(string zipFilePath)
    {
        using var archive = ZipFile.OpenRead(zipFilePath);
        return [.. EnumeratePromptEntries(archive)
            .Select(e => Path.GetFileName(e.FullName))
            .Where(name => File.Exists(Path.Combine(_appPaths.PromptsDirectory, name)))];
    }

    /// <inheritdoc />
    public int ImportZip(string zipFilePath, bool overwriteExisting)
    {
        Directory.CreateDirectory(_appPaths.PromptsDirectory);

        using var archive = ZipFile.OpenRead(zipFilePath);
        var count = 0;
        foreach (var entry in EnumeratePromptEntries(archive))
        {
            // Flattening to the bare file name defuses zip-slip paths like "..\..\evil.json".
            var target = Path.Combine(_appPaths.PromptsDirectory, Path.GetFileName(entry.FullName));
            if (!overwriteExisting && File.Exists(target))
            {
                continue;
            }

            entry.ExtractToFile(target, overwrite: true);
            count++;
        }

        _logger.LogInformation("Imported {Count} prompt files from '{Zip}'", count, zipFilePath);
        return count;
    }

    private IEnumerable<string> EnumeratePromptFiles()
        => Directory.Exists(_appPaths.PromptsDirectory)
            ? Directory.EnumerateFiles(_appPaths.PromptsDirectory, "*.json")
            : [];

    /// <summary>Archive entries that are JSON files (directories have an empty file name).</summary>
    private static IEnumerable<ZipArchiveEntry> EnumeratePromptEntries(ZipArchive archive)
        => archive.Entries.Where(e =>
            Path.GetFileName(e.FullName).Length > 0
            && string.Equals(Path.GetExtension(e.FullName), ".json", StringComparison.OrdinalIgnoreCase));
}
