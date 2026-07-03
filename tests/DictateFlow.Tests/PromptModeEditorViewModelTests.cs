using DictateFlow.App.ViewModels;
using DictateFlow.Core.Models;

namespace DictateFlow.Tests;

/// <summary>Tests for <see cref="PromptModeEditorViewModel"/>.</summary>
public sealed class PromptModeEditorViewModelTests
{
    private static readonly PromptMode Existing = new("Email", "desc", "prompt", 0.2);

    [Fact]
    public void NewMode_StartsEmptyWithLlmEnabled()
    {
        var vm = new PromptModeEditorViewModel(null, []);

        Assert.True(vm.IsNew);
        Assert.Null(vm.OriginalName);
        Assert.Equal("", vm.Name);
        Assert.True(vm.LlmEnabled);
    }

    [Fact]
    public void ExistingMode_PopulatesAllFields()
    {
        var vm = new PromptModeEditorViewModel(Existing, []);

        Assert.False(vm.IsNew);
        Assert.Equal("Email", vm.OriginalName);
        Assert.Equal("Email", vm.Name);
        Assert.Equal("desc", vm.Description);
        Assert.Equal("prompt", vm.SystemPrompt);
        Assert.Equal("0.2", vm.TemperatureText);
        Assert.True(vm.LlmEnabled);
    }

    [Fact]
    public void Save_ValidFields_SetsResultAndRequestsClose()
    {
        var vm = new PromptModeEditorViewModel(null, [])
        {
            Name = " Notes ",
            Description = "quick notes",
            SystemPrompt = "p {{Transcript}}",
            TemperatureText = "0.5",
        };
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        vm.SaveCommand.Execute(null);

        Assert.True(closed);
        Assert.NotNull(vm.Result);
        Assert.Equal("Notes", vm.Result!.Name); // trimmed
        Assert.Equal(0.5, vm.Result.Temperature);
        Assert.True(vm.Result.LlmEnabled);
        Assert.Null(vm.ValidationError);
    }

    [Fact]
    public void Save_EmptyTemperature_MeansProviderDefault()
    {
        var vm = new PromptModeEditorViewModel(Existing, []) { TemperatureText = "" };

        vm.SaveCommand.Execute(null);

        Assert.Null(vm.Result!.Temperature);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("CON")]
    public void Save_InvalidName_SetsErrorAndStaysOpen(string name)
    {
        var vm = new PromptModeEditorViewModel(null, []) { Name = name, SystemPrompt = "p" };
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        vm.SaveCommand.Execute(null);

        Assert.False(closed);
        Assert.Null(vm.Result);
        Assert.NotNull(vm.ValidationError);
    }

    [Fact]
    public void Save_DuplicateName_IsRejectedCaseInsensitively()
    {
        var vm = new PromptModeEditorViewModel(null, ["Email"]) { Name = "eMAIL", SystemPrompt = "p" };

        vm.SaveCommand.Execute(null);

        Assert.Null(vm.Result);
        Assert.NotNull(vm.ValidationError);
    }

    [Fact]
    public void Save_CaseOnlySelfRename_IsAllowed()
    {
        // The caller excludes the edited mode from otherModeNames, so only true clashes remain.
        var vm = new PromptModeEditorViewModel(Existing, ["Raw"]) { Name = "EMAIL" };

        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.Result);
        Assert.Equal("EMAIL", vm.Result!.Name);
    }

    [Fact]
    public void Save_EmptySystemPrompt_RequiredOnlyWhenLlmEnabled()
    {
        var enabled = new PromptModeEditorViewModel(null, []) { Name = "A", SystemPrompt = "" };
        enabled.SaveCommand.Execute(null);
        Assert.Null(enabled.Result);
        Assert.NotNull(enabled.ValidationError);

        var disabled = new PromptModeEditorViewModel(null, []) { Name = "A", SystemPrompt = "", LlmEnabled = false };
        disabled.SaveCommand.Execute(null);
        Assert.NotNull(disabled.Result);
        Assert.False(disabled.Result!.LlmEnabled);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-0.1")]
    [InlineData("2.5")]
    public void Save_InvalidTemperature_SetsError(string temperature)
    {
        var vm = new PromptModeEditorViewModel(Existing, []) { TemperatureText = temperature };

        vm.SaveCommand.Execute(null);

        Assert.Null(vm.Result);
        Assert.NotNull(vm.ValidationError);
    }

    [Fact]
    public void Cancel_LeavesResultNullAndRequestsClose()
    {
        var vm = new PromptModeEditorViewModel(Existing, []) { Name = "Changed" };
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        vm.CancelCommand.Execute(null);

        Assert.True(closed);
        Assert.Null(vm.Result);
    }
}
