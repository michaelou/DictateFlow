namespace DictateFlow.App.Services.Commands;

/// <summary>One of the short voice-command feedback cues.</summary>
public enum CommandSound
{
    /// <summary>A command was recognized and is about to run.</summary>
    Recognized,

    /// <summary>A command executed successfully.</summary>
    Success,

    /// <summary>A command failed, or the utterance matched no command.</summary>
    Failure,
}

/// <summary>Plays the short voice-command feedback cues. Never throws; a missing device is ignored.</summary>
public interface ICommandSoundPlayer
{
    /// <summary>Plays <paramref name="sound"/> (fire-and-forget).</summary>
    /// <param name="sound">The cue to play.</param>
    void Play(CommandSound sound);
}
