using DictateFlow.App.Services.Commands;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using Moq;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="CommandFeedbackService"/>: the overlay command-executing state on
/// recognition and the per-status sound cues gated by <c>EnableSounds</c>.
/// </summary>
public sealed class CommandFeedbackServiceTests
{
    private readonly Mock<IRecordingOverlay> _overlay = new();
    private readonly Mock<ICommandSoundPlayer> _sounds = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly AppSettings _appSettings = new();

    public CommandFeedbackServiceTests()
    {
        _appSettings.VoiceCommands.EnableSounds = true;
        _settings.SetupGet(s => s.Current).Returns(_appSettings);
    }

    private CommandFeedbackService CreateService()
        => new(_overlay.Object, _sounds.Object, _settings.Object);

    [Fact]
    public void OnCommandRecognized_ShowsExecutingStateAndPlaysRecognizedCue()
    {
        CreateService().OnCommandRecognized(new CommandDefinition { Name = "Open Settings" });

        _overlay.Verify(o => o.ShowCommandExecuting("Open Settings"), Times.Once);
        _sounds.Verify(s => s.Play(CommandSound.Recognized), Times.Once);
    }

    [Fact]
    public void OnCommandRecognized_SoundsDisabled_StillShowsOverlayButPlaysNothing()
    {
        _appSettings.VoiceCommands.EnableSounds = false;

        CreateService().OnCommandRecognized(new CommandDefinition { Name = "Open Settings" });

        _overlay.Verify(o => o.ShowCommandExecuting("Open Settings"), Times.Once);
        _sounds.Verify(s => s.Play(It.IsAny<CommandSound>()), Times.Never);
    }

    [Theory]
    [InlineData(CommandOutcomeStatus.Executed, CommandSound.Success)]
    [InlineData(CommandOutcomeStatus.Failed, CommandSound.Failure)]
    [InlineData(CommandOutcomeStatus.Unknown, CommandSound.Failure)]
    public void OnCommandCompleted_PlaysCueForStatus(CommandOutcomeStatus status, CommandSound expected)
    {
        CreateService().OnCommandCompleted(new CommandOutcome(status, "Cmd", "msg"));

        _sounds.Verify(s => s.Play(expected), Times.Once);
    }

    [Fact]
    public void OnCommandCompleted_Declined_PlaysNoSound()
    {
        CreateService().OnCommandCompleted(new CommandOutcome(CommandOutcomeStatus.Declined, "Cmd", "msg"));

        _sounds.Verify(s => s.Play(It.IsAny<CommandSound>()), Times.Never);
    }

    [Fact]
    public void OnCommandCompleted_SoundsDisabled_PlaysNothing()
    {
        _appSettings.VoiceCommands.EnableSounds = false;

        CreateService().OnCommandCompleted(new CommandOutcome(CommandOutcomeStatus.Executed, "Cmd", "msg"));

        _sounds.Verify(s => s.Play(It.IsAny<CommandSound>()), Times.Never);
    }
}
