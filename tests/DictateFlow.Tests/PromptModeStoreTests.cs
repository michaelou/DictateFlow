using DictateFlow.Core.Services.Prompts;
using Microsoft.Extensions.Logging.Abstractions;

namespace DictateFlow.Tests;

/// <summary>Tests for <see cref="PromptModeStore"/> against a temporary prompts directory.</summary>
public sealed class PromptModeStoreTests : IDisposable
{
    private readonly TestAppPaths _paths = new();
    private readonly PromptModeStore _store;

    public PromptModeStoreTests()
    {
        _store = new PromptModeStore(_paths, NullLogger<PromptModeStore>.Instance);
    }

    public void Dispose() => _paths.Dispose();

    private string PromptFile(string name) => Path.Combine(_paths.PromptsDirectory, $"{name}.json");

    [Fact]
    public void GetAll_EmptyDirectory_SeedsTheFiveDefaults()
    {
        var modes = _store.GetAll();

        Assert.Equal(5, modes.Count);
        Assert.Equal(5, Directory.GetFiles(_paths.PromptsDirectory, "*.json").Length);
        foreach (var name in new[] { "Email", "GithubIssue", "ChatPrompt", "TechnicalSpec", "Raw" })
        {
            Assert.NotNull(_store.GetByName(name));
            Assert.True(File.Exists(PromptFile(name)));
        }
    }

    [Fact]
    public void GetAll_DirectoryAlreadyHasFiles_DoesNotSeed()
    {
        File.WriteAllText(PromptFile("Custom"), """{"Name":"Custom","Description":"d","SystemPrompt":"p"}""");

        var modes = _store.GetAll();

        var mode = Assert.Single(modes);
        Assert.Equal("Custom", mode.Name);
        Assert.False(File.Exists(PromptFile("Raw"))); // seeding skipped
    }

    [Fact]
    public void GetAll_MalformedFile_SkippedWhileOthersLoad()
    {
        File.WriteAllText(PromptFile("Good"), """{"Name":"Good","Description":"d","SystemPrompt":"p","Temperature":0.5}""");
        File.WriteAllText(PromptFile("Broken"), "this is not json {");
        File.WriteAllText(PromptFile("MissingPrompt"), """{"Name":"MissingPrompt","Description":"d"}""");

        var modes = _store.GetAll();

        var mode = Assert.Single(modes);
        Assert.Equal("Good", mode.Name);
        Assert.Equal(0.5, mode.Temperature);
    }

    [Fact]
    public void GetByName_IsCaseInsensitive()
    {
        File.WriteAllText(PromptFile("Custom"), """{"Name":"Custom","Description":"d","SystemPrompt":"p"}""");

        Assert.NotNull(_store.GetByName("cUSTOM"));
        Assert.Null(_store.GetByName("Missing"));
    }

    [Fact]
    public void Reload_PicksUpNewAndChangedFiles()
    {
        File.WriteAllText(PromptFile("First"), """{"Name":"First","Description":"d","SystemPrompt":"p1"}""");
        Assert.Single(_store.GetAll());

        File.WriteAllText(PromptFile("Second"), """{"Name":"Second","Description":"d","SystemPrompt":"p2"}""");
        File.WriteAllText(PromptFile("First"), """{"Name":"First","Description":"d","SystemPrompt":"changed"}""");
        Assert.Single(_store.GetAll()); // cached until Reload

        _store.Reload();

        Assert.Equal(2, _store.GetAll().Count);
        Assert.Equal("changed", _store.GetByName("First")!.SystemPrompt);
    }

    [Fact]
    public void GetAll_SeededRawMode_MatchesBuiltInFallback()
    {
        var raw = _store.GetByName(DefaultPromptModes.RawModeName);

        Assert.NotNull(raw);
        Assert.Equal(DefaultPromptModes.Raw.SystemPrompt, raw!.SystemPrompt);
        Assert.Equal(0.0, raw.Temperature);
    }
}
