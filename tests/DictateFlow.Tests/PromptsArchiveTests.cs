using System.IO.Compression;
using DictateFlow.Core.Services.Transfer;
using Microsoft.Extensions.Logging.Abstractions;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="PromptsArchive"/>: zip round-trip in temp directories, conflict
/// detection, overwrite behavior and zip-slip hardening.
/// </summary>
public sealed class PromptsArchiveTests : IDisposable
{
    private readonly TestAppPaths _paths = new();
    private readonly string _scratch;

    public PromptsArchiveTests()
    {
        _scratch = Path.Combine(_paths.RootDirectory, "scratch");
        Directory.CreateDirectory(_scratch);
    }

    public void Dispose() => _paths.Dispose();

    private PromptsArchive CreateArchive() => new(_paths, NullLogger<PromptsArchive>.Instance);

    private void WritePrompt(string fileName, string content)
        => File.WriteAllText(Path.Combine(_paths.PromptsDirectory, fileName), content);

    [Fact]
    public void ExportThenImport_RoundTripsAllPromptFiles()
    {
        WritePrompt("Raw.json", """{ "Name": "Raw" }""");
        WritePrompt("Email.json", """{ "Name": "Email" }""");
        var zipPath = Path.Combine(_scratch, "prompts.zip");
        var archive = CreateArchive();

        Assert.Equal(2, archive.ExportZip(zipPath));

        // Wipe the prompts folder, then restore it from the archive.
        foreach (var file in Directory.EnumerateFiles(_paths.PromptsDirectory))
        {
            File.Delete(file);
        }

        Assert.Equal(2, archive.ImportZip(zipPath, overwriteExisting: false));
        Assert.Equal("""{ "Name": "Raw" }""",
            File.ReadAllText(Path.Combine(_paths.PromptsDirectory, "Raw.json")));
        Assert.Equal("""{ "Name": "Email" }""",
            File.ReadAllText(Path.Combine(_paths.PromptsDirectory, "Email.json")));
    }

    [Fact]
    public void GetConflictingFiles_ListsOnlyEntriesThatAlreadyExist()
    {
        WritePrompt("Raw.json", """{ "Name": "Raw" }""");
        WritePrompt("Email.json", """{ "Name": "Email" }""");
        var zipPath = Path.Combine(_scratch, "prompts.zip");
        var archive = CreateArchive();
        archive.ExportZip(zipPath);

        File.Delete(Path.Combine(_paths.PromptsDirectory, "Email.json"));

        Assert.Equal(["Raw.json"], archive.GetConflictingFiles(zipPath));
    }

    [Fact]
    public void ImportZip_WithoutOverwrite_KeepsExistingFiles()
    {
        WritePrompt("Raw.json", "original");
        var zipPath = Path.Combine(_scratch, "prompts.zip");
        var archive = CreateArchive();
        archive.ExportZip(zipPath);

        File.WriteAllText(Path.Combine(_paths.PromptsDirectory, "Raw.json"), "edited");

        Assert.Equal(0, archive.ImportZip(zipPath, overwriteExisting: false));
        Assert.Equal("edited", File.ReadAllText(Path.Combine(_paths.PromptsDirectory, "Raw.json")));
    }

    [Fact]
    public void ImportZip_WithOverwrite_ReplacesExistingFiles()
    {
        WritePrompt("Raw.json", "original");
        var zipPath = Path.Combine(_scratch, "prompts.zip");
        var archive = CreateArchive();
        archive.ExportZip(zipPath);

        File.WriteAllText(Path.Combine(_paths.PromptsDirectory, "Raw.json"), "edited");

        Assert.Equal(1, archive.ImportZip(zipPath, overwriteExisting: true));
        Assert.Equal("original", File.ReadAllText(Path.Combine(_paths.PromptsDirectory, "Raw.json")));
    }

    [Fact]
    public void ImportZip_IgnoresNonJsonEntries_AndFlattensPaths()
    {
        var zipPath = Path.Combine(_scratch, "crafted.zip");
        using (var stream = File.Create(zipPath))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "readme.txt", "not a prompt");
            WriteEntry(zip, "nested/folder/Nested.json", """{ "Name": "Nested" }""");
            WriteEntry(zip, "../Escape.json", """{ "Name": "Escape" }""");
        }

        Assert.Equal(2, CreateArchive().ImportZip(zipPath, overwriteExisting: false));

        // Both JSON entries land flat inside the prompts folder; nothing escapes it.
        Assert.True(File.Exists(Path.Combine(_paths.PromptsDirectory, "Nested.json")));
        Assert.True(File.Exists(Path.Combine(_paths.PromptsDirectory, "Escape.json")));
        Assert.False(File.Exists(Path.Combine(_paths.PromptsDirectory, "readme.txt")));
        Assert.False(File.Exists(Path.Combine(_paths.RootDirectory, "Escape.json")));
    }

    private static void WriteEntry(ZipArchive zip, string entryName, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(entryName).Open());
        writer.Write(content);
    }
}
