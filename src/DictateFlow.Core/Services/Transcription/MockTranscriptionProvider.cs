using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Transcription;

/// <summary>
/// Fake <see cref="ITranscriptionProvider"/> that returns configurable canned text after an
/// optional delay. Used when no speech endpoint is configured (so the whole dictation flow
/// is demoable without Azure) and by pipeline tests.
/// </summary>
public sealed class MockTranscriptionProvider : ITranscriptionProvider
{
    /// <summary>Bytes of PCM data per second for 16 kHz × 16-bit × mono audio.</summary>
    private const int BytesPerSecond = 16000 * 2;

    /// <summary>Size of the WAV header written by the recorder.</summary>
    private const int WavHeaderBytes = 44;

    /// <summary>Gets or sets the text every transcription returns.</summary>
    public string CannedText { get; set; } =
        "This is mock transcript text — configure a speech endpoint in Settings to enable real transcription.";

    /// <summary>Gets or sets an artificial processing delay before the result is returned.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <inheritdoc />
    public async Task<TranscriptionResult> TranscribeAsync(Stream audio, CancellationToken cancellationToken)
    {
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
        }

        double? duration = audio.CanSeek
            ? Math.Max(0, audio.Length - WavHeaderBytes) / (double)BytesPerSecond
            : null;

        return new TranscriptionResult(CannedText, duration, Language: null);
    }
}
