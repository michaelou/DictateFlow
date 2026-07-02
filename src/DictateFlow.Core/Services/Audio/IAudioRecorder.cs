namespace DictateFlow.Core.Services.Audio;

/// <summary>Captures microphone audio into an in-memory WAV stream.</summary>
public interface IAudioRecorder
{
    /// <summary>Gets a value indicating whether a capture session is currently running.</summary>
    bool IsRecording { get; }

    /// <summary>
    /// Raised for each captured buffer with the peak sample level normalized to 0..1.
    /// Fires on an audio worker thread — handlers must marshal to the UI themselves.
    /// </summary>
    event EventHandler<float>? LevelChanged;

    /// <summary>Starts capture on the configured device.</summary>
    /// <param name="cancellationToken">Cancels the start operation.</param>
    /// <exception cref="InvalidOperationException">A capture session is already running.</exception>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>Stops capture and returns a WAV stream positioned at 0. Caller disposes.</summary>
    /// <returns>The captured audio as a complete WAV stream.</returns>
    /// <exception cref="InvalidOperationException">No capture session is running.</exception>
    Task<Stream> StopAsync();
}
