using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Transcription;

/// <summary>
/// Decides per recording whether streaming transcription applies and, when it does, runs the
/// streaming session against the active provider.
/// </summary>
public interface IStreamingTranscriptionCoordinator
{
    /// <summary>
    /// Begins a streaming session against the active transcription provider, or returns
    /// <see langword="null"/> when streaming does not apply — disabled in settings, the
    /// active provider does not implement <see cref="IStreamingTranscriptionProvider"/>, or
    /// the provider could not be resolved. A <see langword="null"/> result means the caller
    /// should use the standard non-streaming workflow.
    /// </summary>
    IStreamingTranscriptionSession? TryBegin();
}

/// <summary>
/// One in-flight streaming transcription: accepts live audio chunks while recording runs,
/// raises partial transcript updates, and produces the final transcript when completed.
/// Instances are single-use.
/// </summary>
public interface IStreamingTranscriptionSession : IAsyncDisposable
{
    /// <summary>
    /// Raised with the full transcript recognized so far, replacing any previous value.
    /// Fires on a worker thread — handlers must marshal to the UI themselves.
    /// </summary>
    event EventHandler<string>? PartialTranscriptChanged;

    /// <summary>Feeds one captured audio chunk to the provider. Safe to call from audio threads.</summary>
    /// <param name="chunk">The live PCM audio chunk.</param>
    void AddAudio(AudioChunk chunk);

    /// <summary>
    /// Signals that recording has stopped and waits for the provider's final transcript.
    /// </summary>
    /// <returns>
    /// The final transcript, or <see langword="null"/> when streaming failed or produced no
    /// text — the caller should fall back to transcribing the completed capture.
    /// </returns>
    Task<string?> CompleteAsync();
}
