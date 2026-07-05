using DictateFlow.Core.Services.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="CommandStore"/> against a temporary commands directory: seeding, the
/// nested JSON schema, malformed/unknown/rule-violating files skipped, and live reload.
/// </summary>
public sealed class CommandStoreTests : IDisposable
{
    private readonly TestAppPaths _paths = new();
    private readonly ServiceProvider _provider;
    private readonly CommandStore _store;

    public CommandStoreTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IProcessLauncher, FakeProcessLauncher>();
        services.AddSingleton<ICommandActionResolver, CommandActionResolver>();
        services.AddCommandAction<ProcessStartAction>(ProcessStartAction.RegistrationName);
        services.AddCommandAction<OpenUrlAction>(OpenUrlAction.RegistrationName);
        services.AddCommandAction<OpenFolderAction>(OpenFolderAction.RegistrationName);
        _provider = services.BuildServiceProvider();

        _store = new CommandStore(
            _paths, _provider.GetRequiredService<ICommandActionResolver>(), NullLogger<CommandStore>.Instance);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _paths.Dispose();
    }

    private string CommandFile(string name) => Path.Combine(_paths.CommandsDirectory, $"{name}.json");

    private void Write(string name, string json) => File.WriteAllText(CommandFile(name), json);

    [Fact]
    public void GetDefinitions_EmptyDirectory_SeedsExampleFiles()
    {
        var definitions = _store.GetDefinitions();

        Assert.Equal(DefaultCommandFiles.All.Count, definitions.Count);
        Assert.Equal(DefaultCommandFiles.All.Count, Directory.GetFiles(_paths.CommandsDirectory, "*.json").Length);
        Assert.Contains(definitions, d => d.Name == "Open Notepad");
        Assert.Contains(definitions, d => d.Name == "Search Google");
    }

    [Fact]
    public void GetDefinitions_SeededFilesAllPassValidationAndLoad()
    {
        // Every seeded example must itself be a valid, loadable command (no self-inflicted skips).
        var definitions = _store.GetDefinitions();

        foreach (var (_, command) in DefaultCommandFiles.All)
        {
            Assert.Contains(definitions, d => d.Name == command.Name);
        }
    }

    [Fact]
    public void GetDefinitions_DirectoryAlreadyHasFiles_DoesNotSeed()
    {
        Write("Custom", """{"name":"Custom","phrases":["do it"],"action":{"type":"ProcessStart","value":"notepad.exe"}}""");

        var definition = Assert.Single(_store.GetDefinitions());

        Assert.Equal("Custom", definition.Name);
        Assert.False(File.Exists(CommandFile("Open Notepad"))); // seeding skipped
    }

    [Fact]
    public void GetDefinitions_ParsesNestedActionSchema_IncludingArguments()
    {
        Write("Notes", """
            {
              "name": "Open notes",
              "phrases": ["open in notepad"],
              "action": { "type": "ProcessStart", "value": "notepad.exe", "arguments": "\"{{Argument}}\"" }
            }
            """);

        var definition = Assert.Single(_store.GetDefinitions());

        Assert.Equal("ProcessStart", definition.ActionType);
        Assert.Equal("notepad.exe", definition.ActionValue);
        Assert.Equal("\"{{Argument}}\"", definition.ActionArguments);
    }

    [Fact]
    public void GetDefinitions_MalformedFile_SkippedWhileOthersLoad()
    {
        Write("Good", """{"name":"Good","phrases":["go"],"action":{"type":"OpenUrl","value":"https://example.com/"}}""");
        Write("Broken", "this is not json {");

        var definition = Assert.Single(_store.GetDefinitions());

        Assert.Equal("Good", definition.Name);
    }

    [Fact]
    public void GetDefinitions_UnknownActionType_Skipped()
    {
        Write("Danger", """{"name":"Danger","phrases":["run script"],"action":{"type":"PowerShellScript","value":"rm -rf /"}}""");

        Assert.Empty(_store.GetDefinitions());
    }

    [Fact]
    public void GetDefinitions_MissingPhrases_Skipped()
    {
        Write("NoPhrase", """{"name":"NoPhrase","phrases":[],"action":{"type":"ProcessStart","value":"notepad.exe"}}""");
        Write("NullPhrase", """{"name":"NullPhrase","action":{"type":"ProcessStart","value":"notepad.exe"}}""");

        Assert.Empty(_store.GetDefinitions());
    }

    [Fact]
    public void GetDefinitions_PlaceholderInProcessStartExecutable_Skipped()
    {
        Write("Bad", """{"name":"Bad","phrases":["open"],"action":{"type":"ProcessStart","value":"{{Argument}}.exe"}}""");

        Assert.Empty(_store.GetDefinitions());
    }

    [Fact]
    public void GetDefinitions_NonHttpUrl_Skipped()
    {
        Write("Bad", """{"name":"Bad","phrases":["open"],"action":{"type":"OpenUrl","value":"file:///C:/Windows"}}""");

        Assert.Empty(_store.GetDefinitions());
    }

    [Fact]
    public void GetDefinitions_DisabledCommand_LoadsButIsMarkedDisabled()
    {
        Write("Off", """{"name":"Off","enabled":false,"phrases":["off"],"action":{"type":"ProcessStart","value":"notepad.exe"}}""");

        var definition = Assert.Single(_store.GetDefinitions());

        Assert.False(definition.Enabled); // the matcher skips disabled definitions.
    }

    [Fact]
    public void GetDefinitions_MissingEnabled_DefaultsToEnabled()
    {
        Write("On", """{"name":"On","phrases":["on"],"action":{"type":"ProcessStart","value":"notepad.exe"}}""");

        Assert.True(Assert.Single(_store.GetDefinitions()).Enabled);
    }

    [Fact]
    public void GetDefinitions_PicksUpNewFileWithoutReload()
    {
        Write("First", """{"name":"First","phrases":["first"],"action":{"type":"ProcessStart","value":"notepad.exe"}}""");
        Assert.Single(_store.GetDefinitions());

        Write("Second", """{"name":"Second","phrases":["second"],"action":{"type":"ProcessStart","value":"calc.exe"}}""");

        Assert.Equal(2, _store.GetDefinitions().Count);
    }

    [Fact]
    public void GetDefinitions_PicksUpEditedFileWithoutReload()
    {
        Write("Edit", """{"name":"Before","phrases":["x"],"action":{"type":"ProcessStart","value":"notepad.exe"}}""");
        Assert.Equal("Before", Assert.Single(_store.GetDefinitions()).Name);

        Write("Edit", """{"name":"After edit","phrases":["x"],"action":{"type":"ProcessStart","value":"notepad.exe"}}""");

        Assert.Equal("After edit", Assert.Single(_store.GetDefinitions()).Name);
    }

    [Fact]
    public void GetDefinitions_PicksUpDeletedFile()
    {
        Write("Keep", """{"name":"Keep","phrases":["keep"],"action":{"type":"ProcessStart","value":"notepad.exe"}}""");
        Write("Gone", """{"name":"Gone","phrases":["gone"],"action":{"type":"ProcessStart","value":"calc.exe"}}""");
        Assert.Equal(2, _store.GetDefinitions().Count);

        File.Delete(CommandFile("Gone"));

        var definition = Assert.Single(_store.GetDefinitions());
        Assert.Equal("Keep", definition.Name);
    }

    [Fact]
    public void GetDefinitions_EmptiedDirectory_ReseedsExamples()
    {
        Write("Only", """{"name":"Only","phrases":["only"],"action":{"type":"ProcessStart","value":"notepad.exe"}}""");
        Assert.Single(_store.GetDefinitions());

        File.Delete(CommandFile("Only"));

        // Consistent with the prompt store: an empty directory reseeds the examples.
        Assert.Equal(DefaultCommandFiles.All.Count, _store.GetDefinitions().Count);
    }

    [Fact]
    public void Save_WritesNestedSchema_AndTheCommandLoads()
    {
        _store.Save(new Core.Models.CommandDefinition
        {
            Name = "Open calc",
            Phrases = ["open calc", "open calculator"],
            ActionType = ProcessStartAction.RegistrationName,
            ActionValue = "calc.exe",
        });

        Assert.True(File.Exists(CommandFile("Open calc")));
        var json = File.ReadAllText(CommandFile("Open calc"));
        Assert.Contains("\"action\"", json); // nested action object, not a flat field
        Assert.Contains("\"type\": \"ProcessStart\"", json);

        var definition = Assert.Single(_store.GetUserCommands(), d => d.Name == "Open calc");
        Assert.Equal("calc.exe", definition.ActionValue);
        Assert.Equal(["open calc", "open calculator"], definition.Phrases);
    }

    [Fact]
    public void Save_Overwrite_ReplacesTheExistingCommand()
    {
        _store.Save(new Core.Models.CommandDefinition
        {
            Name = "Site",
            Phrases = ["open site"],
            ActionType = OpenUrlAction.RegistrationName,
            ActionValue = "https://example.com/",
        });

        _store.Save(new Core.Models.CommandDefinition
        {
            Name = "Site",
            Phrases = ["open site"],
            ActionType = OpenUrlAction.RegistrationName,
            ActionValue = "https://example.org/",
        });

        var definition = Assert.Single(_store.GetUserCommands());
        Assert.Equal("https://example.org/", definition.ActionValue);
    }

    [Fact]
    public void Save_InvalidName_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => _store.Save(new Core.Models.CommandDefinition
        {
            Name = "a/b",
            Phrases = ["x"],
            ActionType = ProcessStartAction.RegistrationName,
            ActionValue = "notepad.exe",
        }));

        Assert.False(File.Exists(CommandFile("a")));
        Assert.NotNull(ex.Message);
    }

    [Fact]
    public void Save_ActionValidationFailure_Throws()
    {
        Assert.Throws<ArgumentException>(() => _store.Save(new Core.Models.CommandDefinition
        {
            Name = "Bad url",
            Phrases = ["go"],
            ActionType = OpenUrlAction.RegistrationName,
            ActionValue = "file:///C:/Windows",
        }));

        Assert.False(File.Exists(CommandFile("Bad url")));
    }

    [Fact]
    public void Save_UnknownActionType_Throws()
    {
        Assert.Throws<ArgumentException>(() => _store.Save(new Core.Models.CommandDefinition
        {
            Name = "Danger",
            Phrases = ["run"],
            ActionType = "PowerShellScript",
            ActionValue = "whatever",
        }));
    }

    [Fact]
    public void Delete_RemovesTheFile_CaseInsensitively()
    {
        Write("Keep", """{"name":"Keep","phrases":["keep"],"action":{"type":"ProcessStart","value":"notepad.exe"}}""");
        Write("Gone", """{"name":"Gone","phrases":["gone"],"action":{"type":"ProcessStart","value":"calc.exe"}}""");
        Assert.Equal(2, _store.GetUserCommands().Count);

        _store.Delete("gone"); // different casing than the file name

        var definition = Assert.Single(_store.GetUserCommands());
        Assert.Equal("Keep", definition.Name);
        Assert.False(File.Exists(CommandFile("Gone")));
    }

    [Fact]
    public void Delete_MissingCommand_DoesNothing()
    {
        Write("Keep", """{"name":"Keep","phrases":["keep"],"action":{"type":"ProcessStart","value":"notepad.exe"}}""");

        _store.Delete("nope");

        Assert.Single(_store.GetUserCommands());
    }
}
