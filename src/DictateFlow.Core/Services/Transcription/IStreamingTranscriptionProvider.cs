using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Transcription;

/// <summary>
/// Optional capability of a transcription provider: converts audio into text while the audio
/// is still being captured. Providers that support streaming implement this interface in
/// addition to <see cref="ITranscriptionProvider"/>; the application detects the capability
/// at runtime and falls back to the non-streaming workflow when it is absent (or when
/// streaming is disabled in settings).
/// </summary>
public interface IStreamingTranscriptionProvider
{
    /// <summary>
    /// Transcribes live 16 kHz/16-bit/mono PCM audio as it arrives.
    /// </summary>
    /// <param name="audio">The captured audio chunks; the sequence completes when recording stops.</param>
    /// <param name="cancellationToken">Cancels the streaming session.</param>
    /// <returns>
    /// Transcript updates, each carrying the full text recognized so far. The last update
    /// should have <see cref="TranscriptionUpdate.IsFinal"/> set; when the stream completes
    /// without a final update, the latest text is used as the final transcript.
    /// </returns>
    /// <exception cref="ProviderException">The provider failed in a user-actionable way.</exception>
    IAsyncEnumerable<TranscriptionUpdate> TranscribeStreamingAsync(
        IAsyncEnumerable<AudioChunk> audio,
        CancellationToken cancellationToken);
}
