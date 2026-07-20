namespace DictateFlow.App.Services.Audio;

/// <summary>
/// Plays a single downloaded recording with transport control (play/pause/stop/seek), for the
/// cloud recordings review dialog. Only one track plays at a time; loading a new track replaces
/// the current one.
/// </summary>
public interface IRecordingPlayer : IDisposable
{
    /// <summary>
    /// Raised when playback state changes (loaded, started, paused, stopped or reached the end).
    /// May fire on a background thread — subscribers marshal to the UI thread themselves.
    /// </summary>
    event EventHandler? StateChanged;

    /// <summary>Gets a value indicating whether a track is currently playing (not paused/stopped).</summary>
    bool IsPlaying { get; }

    /// <summary>Gets a value indicating whether a track is loaded.</summary>
    bool HasTrack { get; }

    /// <summary>Gets the current playback position; <see cref="TimeSpan.Zero"/> when no track is loaded.</summary>
    TimeSpan Position { get; }

    /// <summary>Gets the loaded track's total duration; <see cref="TimeSpan.Zero"/> when no track is loaded.</summary>
    TimeSpan Duration { get; }

    /// <summary>Loads a track from a local file, replacing any current one (stopped, at position zero).</summary>
    /// <param name="filePath">Local path of the audio file to play (any format Media Foundation supports).</param>
    void Load(string filePath);

    /// <summary>Starts or resumes playback of the loaded track; a no-op when no track is loaded.</summary>
    void Play();

    /// <summary>Pauses playback, keeping the position; a no-op when nothing is playing.</summary>
    void Pause();

    /// <summary>Stops playback and rewinds to the start; a no-op when no track is loaded.</summary>
    void Stop();

    /// <summary>Seeks to <paramref name="position"/> (clamped to the track length); a no-op when no track is loaded.</summary>
    /// <param name="position">The target playback position.</param>
    void Seek(TimeSpan position);
}
