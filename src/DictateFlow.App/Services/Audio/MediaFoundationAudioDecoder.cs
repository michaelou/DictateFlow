using DictateFlow.Core.Services.Audio;
using Microsoft.Extensions.Logging;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace DictateFlow.App.Services.Audio;

/// <summary>
/// <see cref="IAudioDecoder"/> backed by Windows Media Foundation (via NAudio). Decodes any
/// format Media Foundation supports (notably <c>.m4a</c>/AAC uploaded by the mobile app) and
/// resamples it to the 16 kHz / 16-bit / mono WAV the transcription providers expect — the same
/// format <c>NAudioRecorder</c> captures.
/// </summary>
public sealed class MediaFoundationAudioDecoder : IAudioDecoder
{
    /// <summary>The format the transcription providers require (matches <c>NAudioRecorder.CaptureFormat</c>).</summary>
    private static readonly WaveFormat TargetFormat = new(16000, 16, 1);

    private readonly ILogger<MediaFoundationAudioDecoder> _logger;

    /// <summary>Initializes a new instance of the <see cref="MediaFoundationAudioDecoder"/> class.</summary>
    /// <param name="logger">Receives diagnostic output.</param>
    public MediaFoundationAudioDecoder(ILogger<MediaFoundationAudioDecoder> logger)
    {
        _logger = logger;

        // Idempotent; ensures the MF platform is initialized before the first decode.
        MediaFoundationApi.Startup();
    }

    /// <inheritdoc />
    public Task DecodeToWav16kMonoAsync(string inputPath, string outputWavPath, CancellationToken cancellationToken)
        // The NAudio calls are synchronous and CPU/IO-bound; run them off the caller's thread.
        => Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var reader = new MediaFoundationReader(inputPath);
                    using var resampler = new MediaFoundationResampler(reader, TargetFormat) { ResamplerQuality = 60 };
                    WaveFileWriter.CreateWaveFile(outputWavPath, resampler);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not decode '{Input}' to WAV", inputPath);
                    throw new InvalidOperationException($"Could not decode the recording: {ex.Message}", ex);
                }

                _logger.LogDebug("Decoded '{Input}' to 16 kHz mono WAV '{Output}'", inputPath, outputWavPath);
            },
            cancellationToken);
}
