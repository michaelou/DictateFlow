using DictateFlow.Core.Models;
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

    [Fact]
    public void GetAll_FileWithoutLlmEnabled_DefaultsToEnabled()
    {
        File.WriteAllText(PromptFile("Legacy"), """{"Name":"Legacy","Description":"d","SystemPrompt":"p"}""");

        Assert.True(_store.GetByName("Legacy")!.LlmEnabled);
    }

    [Fact]
    public void GetAll_LlmDisabledWithoutSystemPrompt_LoadsWithEmptyPrompt()
    {
        File.WriteAllText(PromptFile("Verbatim"), """{"Name":"Verbatim","Description":"d","LlmEnabled":false}""");

        var mode = _store.GetByName("Verbatim");

        Assert.NotNull(mode);
        Assert.False(mode!.LlmEnabled);
        Assert.Equal("", mode.SystemPrompt);
    }

    [Fact]
    public void Save_NewMode_WritesFileAndRefreshesCacheWithoutReload()
    {
        _store.GetAll(); // populate the cache (seeds the five defaults)

        _store.Save(new PromptMode("Custom", "desc", "prompt", 0.7, LlmEnabled: false));

        Assert.True(File.Exists(PromptFile("Custom")));
        var mode = _store.GetByName("Custom");
        Assert.NotNull(mode);
        Assert.Equal("desc", mode!.Description);
        Assert.Equal("prompt", mode.SystemPrompt);
        Assert.Equal(0.7, mode.Temperature);
        Assert.False(mode.LlmEnabled);
    }

    [Fact]
    public void Save_ExistingMode_Overwrites()
    {
        File.WriteAllText(PromptFile("Custom"), """{"Name":"Custom","Description":"old","SystemPrompt":"old"}""");
        Assert.Equal("old", _store.GetByName("Custom")!.Description);

        _store.Save(new PromptMode("Custom", "new", "new prompt", null));

        Assert.Single(Directory.GetFiles(_paths.PromptsDirectory, "*.json"));
        Assert.Equal("new", _store.GetByName("Custom")!.Description);
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("  ")]
    [InlineData("CON")]
    [InlineData("dots.")]
    public void Save_InvalidName_ThrowsAndWritesNothing(string name)
    {
        File.WriteAllText(PromptFile("Existing"), """{"Name":"Existing","Description":"d","SystemPrompt":"p"}""");

        Assert.Throws<ArgumentException>(() => _store.Save(new PromptMode(name, "", "p", null)));
        Assert.Single(Directory.GetFiles(_paths.PromptsDirectory, "*.json"));
    }

    [Fact]
    public void Save_TrimsTheName()
    {
        File.WriteAllText(PromptFile("Existing"), """{"Name":"Existing","Description":"d","SystemPrompt":"p"}""");

        _store.Save(new PromptMode("  Padded  ", "", "p", null));

        Assert.True(File.Exists(PromptFile("Padded")));
        Assert.Equal("Padded", _store.GetByName("Padded")!.Name);
    }

    [Fact]
    public void Delete_IsCaseInsensitive_AndRefreshesCache()
    {
        File.WriteAllText(PromptFile("First"), """{"Name":"First","Description":"d","SystemPrompt":"p"}""");
        File.WriteAllText(PromptFile("Second"), """{"Name":"Second","Description":"d","SystemPrompt":"p"}""");
        Assert.Equal(2, _store.GetAll().Count);

        _store.Delete("fIRST");

        Assert.False(File.Exists(PromptFile("First")));
        Assert.Null(_store.GetByName("First"));
        Assert.NotNull(_store.GetByName("Second"));
    }

    [Fact]
    public void Delete_MissingMode_IsANoOp()
    {
        File.WriteAllText(PromptFile("Only"), """{"Name":"Only","Description":"d","SystemPrompt":"p"}""");

        _store.Delete("Missing");

        Assert.NotNull(_store.GetByName("Only"));
    }

    [Fact]
    public void SaveThenDelete_RenameFlow_LeavesOneFile()
    {
        File.WriteAllText(PromptFile("Old"), """{"Name":"Old","Description":"d","SystemPrompt":"p"}""");
        File.WriteAllText(PromptFile("Other"), """{"Name":"Other","Description":"d","SystemPrompt":"p"}""");

        _store.Save(new PromptMode("New", "d", "p", null));
        _store.Delete("Old");

        Assert.False(File.Exists(PromptFile("Old")));
        Assert.True(File.Exists(PromptFile("New")));
        Assert.Equal(2, _store.GetAll().Count);
    }
}
