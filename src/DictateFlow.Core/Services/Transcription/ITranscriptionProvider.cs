using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Transcription;

/// <summary>Converts recorded audio into text.</summary>
public interface ITranscriptionProvider
{
    /// <summary>
    /// Transcribes a 16 kHz/16-bit/mono WAV stream.
    /// </summary>
    /// <param name="audio">The WAV audio to transcribe; read from its current position.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The transcription outcome.</returns>
    /// <exception cref="ProviderException">The provider failed in a user-actionable way.</exception>
    Task<TranscriptionResult> TranscribeAsync(
        Stream audio,
        CancellationToken cancellationToken);
}
