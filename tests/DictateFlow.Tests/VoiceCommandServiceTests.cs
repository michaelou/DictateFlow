using System.Diagnostics.CodeAnalysis;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Commands;
using Microsoft.Extensions.Logging;
using Moq;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="VoiceCommandService"/> over a real detector and matcher: the
/// fall-through cases, the confirmation gate (including its fail-closed default), the
/// action-type allowlist and the execution failure shapes.
/// </summary>
public sealed class VoiceCommandServiceTests
{
    private readonly Mock<ISettingsService> _settings = new();
    private readonly Mock<ICommandConfirmationService> _confirmation = new();
    private readonly AppSettings _appSettings = new();
    private readonly FakeCommandActionResolver _resolver = new();
    private readonly RecordingCommandAction _action = new();
    private readonly List<CommandDefinition> _definitions = [];

    public VoiceCommandServiceTests()
    {
        _appSettings.VoiceCommands.Enabled = true;
        _settings.SetupGet(s => s.Current).Returns(_appSettings);
        _resolver.Add("Mock", _action);
        _definitions.Add(new CommandDefinition
        {
            Name = "Open Notepad",
            Phrases = ["open notepad"],
            ActionType = "Mock",
            ActionValue = "notepad.exe",
        });
        _confirmation.Setup(c => c.ConfirmAsync(It.IsAny<CommandDefinition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private VoiceCommandService CreateService(ICommandConfirmationService? confirmation = null)
    {
        var definitionSource = new Mock<ICommandDefinitionSource>();
        definitionSource.Setup(s => s.GetDefinitions()).Returns(() => _definitions);
        return new VoiceCommandService(
            _settings.Object,
            new WakePhraseDetector(Mock.Of<ILogger<WakePhraseDetector>>()),
            new CommandMatcher(Mock.Of<ILogger<CommandMatcher>>()),
            [definitionSource.Object],
            _resolver,
            confirmation ?? _confirmation.Object,
            TimeProvider.System,
            Mock.Of<ILogger<VoiceCommandService>>());
    }

    [Fact]
    public async Task TryHandleAsync_Disabled_ReturnsNullWithoutExecuting()
    {
        _appSettings.VoiceCommands.Enabled = false;

        var outcome = await CreateService().TryHandleAsync("Hey John open notepad", CancellationToken.None);

        Assert.Null(outcome);
        Assert.Null(_action.LastContext);
    }

    [Fact]
    public async Task TryHandleAsync_NoWakePhrase_ReturnsNull()
    {
        var outcome = await CreateService().TryHandleAsync("open notepad", CancellationToken.None);

        Assert.Null(outcome);
        Assert.Null(_action.LastContext);
    }

    [Fact]
    public async Task TryHandleAsync_MatchedCommand_ExecutesWithTheRightContext()
    {
        var outcome = await CreateService().TryHandleAsync("Hey John, open Notepad!", CancellationToken.None);

        Assert.Equal(CommandOutcomeStatus.Executed, outcome?.Status);
        Assert.Equal("Open Notepad", outcome?.CommandName);
        Assert.NotNull(_action.LastContext);
        Assert.Equal("Open Notepad", _action.LastContext.CommandName);
        Assert.Equal("notepad.exe", _action.LastContext.ActionValue);
        Assert.Equal("", _action.LastContext.Argument);
        Assert.Equal("Hey John, open Notepad!", _action.LastContext.Transcript);
    }

    [Fact]
    public async Task TryHandleAsync_ArgumentCommand_PassesTheVerbatimRemainder()
    {
        _definitions.Add(new CommandDefinition
        {
            Name = "Reminder", Phrases = ["remind me"], ActionType = "Mock",
        });

        await CreateService().TryHandleAsync(
            "Hey John remind me in 10 minutes to call Marko", CancellationToken.None);

        Assert.Equal("in 10 minutes to call Marko", _action.LastContext?.Argument);
    }

    [Fact]
    public async Task TryHandleAsync_WakePhraseButNoMatch_UnknownOutcomeAndNothingExecutes()
    {
        var outcome = await CreateService().TryHandleAsync("Hey John format my disk", CancellationToken.None);

        Assert.Equal(CommandOutcomeStatus.Unknown, outcome?.Status);
        Assert.Contains("format my disk", outcome?.Message);
        Assert.Null(_action.LastContext);
    }

    [Fact]
    public async Task TryHandleAsync_WakePhraseDisabledAndNoMatch_FallsThroughToDictation()
    {
        _appSettings.VoiceCommands.WakePhraseEnabled = false;

        var outcome = await CreateService().TryHandleAsync("just some dictated text", CancellationToken.None);

        Assert.Null(outcome);
    }

    [Fact]
    public async Task TryHandleAsync_WakePhraseDisabledAndMatch_Executes()
    {
        _appSettings.VoiceCommands.WakePhraseEnabled = false;

        var outcome = await CreateService().TryHandleAsync("open notepad", CancellationToken.None);

        Assert.Equal(CommandOutcomeStatus.Executed, outcome?.Status);
    }

    [Fact]
    public async Task TryHandleAsync_RequiresConfirmationDenied_DeclinedAndNothingExecutes()
    {
        _definitions[0].RequiresConfirmation = true;
        _confirmation.Setup(c => c.ConfirmAsync(It.IsAny<CommandDefinition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var outcome = await CreateService().TryHandleAsync("Hey John open notepad", CancellationToken.None);

        Assert.Equal(CommandOutcomeStatus.Declined, outcome?.Status);
        Assert.Null(_action.LastContext);
    }

    [Fact]
    public async Task TryHandleAsync_GlobalRequireConfirmation_AsksEvenWhenTheCommandDoesNot()
    {
        _appSettings.VoiceCommands.RequireConfirmation = true;

        await CreateService().TryHandleAsync("Hey John open notepad", CancellationToken.None);

        _confirmation.Verify(c => c.ConfirmAsync(
            It.Is<CommandDefinition>(d => d.Name == "Open Notepad"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryHandleAsync_NoConfirmationNeeded_NeverAsks()
    {
        await CreateService().TryHandleAsync("Hey John open notepad", CancellationToken.None);

        _confirmation.Verify(
            c => c.ConfirmAsync(It.IsAny<CommandDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TryHandleAsync_DefaultConfirmationService_DeniesFailClosed()
    {
        _definitions[0].RequiresConfirmation = true;
        var denying = new DenyingCommandConfirmationService(
            Mock.Of<ILogger<DenyingCommandConfirmationService>>());

        var outcome = await CreateService(denying).TryHandleAsync("Hey John open notepad", CancellationToken.None);

        Assert.Equal(CommandOutcomeStatus.Declined, outcome?.Status);
        Assert.Null(_action.LastContext);
    }

    [Fact]
    public async Task TryHandleAsync_ConfirmationThrows_DeniesFailClosed()
    {
        _definitions[0].RequiresConfirmation = true;
        _confirmation.Setup(c => c.ConfirmAsync(It.IsAny<CommandDefinition>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dialog exploded"));

        var outcome = await CreateService().TryHandleAsync("Hey John open notepad", CancellationToken.None);

        Assert.Equal(CommandOutcomeStatus.Declined, outcome?.Status);
        Assert.Null(_action.LastContext);
    }

    [Fact]
    public async Task TryHandleAsync_UnknownActionType_FailsWithoutExecuting()
    {
        _definitions[0].ActionType = "PowerShellScript"; // not registered — the allowlist boundary

        var outcome = await CreateService().TryHandleAsync("Hey John open notepad", CancellationToken.None);

        Assert.Equal(CommandOutcomeStatus.Failed, outcome?.Status);
        Assert.Contains("PowerShellScript", outcome?.Message);
        Assert.Null(_action.LastContext);
    }

    [Fact]
    public async Task TryHandleAsync_ActionReportsFailure_FailedOutcomeCarriesItsMessage()
    {
        _action.Behavior = (_, _) => Task.FromResult(CommandResult.Fail("Notepad could not be started."));

        var outcome = await CreateService().TryHandleAsync("Hey John open notepad", CancellationToken.None);

        Assert.Equal(CommandOutcomeStatus.Failed, outcome?.Status);
        Assert.Equal("Notepad could not be started.", outcome?.Message);
    }

    [Fact]
    public async Task TryHandleAsync_ActionThrows_FailedOutcomeHidesTheExceptionText()
    {
        _action.Behavior = (_, _) => throw new InvalidOperationException("boom");

        var outcome = await CreateService().TryHandleAsync("Hey John open notepad", CancellationToken.None);

        Assert.Equal(CommandOutcomeStatus.Failed, outcome?.Status);
        Assert.DoesNotContain("boom", outcome?.Message);
    }

    [Fact]
    public async Task TryHandleAsync_ActionOutlivesTheTimeout_FailsAsTimedOut()
    {
        _appSettings.VoiceCommands.CommandTimeoutSeconds = 1;
        _action.Behavior = async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return CommandResult.Ok("never reached");
        };

        var outcome = await CreateService().TryHandleAsync("Hey John open notepad", CancellationToken.None);

        Assert.Equal(CommandOutcomeStatus.Failed, outcome?.Status);
        Assert.Contains("timed out", outcome?.Message);
    }

    [Fact]
    public async Task TryHandleAsync_FailingDefinitionSource_IsSkippedNotFatal()
    {
        var broken = new Mock<ICommandDefinitionSource>();
        broken.Setup(s => s.GetDefinitions()).Throws(new InvalidOperationException("bad json"));
        var working = new Mock<ICommandDefinitionSource>();
        working.Setup(s => s.GetDefinitions()).Returns(_definitions);
        var service = new VoiceCommandService(
            _settings.Object,
            new WakePhraseDetector(Mock.Of<ILogger<WakePhraseDetector>>()),
            new CommandMatcher(Mock.Of<ILogger<CommandMatcher>>()),
            [broken.Object, working.Object],
            _resolver,
            _confirmation.Object,
            TimeProvider.System,
            Mock.Of<ILogger<VoiceCommandService>>());

        var outcome = await service.TryHandleAsync("Hey John open notepad", CancellationToken.None);

        Assert.Equal(CommandOutcomeStatus.Executed, outcome?.Status);
    }

    /// <summary>Resolver over a plain dictionary, standing in for the keyed-DI catalog.</summary>
    private sealed class FakeCommandActionResolver : ICommandActionResolver
    {
        private readonly Dictionary<string, ICommandAction> _actions = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string actionType, ICommandAction action) => _actions[actionType] = action;

        public IReadOnlyList<string> GetActionTypes() => [.. _actions.Keys];

        public bool TryResolve(string actionType, [NotNullWhen(true)] out ICommandAction? action)
            => _actions.TryGetValue(actionType, out action);
    }

    /// <summary>Action that records the context it ran with; behavior is swappable per test.</summary>
    private sealed class RecordingCommandAction : ICommandAction
    {
        public CommandContext? LastContext { get; private set; }

        public Func<CommandContext, CancellationToken, Task<CommandResult>> Behavior { get; set; }
            = (context, _) => Task.FromResult(CommandResult.Ok($"{context.CommandName} executed."));

        public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            LastContext = context;
            return Behavior(context, cancellationToken);
        }
    }
}
