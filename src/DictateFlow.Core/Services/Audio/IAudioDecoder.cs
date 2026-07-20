namespace DictateFlow.Core.Services.Audio;

/// <summary>
/// Decodes a compressed audio file (e.g. <c>.m4a</c>/AAC) into the 16 kHz / 16-bit / mono WAV
/// format the transcription providers expect (see <c>ITranscriptionProvider</c>).
/// </summary>
public interface IAudioDecoder
{
    /// <summary>
    /// Decodes <paramref name="inputPath"/> into a 16 kHz / 16-bit / mono WAV file at
    /// <paramref name="outputWavPath"/>, overwriting any existing file.
    /// </summary>
    /// <param name="inputPath">Path of the source audio file (any format the platform can decode).</param>
    /// <param name="outputWavPath">Path of the WAV file to write.</param>
    /// <param name="cancellationToken">Cancels the decode.</param>
    /// <exception cref="InvalidOperationException">The source could not be decoded.</exception>
    Task DecodeToWav16kMonoAsync(string inputPath, string outputWavPath, CancellationToken cancellationToken);
}
