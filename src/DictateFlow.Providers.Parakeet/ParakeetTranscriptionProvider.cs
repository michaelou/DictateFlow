using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Core.Services.Transcription;
using Microsoft.Extensions.Logging;
using SherpaOnnx;

namespace DictateFlow.Providers.Parakeet;

/// <summary>
/// <see cref="ITranscriptionProvider"/> backed by NVIDIA's
/// <see href="https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3">Parakeet TDT 0.6B v3</see>
/// model running in-process through the sherpa-onnx runtime — fully offline, multilingual
/// (25 European languages, auto-detected). The recognizer is loaded lazily on first use and
/// cached until the model files change, so the ~660 MB load cost is paid once per session.
/// Reads its <see cref="ParakeetTranscriptionConfig"/> section on every call and maps all
/// failures to <see cref="ProviderException"/>.
/// </summary>
public sealed class ParakeetTranscriptionProvider : ITranscriptionProvider, IDisposable
{
    private const string ProviderName = ParakeetProviders.RegistrationName;

    private readonly ParakeetModelManager _modelManager;
    private readonly IProviderConfigReader _configReader;
    private readonly IUsageSink _usageSink;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ParakeetTranscriptionProvider> _logger;

    private readonly object _recognizerLock = new();
    private OfflineRecognizer? _recognizer;
    private string? _recognizerCacheKey;

