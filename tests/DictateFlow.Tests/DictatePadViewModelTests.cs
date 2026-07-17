using DictateFlow.App.ViewModels;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Prompts;
using DictateFlow.Core.Services.Replacements;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="DictatePadViewModel"/>: the whole-text Enhance path (replacements →
/// resolve → LLM), one-step Undo, the LLM-disabled mode, and graceful provider failure.
/// </summary>
public sealed class DictatePadViewModelTests
{
    private static readonly PromptMode RawMode = new("Raw", "raw", "{{Transcript}}", 0.0);
    private static readonly PromptMode EmailMode = new("Email", "email", "e {{Transcript}}", 0.2);

    private readonly Mock<IPromptModeStore> _modeStore = new();
    private readonly Mock<IPromptResolver> _resolver = new();
    private readonly Mock<ILLMProvider> _llm = new();
    private readonly Mock<ITextReplacementService> _replacements = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly AppSettings _appSettings = new() { ActivePromptMode = "Email" };

    public DictatePadViewModelTests()
    {
        _modeStore.Setup(s => s.GetAll()).Returns([RawMode, EmailMode]);
        _settings.SetupGet(s => s.Current).Returns(_appSettings);
        // Replacements pass through unless a test overrides this.
        _replacements.Setup(r => r.Apply(It.IsAny<string>())).Returns<string>(t => t);
    }

    private DictatePadViewModel CreateViewModel() => new(
        _modeStore.Object,
        _resolver.Object,
        _llm.Object,
        _replacements.Object,
        _settings.Object,
        NullLogger<DictatePadViewModel>.Instance);

    private void SetupResolve(bool llmEnabled) =>
        _resolver.Setup(r => r.Resolve(It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string>((transcript, mode) =>
                new PromptContext("system", transcript, 0.2, 1000, mode, llmEnabled));

    [Fact]
    public void Ctor_LoadsModes_AndDefaultsToActiveMode()
    {
        var vm = CreateViewModel();

        Assert.Equal(new[] { "Raw", "Email" }, vm.PromptModes);
        Assert.Equal("Email", vm.SelectedPromptMode);
        _modeStore.Verify(s => s.Reload(), Times.Once);
    }

    [Fact]
    public async Task Enhance_ReplacesTextWithLlmOutput_AndEnablesUndo()
    {
        SetupResolve(llmEnabled: true);
        _llm.Setup(l => l.ProcessAsync(It.IsAny<PromptContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("ENHANCED");
        var vm = CreateViewModel();
        vm.Text = "hello world";

        await vm.EnhanceCommand.ExecuteAsync(null);

        Assert.Equal("ENHANCED", vm.Text);
        Assert.True(vm.CanUndo);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Enhance_UsesSelectedModeAndReplacementsBeforeLlm()
    {
        _replacements.Setup(r => r.Apply("raw text")).Returns("corrected text");
        _resolver.Setup(r => r.Resolve("corrected text", "Raw"))
            .Returns(new PromptContext("s", "corrected text", 0.0, 100, "Raw"));
        _llm.Setup(l => l.ProcessAsync(It.IsAny<PromptContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("final");
        var vm = CreateViewModel();
        vm.SelectedPromptMode = "Raw";
        vm.Text = "raw text";

        await vm.EnhanceCommand.ExecuteAsync(null);

        _replacements.Verify(r => r.Apply("raw text"), Times.Once);
        _resolver.Verify(r => r.Resolve("corrected text", "Raw"), Times.Once);
        Assert.Equal("final", vm.Text);
    }

    [Fact]
    public async Task Undo_RestoresPreEnhanceText()
    {
        SetupResolve(llmEnabled: true);
        _llm.Setup(l => l.ProcessAsync(It.IsAny<PromptContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("ENHANCED");
        var vm = CreateViewModel();
        vm.Text = "original";
        await vm.EnhanceCommand.ExecuteAsync(null);

        vm.UndoCommand.Execute(null);

        Assert.Equal("original", vm.Text);
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public async Task Enhance_LlmDisabledMode_AppliesReplacementsOnly_WithoutCallingLlm()
    {
        _replacements.Setup(r => r.Apply("raw")).Returns("REPLACED");
        SetupResolve(llmEnabled: false);
        var vm = CreateViewModel();
        vm.Text = "raw";

        await vm.EnhanceCommand.ExecuteAsync(null);

        Assert.Equal("REPLACED", vm.Text);
        _llm.Verify(
            l => l.ProcessAsync(It.IsAny<PromptContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Enhance_ProviderFailure_KeepsTextAndReportsError()
    {
        SetupResolve(llmEnabled: true);
        _llm.Setup(l => l.ProcessAsync(It.IsAny<PromptContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProviderException("Anthropic", "the model is unavailable"));
        var vm = CreateViewModel();
        vm.Text = "keep me";

        await vm.EnhanceCommand.ExecuteAsync(null);

        Assert.Equal("keep me", vm.Text);
        Assert.False(vm.CanUndo);
        Assert.False(vm.IsBusy);
        Assert.Contains("the model is unavailable", vm.StatusMessage);
    }

    [Fact]
    public void Enhance_CanNotExecute_WhenTextIsBlank()
    {
        var vm = CreateViewModel();
        vm.Text = "   ";

        Assert.False(vm.EnhanceCommand.CanExecute(null));

        vm.Text = "something";
        Assert.True(vm.EnhanceCommand.CanExecute(null));
    }
}
