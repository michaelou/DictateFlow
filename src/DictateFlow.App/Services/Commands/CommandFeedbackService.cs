using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.Commands;

namespace DictateFlow.App.Services.Commands;

/// <summary>
/// App-layer <see cref="ICommandFeedback"/> that surfaces voice command recognition and
/// completion to the user: it puts the overlay into its command-executing state as soon as a
/// command is recognized, and plays the short cues (recognized / success / failure) gated by
/// <see cref="VoiceCommandSettings.EnableSounds"/>. The terminal overlay states
/// (success/error and their auto-hide) are driven separately by the dictation controller from
/// <c>PipelineResult.Command</c>.
/// </summary>
public sealed class CommandFeedbackService : ICommandFeedback
{
    private readonly IRecordingOverlay _overlay;
    private readonly ICommandSoundPlayer _soundPlayer;
    private readonly ISettingsService _settingsService;

    /// <summary>Initializes a new instance of the <see cref="CommandFeedbackService"/> class.</summary>
    /// <param name="overlay">Shows the command-executing state while the command runs.</param>
    /// <param name="soundPlayer">Plays the command cues.</param>
    /// <param name="settingsService">Supplies the sound-enabled setting, read per event.</param>
    public CommandFeedbackService(
        IRecordingOverlay overlay, ICommandSoundPlayer soundPlayer, ISettingsService settingsService)
    {
        _overlay = overlay;
        _soundPlayer = soundPlayer;
        _settingsService = settingsService;
    }

    /// <inheritdoc />
    public void OnCommandRecognized(CommandDefinition command)
    {
        _overlay.ShowCommandExecuting(command.Name);
        Play(CommandSound.Recognized);
    }

    /// <inheritdoc />
    public void OnCommandCompleted(CommandOutcome outcome)
    {
        switch (outcome.Status)
        {
            case CommandOutcomeStatus.Executed:
                Play(CommandSound.Success);
                break;

            case CommandOutcomeStatus.Failed:
            case CommandOutcomeStatus.Unknown:
                Play(CommandSound.Failure);
                break;

            // Declined is the user's own choice (they said No); the dialog was the feedback.
            default:
                break;
        }
    }

    /// <summary>Plays a cue when command sounds are enabled in settings.</summary>
    private void Play(CommandSound sound)
    {
        if (_settingsService.Current.VoiceCommands.EnableSounds)
        {
            _soundPlayer.Play(sound);
        }
    }
}
