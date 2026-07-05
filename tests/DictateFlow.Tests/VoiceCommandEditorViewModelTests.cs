using DictateFlow.App.ViewModels;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace DictateFlow.Tests;

/// <summary>Tests for <see cref="VoiceCommandEditorViewModel"/>.</summary>
public sealed class VoiceCommandEditorViewModelTests : IDisposable
{
    private static readonly IReadOnlyList<string> ActionTypes =
        [ProcessStartAction.RegistrationName, OpenUrlAction.RegistrationName, OpenFolderAction.RegistrationName];

    private static readonly CommandDefinition Existing = new()
    {
        Name = "Open notes",
        Enabled = true,
        Phrases = ["open in notepad", "open notes"],
        ActionType = ProcessStartAction.RegistrationName,
        ActionValue = "notepad.exe",
        ActionArguments = "\"{{Argument}}\"",
        RequiresConfirmation = true,
    };

    private readonly ServiceProvider _provider;
    private readonly ICommandActionResolver _resolver;

    public VoiceCommandEditorViewModelTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IProcessLauncher, FakeProcessLauncher>();
        services.AddSingleton<ICommandActionResolver, CommandActionResolver>();
        services.AddCommandAction<ProcessStartAction>(ProcessStartAction.RegistrationName);
        services.AddCommandAction<OpenUrlAction>(OpenUrlAction.RegistrationName);
        services.AddCommandAction<OpenFolderAction>(OpenFolderAction.RegistrationName);
        _provider = services.BuildServiceProvider();
        _resolver = _provider.GetRequiredService<ICommandActionResolver>();
    }

    public void Dispose() => _provider.Dispose();

    private VoiceCommandEditorViewModel Create(CommandDefinition? existing, IReadOnlyList<string>? otherNames = null)
        => new(existing, otherNames ?? [], ActionTypes, _resolver);

    [Fact]
    public void NewCommand_StartsEmptyWithDefaults()
    {
        var vm = Create(null);

        Assert.True(vm.IsNew);
        Assert.Null(vm.OriginalName);
        Assert.Equal("", vm.Name);
        Assert.Equal("", vm.PhrasesText);
        Assert.Equal(ProcessStartAction.RegistrationName, vm.ActionType); // first offered type
        Assert.True(vm.Enabled);
        Assert.False(vm.RequiresConfirmation);
    }

    [Fact]
    public void ExistingCommand_PopulatesAllFields()
    {
        var vm = Create(Existing);

        Assert.False(vm.IsNew);
        Assert.Equal("Open notes", vm.OriginalName);
        Assert.Equal("Open notes", vm.Name);
        Assert.Equal($"open in notepad{Environment.NewLine}open notes", vm.PhrasesText);
        Assert.Equal(ProcessStartAction.RegistrationName, vm.ActionType);
        Assert.Equal("notepad.exe", vm.ActionValue);
        Assert.Equal("\"{{Argument}}\"", vm.ActionArguments);
        Assert.True(vm.RequiresConfirmation);
    }

    [Fact]
    public void Save_ValidFields_TrimsNameAndSplitsPhrases()
    {
        var vm = Create(null);
        vm.Name = "  Open calc  ";
        vm.PhrasesText = "open calc\r\n  open calculator  \n\n";
        vm.ActionType = ProcessStartAction.RegistrationName;
        vm.ActionValue = "calc.exe";
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        vm.SaveCommand.Execute(null);

        Assert.True(closed);
        Assert.NotNull(vm.Result);
        Assert.Equal("Open calc", vm.Result!.Name);
        Assert.Equal(["open calc", "open calculator"], vm.Result.Phrases);
        Assert.Null(vm.ValidationError);
    }

    [Fact]
    public void Save_MissingName_SetsErrorAndStaysOpen()
    {
        var vm = Create(null);
        vm.PhrasesText = "do it";
        vm.ActionValue = "notepad.exe";
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        vm.SaveCommand.Execute(null);

        Assert.False(closed);
        Assert.Null(vm.Result);
        Assert.NotNull(vm.ValidationError);
    }

    [Fact]
    public void Save_NoPhrases_SetsError()
    {
        var vm = Create(null);
        vm.Name = "X";
        vm.PhrasesText = "   \n  ";
        vm.ActionValue = "notepad.exe";

        vm.SaveCommand.Execute(null);

        Assert.Null(vm.Result);
        Assert.NotNull(vm.ValidationError);
    }

    [Fact]
    public void Save_DuplicateName_IsRejectedCaseInsensitively()
    {
        var vm = Create(null, ["Open calc"]);
        vm.Name = "open CALC";
        vm.PhrasesText = "open calc";
        vm.ActionValue = "calc.exe";

        vm.SaveCommand.Execute(null);

        Assert.Null(vm.Result);
        Assert.NotNull(vm.ValidationError);
    }

    [Fact]
    public void Save_UsesActionValidatorMessage_ForBadUrl()
    {
        var vm = Create(null);
        vm.Name = "Bad";
        vm.PhrasesText = "go";
        vm.ActionType = OpenUrlAction.RegistrationName;
        vm.ActionValue = "ftp://example.com";

        vm.SaveCommand.Execute(null);

        Assert.Null(vm.Result);
        Assert.Contains("http", vm.ValidationError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Save_ProcessStartPlaceholderInExecutable_IsRejected()
    {
        var vm = Create(null);
        vm.Name = "Bad";
        vm.PhrasesText = "open";
        vm.ActionType = ProcessStartAction.RegistrationName;
        vm.ActionValue = "{{Argument}}.exe";

        vm.SaveCommand.Execute(null);

        Assert.Null(vm.Result);
        Assert.NotNull(vm.ValidationError);
    }

    [Fact]
    public void Save_ClearsArguments_ForActionsThatDoNotSupportThem()
    {
        var vm = Create(null);
        vm.Name = "Site";
        vm.PhrasesText = "open site";
        vm.ActionType = OpenUrlAction.RegistrationName;
        vm.ActionValue = "https://example.com/";
        vm.ActionArguments = "leftover"; // typed while ProcessStart was selected

        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.Result);
        Assert.Equal("", vm.Result!.ActionArguments);
    }

    [Fact]
    public void SupportsArguments_AndValueHint_TrackActionType()
    {
        var vm = Create(null);

        vm.ActionType = ProcessStartAction.RegistrationName;
        Assert.True(vm.SupportsArguments);

        vm.ActionType = OpenFolderAction.RegistrationName;
        Assert.False(vm.SupportsArguments);
        Assert.Contains("folder", vm.ValueHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cancel_LeavesResultNullAndRequestsClose()
    {
        var vm = Create(Existing);
        vm.Name = "Changed";
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        vm.CancelCommand.Execute(null);

        Assert.True(closed);
        Assert.Null(vm.Result);
    }
}