    /// <summary>Initializes a new instance of the <see cref="ParakeetTranscriptionProvider"/> class.</summary>
    /// <param name="modelManager">Locates the installed model files.</param>
    /// <param name="configReader">Supplies the threads and timeout, read per call.</param>
    /// <param name="usageSink">Receives the audio duration after each successful call.</param>
    /// <param name="timeProvider">Timestamps usage records (replaceable in tests).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public ParakeetTranscriptionProvider(
        ParakeetModelManager modelManager,
        IProviderConfigReader configReader,
        IUsageSink usageSink,
        TimeProvider timeProvider,
        ILogger<ParakeetTranscriptionProvider> logger)
    {
        _modelManager = modelManager;
        _configReader = configReader;
        _usageSink = usageSink;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TranscriptionResult> TranscribeAsync(Stream audio, CancellationToken cancellationToken)
    {
        var config = _configReader.GetConfig<ParakeetTranscriptionConfig>(ProviderKind.Transcription, ProviderName);
        if (!_modelManager.IsFullyInstalled())
        {
            throw new ProviderException(
                ProviderName,
                $"Local transcription is not installed. Download the {ParakeetModelCatalog.ModelDisplayName} files in Settings → Local Models.",
                isConfigurationError: true);
        }

        // The user-facing timeout: enforced here so a changed TimeoutSeconds takes effect
        // immediately, without a restart.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (config.TimeoutSeconds > 0)
        {
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));
        }

        try
        {
            var (samples, sampleRate) = await ReadWavAsync(audio, timeoutCts.Token).ConfigureAwait(false);
            var duration = samples.Length / (double)sampleRate;

            var stopwatch = Stopwatch.StartNew();
            // ONNX inference cannot be interrupted mid-run; WaitAsync abandons the decode on
            // cancellation/timeout and the background task finishes on its own. The cached
            // recognizer stays valid, so an abandoned run leaks nothing but CPU time.
            var text = await Task
                .Run(() => Decode(samples, sampleRate, config), CancellationToken.None)
                .WaitAsync(timeoutCts.Token)
                .ConfigureAwait(false);
            stopwatch.Stop();
            _logger.LogDebug(
                "Parakeet finished: {DurationSeconds:F1} s of audio in {ElapsedMs} ms",
                duration, stopwatch.ElapsedMilliseconds);

            _usageSink.Record(new UsageRecord(
                _timeProvider.GetUtcNow().UtcDateTime,
                UsageCategories.Speech,
                duration,
                PromptTokens: null,
                CompletionTokens: null));

            _logger.LogDebug("Transcript received: {CharCount} characters, {DurationSeconds:F1} s of audio", text.Length, duration);
            return new TranscriptionResult(text, duration, Language: null);
        }
        catch (ProviderException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Caller-initiated cancellation is not a provider failure.
        }
        catch (OperationCanceledException ex)
        {
            throw new ProviderException(
                ProviderName,
                $"Local transcription timed out after {config.TimeoutSeconds} s. Raise the timeout in Settings → Speech, or free up CPU.",
                ex);
        }
        catch (Exception ex)
        {
            // Defensive: nothing rawer than ProviderException may escape the provider.
            throw new ProviderException(ProviderName, $"Local transcription failed: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_recognizerLock)
        {
            _recognizer?.Dispose();
            _recognizer = null;
            _recognizerCacheKey = null;
        }
    }

    /// <summary>Runs one recognition through the cached recognizer.</summary>
    private string Decode(float[] samples, int sampleRate, ParakeetTranscriptionConfig config)
    {
        var recognizer = GetOrCreateRecognizer(config);
        using var stream = recognizer.CreateStream();
        stream.AcceptWaveform(sampleRate, samples);
        recognizer.Decode(stream);
        return stream.Result.Text?.Trim() ?? "";
    }

    /// <summary>
    /// Returns the cached recognizer, rebuilding it when the model files or the thread
    /// setting changed since the last load. Loading reads ~660 MB from disk, so the instance
    /// is kept for the lifetime of the provider (a singleton).
    /// </summary>
    private OfflineRecognizer GetOrCreateRecognizer(ParakeetTranscriptionConfig config)
    {
        var threads = config.Threads > 0
            ? config.Threads
            : Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
        var cacheKey = BuildCacheKey(threads);

        lock (_recognizerLock)
        {
            if (_recognizer is not null && _recognizerCacheKey == cacheKey)
            {
                return _recognizer;
            }

            _recognizer?.Dispose();
            _recognizer = null;
            _recognizerCacheKey = null;

            var recognizerConfig = new OfflineRecognizerConfig();
            recognizerConfig.ModelConfig.Transducer.Encoder = _modelManager.GetModelPath(ParakeetModelCatalog.Encoder);
            recognizerConfig.ModelConfig.Transducer.Decoder = _modelManager.GetModelPath(ParakeetModelCatalog.Decoder);
            recognizerConfig.ModelConfig.Transducer.Joiner = _modelManager.GetModelPath(ParakeetModelCatalog.Joiner);
            recognizerConfig.ModelConfig.Tokens = _modelManager.GetModelPath(ParakeetModelCatalog.Tokens);
            recognizerConfig.ModelConfig.ModelType = "nemo_transducer";
            recognizerConfig.ModelConfig.NumThreads = threads;

            var stopwatch = Stopwatch.StartNew();
            _recognizer = new OfflineRecognizer(recognizerConfig);
            _recognizerCacheKey = cacheKey;
            stopwatch.Stop();
            _logger.LogInformation(
                "Loaded {ModelDisplayName} with {Threads} threads in {ElapsedMs} ms",
                ParakeetModelCatalog.ModelDisplayName, threads, stopwatch.ElapsedMilliseconds);
            return _recognizer;
        }
    }

    /// <summary>Fingerprints the installed model files and thread setting for cache invalidation.</summary>
    private string BuildCacheKey(int threads)
    {
        var builder = new StringBuilder().Append(threads);
        foreach (var component in ParakeetModelCatalog.All)
        {
            var info = new FileInfo(_modelManager.GetModelPath(component));
            builder.Append('|').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads a PCM WAV stream (from its current position) into normalized float samples.
    /// The pipeline records 16 kHz/16-bit/mono, but the parser tolerates any sample rate
    /// (sherpa-onnx resamples internally) and averages multi-channel audio down to mono.
    /// </summary>
    private static async Task<(float[] Samples, int SampleRate)> ReadWavAsync(
        Stream audio, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await audio.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        var bytes = memory.ToArray();

        if (bytes.Length < 12
            || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new ProviderException(ProviderName, "The recording is not a valid WAV file.");
        }

        int channels = 0, sampleRate = 0, bitsPerSample = 0;
        ReadOnlyMemory<byte> data = default;

        // Walk the RIFF chunks; only "fmt " and "data" are consumed.
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var chunkId = bytes.AsSpan(offset, 4);
            var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            var chunkStart = offset + 8;
            if (chunkSize < 0 || chunkStart > bytes.Length)
            {
                break;
            }

            var available = Math.Min(chunkSize, bytes.Length - chunkStart);
            if (chunkId.SequenceEqual("fmt "u8) && available >= 16)
            {
                var format = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(chunkStart, 2));
                if (format != 1)
                {
                    throw new ProviderException(ProviderName, $"Unsupported WAV format {format}; only 16-bit PCM is supported.");
                }

                channels = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(chunkStart + 2, 2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(chunkStart + 4, 4));
                bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(chunkStart + 14, 2));
            }
            else if (chunkId.SequenceEqual("data"u8))
            {
                data = bytes.AsMemory(chunkStart, available);
            }

            offset = chunkStart + chunkSize + (chunkSize % 2); // chunks are word-aligned
        }

        if (channels <= 0 || sampleRate <= 0 || bitsPerSample != 16)
        {
            throw new ProviderException(ProviderName, "The recording is not a supported 16-bit PCM WAV file.");
        }

        var pcm = data.Span;
        var frameCount = pcm.Length / (2 * channels);
        var samples = new float[frameCount];
        for (var frame = 0; frame < frameCount; frame++)
        {
            // Average the channels down to mono (the recorder produces mono anyway).
            var sum = 0f;
            for (var channel = 0; channel < channels; channel++)
            {
                var sample = BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice((frame * channels + channel) * 2, 2));
                sum += sample / 32768f;
            }

            samples[frame] = sum / channels;
        }

        return (samples, sampleRate);
    }
}
