using DictateFlow.App.Services;
using DictateFlow.App.Services.Commands;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Prompts;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="DictateFlowAction"/>: the spoken-argument prompt-mode resolution, the
/// fixed-enum <c>value</c> rejection, and the window/update/mode operations delegating to the
/// shared <see cref="IAppActions"/>.
/// </summary>
public sealed class DictateFlowActionTests
{
    private static readonly IReadOnlyList<PromptMode> Modes =
    [
        new PromptMode("Raw", "", "", null),
        new PromptMode("Email", "", "", null),
        new PromptMode("Code Review", "", "", null),
    ];

    private readonly Mock<IAppActions> _appActions = new();
    private readonly Mock<IPromptModeStore> _promptModeStore = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly AppSettings _appSettings = new() { ActivePromptMode = "Raw" };

    public DictateFlowActionTests()
    {
        _promptModeStore.Setup(s => s.GetAll()).Returns(Modes);
        _settings.SetupGet(s => s.Current).Returns(_appSettings);
        _settings.Setup(s => s.SaveAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    private DictateFlowAction CreateAction()
        => new(_appActions.Object, _settings.Object, _promptModeStore.Object, NullLogger<DictateFlowAction>.Instance);

    private static CommandContext Context(string actionValue, string argument = "")
        => new("Test Command", DictateFlowAction.RegistrationName, actionValue, "", argument, "transcript", DateTime.UtcNow);

    // --- SwitchPromptMode argument resolution (the reference argument-consuming action) ---

    [Theory]
    [InlineData("Email", "Email")]
    [InlineData("email", "Email")]            // case-insensitive
    [InlineData("EMAIL", "Email")]
    [InlineData("Email mode", "Email")]       // tolerates a trailing "mode"
    [InlineData("email MODE", "Email")]
    [InlineData("Code Review", "Code Review")] // multi-word mode name
    [InlineData("code review mode", "Code Review")]
    public void TryResolvePromptMode_ResolvesKnownModes(string argument, string expected)
    {
        var resolved = DictateFlowAction.TryResolvePromptMode(argument, Modes, out var mode);

        Assert.True(resolved);
        Assert.Equal(expected, mode.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("mode")]                       // just the suffix — nothing to resolve
    [InlineData("Spanish")]                    // unknown mode
    [InlineData("Email inbox")]                // extra words, not a suffix
    public void TryResolvePromptMode_UnknownArgument_FailsSafely(string argument)
    {
        Assert.False(DictateFlowAction.TryResolvePromptMode(argument, Modes, out _));
    }

    [Fact]
    public async Task Execute_SwitchPromptMode_KnownMode_UpdatesActiveModeAndPersists()
    {
        var result = await CreateAction().ExecuteAsync(
            Context(nameof(DictateFlowOperation.SwitchPromptMode), "Email mode"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Email", _appSettings.ActivePromptMode);
        _settings.Verify(s => s.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_SwitchPromptMode_UnknownMode_FailsWithoutChangingAnything()
    {
        var result = await CreateAction().ExecuteAsync(
            Context(nameof(DictateFlowOperation.SwitchPromptMode), "Klingon"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Raw", _appSettings.ActivePromptMode); // unchanged
        _settings.Verify(s => s.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- Fixed-enum value rejection ---

    [Theory]
    [InlineData("")]
    [InlineData("FormatMyDisk")]   // not a member of the enum
    [InlineData("open settings")]  // display text, not the enum member name
    public void Validate_UnknownOperation_IsRejected(string actionValue)
    {
        var error = CreateAction().Validate(new CommandDefinition { ActionValue = actionValue });

        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("OpenSettings")]
    [InlineData("switchpromptmode")] // case-insensitive
    public void Validate_KnownOperation_IsAccepted(string actionValue)
    {
        Assert.Null(CreateAction().Validate(new CommandDefinition { ActionValue = actionValue }));
    }

    [Fact]
    public async Task Execute_UnknownOperation_FailsWithoutRunningAnything()
    {
        var result = await CreateAction().ExecuteAsync(Context("FormatMyDisk"), CancellationToken.None);

        Assert.False(result.Success);
        _appActions.VerifyNoOtherCalls();
    }

    // --- Window/update operations share the tray's code via IAppActions ---

    [Fact]
    public async Task Execute_OpenSettings_DelegatesToAppActions()
    {
        var result = await CreateAction().ExecuteAsync(
            Context(nameof(DictateFlowOperation.OpenSettings)), CancellationToken.None);

        Assert.True(result.Success);
        _appActions.Verify(a => a.OpenSettings(), Times.Once);
    }

    [Fact]
    public async Task Execute_ShowHistory_DelegatesToAppActions()
    {
        await CreateAction().ExecuteAsync(Context(nameof(DictateFlowOperation.ShowHistory)), CancellationToken.None);

        _appActions.Verify(a => a.ShowHistory(), Times.Once);
    }

    [Fact]
    public async Task Execute_OpenCostDashboard_DelegatesToAppActions()
    {
        await CreateAction().ExecuteAsync(Context(nameof(DictateFlowOperation.OpenCostDashboard)), CancellationToken.None);

        _appActions.Verify(a => a.OpenCostDashboard(), Times.Once);
    }

    [Fact]
    public async Task Execute_CheckForUpdates_DelegatesToAppActions()
    {
        _appActions.Setup(a => a.CheckForUpdatesAsync()).Returns(Task.CompletedTask);

        await CreateAction().ExecuteAsync(Context(nameof(DictateFlowOperation.CheckForUpdates)), CancellationToken.None);

        _appActions.Verify(a => a.CheckForUpdatesAsync(), Times.Once);
    }
}
