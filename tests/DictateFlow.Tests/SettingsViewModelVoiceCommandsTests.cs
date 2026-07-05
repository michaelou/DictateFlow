using System.Linq;
using DictateFlow.App;
using DictateFlow.App.Services.Commands;
using DictateFlow.App.ViewModels;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for the Voice Commands section of <see cref="SettingsViewModel"/>: the new section, the
/// <c>VoiceCommandSettings</c> read/write bindings, and the loaded-commands list. The view model
/// is built through the real DI container (as in <see cref="ServiceRegistrationTests"/>) so the
/// command definition sources are the ones the app actually registers.
/// </summary>
public sealed class SettingsViewModelVoiceCommandsTests : IDisposable
{
    private readonly TestAppPaths _paths = new();
    private readonly ServiceProvider _provider;

    public SettingsViewModelVoiceCommandsTests()
        => _provider = new ServiceCollection().AddLogging().AddDictateFlow(_paths).BuildServiceProvider();

    public void Dispose()
    {
        _provider.Dispose();
        _paths.Dispose();
    }

    private SettingsViewModel CreateViewModel() => _provider.GetRequiredService<SettingsViewModel>();

    [Fact]
    public void Sections_IncludeVoiceCommands()
    {
        Assert.Contains("Voice Commands", CreateViewModel().Sections);
    }

    [Fact]
    public void Constructor_LoadsVoiceCommandSettings()
    {
        var voice = _provider.GetRequiredService<ISettingsService>().Current.VoiceCommands;
        voice.Enabled = true;
        voice.WakePhrase = "Hey Tester";
        voice.WakePhraseEnabled = false;
        voice.CommandTimeoutSeconds = 12;
        voice.RequireConfirmation = true;
        voice.EnableSounds = false;

        var viewModel = CreateViewModel();

        Assert.True(viewModel.VoiceCommandsEnabled);
        Assert.Equal("Hey Tester", viewModel.VoiceWakePhrase);
        Assert.False(viewModel.VoiceWakePhraseEnabled);
        Assert.Equal(12, viewModel.VoiceCommandTimeoutSeconds);
        Assert.True(viewModel.VoiceRequireConfirmation);
        Assert.False(viewModel.VoiceEnableSounds);
    }

    [Fact]
    public async Task Save_PersistsVoiceCommandEdits()
    {
        var settings = _provider.GetRequiredService<ISettingsService>();
        var viewModel = CreateViewModel();

        viewModel.VoiceCommandsEnabled = true;
        viewModel.VoiceWakePhrase = "  Computer  ";
        viewModel.VoiceWakePhraseEnabled = true;
        viewModel.VoiceCommandTimeoutSeconds = 20;
        viewModel.VoiceRequireConfirmation = true;
        viewModel.VoiceEnableSounds = false;

        await viewModel.SaveCommand.ExecuteAsync(null);

        var voice = settings.Current.VoiceCommands;
        Assert.True(voice.Enabled);
        Assert.Equal("Computer", voice.WakePhrase); // trimmed
        Assert.True(voice.WakePhraseEnabled);
        Assert.Equal(20, voice.CommandTimeoutSeconds);
        Assert.True(voice.RequireConfirmation);
        Assert.False(voice.EnableSounds);
    }

    [Fact]
    public void LoadedCommands_IncludeBuiltInDictateFlowCommands()
    {
        var viewModel = CreateViewModel();

        Assert.NotEmpty(viewModel.LoadedCommands);
        Assert.Contains(viewModel.LoadedCommands, c =>
            c.Name == "Open Settings" && c.ActionType == DictateFlowAction.RegistrationName);

        // "Switch Prompt Mode" consumes the spoken argument, so it must be flagged.
        var switchMode = Assert.Single(
            viewModel.LoadedCommands, c => c.ActionType == DictateFlowAction.RegistrationName
                && c.Name == "Switch Prompt Mode");
        Assert.True(switchMode.TakesArgument);

        // A plain window command does not consume the argument.
        var openSettings = Assert.Single(viewModel.LoadedCommands, c => c.Name == "Open Settings");
        Assert.False(openSettings.TakesArgument);
    }

    [Fact]
    public void ReloadCommands_RepopulatesTheList()
    {
        var viewModel = CreateViewModel();
        viewModel.LoadedCommands.Clear();

        viewModel.ReloadCommandsCommand.Execute(null);

        Assert.NotEmpty(viewModel.LoadedCommands);
    }

    [Fact]
    public void CommandActionTypes_OfferLaunchActionsButNotAppOrTestActions()
    {
        var viewModel = CreateViewModel();

        Assert.Contains(ProcessStartAction.RegistrationName, viewModel.CommandActionTypes);
        Assert.Contains(OpenUrlAction.RegistrationName, viewModel.CommandActionTypes);
        Assert.Contains(OpenFolderAction.RegistrationName, viewModel.CommandActionTypes);
        Assert.DoesNotContain(DictateFlowAction.RegistrationName, viewModel.CommandActionTypes);
        Assert.DoesNotContain(MockCommandAction.RegistrationName, viewModel.CommandActionTypes);
    }

    [Fact]
    public void LoadedCommands_MarkUserCommandsEditableAndBuiltInsReadOnly()
    {
        WriteUserCommand("Unique test cmd",
            """{"name":"Unique test cmd","phrases":["do the unique thing"],"action":{"type":"ProcessStart","value":"notepad.exe"}}""");

        var viewModel = CreateViewModel();

        var user = Assert.Single(viewModel.LoadedCommands, c => c.Name == "Unique test cmd");
        Assert.True(user.IsUserCommand);
        Assert.NotNull(user.Definition);

        var builtIn = Assert.Single(viewModel.LoadedCommands, c => c.Name == "Open Settings");
        Assert.False(builtIn.IsUserCommand);
        Assert.Null(builtIn.Definition);
    }

    [Fact]
    public void CommandFilter_NarrowsTheView()
    {
        WriteUserCommand("Unique test cmd",
            """{"name":"Unique test cmd","phrases":["do the unique thing"],"action":{"type":"ProcessStart","value":"notepad.exe"}}""");
        var viewModel = CreateViewModel();

        viewModel.CommandFilter = "unique thing"; // matches the phrase only

        var shown = viewModel.CommandsView.Cast<LoadedCommandItem>().ToList();
        Assert.Equal("Unique test cmd", Assert.Single(shown).Name);

        viewModel.CommandFilter = "";
        Assert.True(viewModel.CommandsView.Cast<LoadedCommandItem>().Count() > 1);
    }

    private void WriteUserCommand(string name, string json)
    {
        Directory.CreateDirectory(_paths.CommandsDirectory);
        File.WriteAllText(Path.Combine(_paths.CommandsDirectory, $"{name}.json"), json);
    }
}
