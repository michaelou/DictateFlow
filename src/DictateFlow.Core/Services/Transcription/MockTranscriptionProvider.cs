using System.Runtime.CompilerServices;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Providers;

namespace DictateFlow.Core.Services.Transcription;

/// <summary>Configuration section (<c>Providers.Transcription.Mock</c>) of <see cref="MockTranscriptionProvider"/>.</summary>
public sealed class MockTranscriptionConfig
{
    /// <summary>Gets or sets the artificial processing delay in milliseconds.</summary>
    public int DelayMs { get; set; } = 300;

    /// <summary>Gets or sets the text every transcription returns.</summary>
    public string Text { get; set; } =
        "This is mock transcript text — configure a speech provider in Settings to enable real transcription.";
}

/// <summary>
/// Fake <see cref="ITranscriptionProvider"/> that returns configurable canned text after an
/// optional delay, so the whole dictation flow is demoable without any cloud service. Reads
/// its <see cref="MockTranscriptionConfig"/> section on every call, so edits apply live.
/// Also implements <see cref="IStreamingTranscriptionProvider"/>, revealing the canned text
/// word by word while recording runs, so streaming transcription is demoable too.
/// </summary>
public sealed class MockTranscriptionProvider : ITranscriptionProvider, IStreamingTranscriptionProvider
{
    /// <summary>The name this provider is registered and configured under.</summary>
    public const string RegistrationName = "Mock";

    /// <summary>Bytes of PCM data per second for 16 kHz × 16-bit × mono audio.</summary>
    private const int BytesPerSecond = 16000 * 2;

    /// <summary>Size of the WAV header written by the recorder.</summary>
    private const int WavHeaderBytes = 44;

    /// <summary>How often the streaming mock reveals the next word of the canned text.</summary>
    private const int StreamingWordIntervalMs = 200;

    private readonly IProviderConfigReader _configReader;

    /// <summary>Initializes a new instance of the <see cref="MockTranscriptionProvider"/> class.</summary>
    /// <param name="configReader">Supplies the <c>Providers.Transcription.Mock</c> section, read per call.</param>
    public MockTranscriptionProvider(IProviderConfigReader configReader)
    {
        _configReader = configReader;
    }

    /// <inheritdoc />
    public async Task<TranscriptionResult> TranscribeAsync(Stream audio, CancellationToken cancellationToken)
    {
        var config = _configReader.GetConfig<MockTranscriptionConfig>(ProviderKind.Transcription, RegistrationName);
        if (config.DelayMs > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(config.DelayMs), cancellationToken).ConfigureAwait(false);
        }

        double? duration = audio.CanSeek
            ? Math.Max(0, audio.Length - WavHeaderBytes) / (double)BytesPerSecond
            : null;

        return new TranscriptionResult(config.Text, duration, Language: null);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TranscriptionUpdate> TranscribeStreamingAsync(
        IAsyncEnumerable<AudioChunk> audio,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var config = _configReader.GetConfig<MockTranscriptionConfig>(ProviderKind.Transcription, RegistrationName);
        var words = config.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // The mock ignores the samples, but the audio sequence must still be consumed — its
        // completion is the "recording stopped" signal, just like for a real provider.
        var recordingStopped = Task.Run(
            async () =>
            {
                await foreach (var _ in audio.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                }
            },
            cancellationToken);

        var revealed = 0;
        while (revealed < words.Length)
        {
            await Task.WhenAny(recordingStopped, Task.Delay(StreamingWordIntervalMs, cancellationToken))
                .ConfigureAwait(false);
            if (recordingStopped.IsCompleted)
            {
                break;
            }

            revealed++;
            yield return new TranscriptionUpdate(string.Join(' ', words.Take(revealed)), IsFinal: false);
        }

        await recordingStopped.ConfigureAwait(false);
        yield return new TranscriptionUpdate(config.Text, IsFinal: true);
    }
}
