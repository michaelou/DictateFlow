using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Core.Services.Transcription;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Providers.AzureSpeech;

/// <summary>
/// <see cref="ITranscriptionProvider"/> and <see cref="IStreamingTranscriptionProvider"/>
/// backed by the Azure AI Speech
/// <see href="https://learn.microsoft.com/azure/ai-services/speech-service/how-to-recognize-speech">real-time speech-to-text</see>
/// API through the Speech SDK. The streaming implementation pushes live PCM into a websocket
/// recognition session and yields the running transcript; the non-streaming implementation
/// runs the completed capture through the same session type. Reads its
/// <see cref="AzureSpeechTranscriptionConfig"/> section on every call, maps all failures to
/// <see cref="ProviderException"/>, and never logs the API key or the transcript above Debug.
/// </summary>
public sealed class AzureSpeechTranscriptionProvider : ITranscriptionProvider, IStreamingTranscriptionProvider
{
    private const string ProviderName = AzureSpeechProviders.RegistrationName;

    /// <summary>Size of the WAV header written by the recorder; stripped before pushing PCM.</summary>
    private const int WavHeaderBytes = 44;

    /// <summary>Bytes of PCM data per second for 16 kHz × 16-bit × mono audio.</summary>
    private const int BytesPerSecond = 16000 * 2;

    private readonly IProviderConfigReader _configReader;
    private readonly IUsageSink _usageSink;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AzureSpeechTranscriptionProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="AzureSpeechTranscriptionProvider"/> class.</summary>
    /// <param name="configReader">Supplies the endpoint, key and languages, read per call.</param>
    /// <param name="usageSink">Receives the audio duration after each completed recognition.</param>
    /// <param name="timeProvider">Timestamps usage records (replaceable in tests).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public AzureSpeechTranscriptionProvider(
        IProviderConfigReader configReader,
        IUsageSink usageSink,
        TimeProvider timeProvider,
        ILogger<AzureSpeechTranscriptionProvider> logger)
    {
        _configReader = configReader;
        _usageSink = usageSink;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TranscriptionResult> TranscribeAsync(Stream audio, CancellationToken cancellationToken)
    {
        var config = ReadConfig();
        var audioBytes = await ReadAllBytesAsync(audio, cancellationToken).ConfigureAwait(false);
        var duration = Math.Max(0, audioBytes.Length - WavHeaderBytes) / (double)BytesPerSecond;

        // The user-facing timeout applies to the whole-capture path only; streaming sessions
        // last as long as the recording and are bounded by the caller instead.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (config.TimeoutSeconds > 0)
        {
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));
        }

        var text = "";
        try
        {
            // The completed capture runs through the same streaming recognition as one chunk,
            // so both paths share a single code path (and a single usage record).
            await foreach (var update in TranscribeStreamingAsync(WholeCapture(audioBytes), timeoutCts.Token)
                .ConfigureAwait(false))
            {
                if (update.IsFinal)
                {
                    text = update.Text;
                }
            }
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
                $"The transcription timed out after {config.TimeoutSeconds} s. Try again, or raise the timeout in Settings → Speech.",
                ex);
        }
        catch (Exception ex)
        {
            // Defensive: nothing rawer than ProviderException may escape the provider.
            throw new ProviderException(ProviderName, $"Transcription failed: {ex.Message}", ex);
        }

        var locales = ParseLocales(config.Language);
        return new TranscriptionResult(text, duration, locales is [var single] ? single : null);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TranscriptionUpdate> TranscribeStreamingAsync(
        IAsyncEnumerable<AudioChunk> audio,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var config = ReadConfig();
        var run = new RecognitionRun(BuildSpeechConfig(config), ParseLocales(config.Language), _logger);
        await using (run.ConfigureAwait(false))
        {
            await run.StartAsync().ConfigureAwait(false);

            // Feed audio on the side; recognition updates flow independently of the pushes.
            var pump = Task.Run(
                async () =>
                {
                    try
                    {
                        await foreach (var chunk in audio.WithCancellation(cancellationToken).ConfigureAwait(false))
                        {
                            run.PushAudio(chunk);
                        }
                    }
                    finally
                    {
                        run.CompleteAudio(); // end-of-stream tells the service to finalize
                    }
                },
                cancellationToken);

            // Completes when the service ends the session; a service error surfaces here as
            // the ProviderException the run completed the channel with.
            await foreach (var update in run.Updates.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }

            await pump.ConfigureAwait(false);

            RecordUsage(run.BytesPushed);
            _logger.LogDebug(
                "Streaming recognition finished: {CharCount} characters from {AudioBytes} audio bytes",
                run.FinalText.Length, run.BytesPushed);
            yield return new TranscriptionUpdate(run.FinalText, IsFinal: true);
        }
    }

    /// <summary>Reads the provider config section (per call, so edits apply live).</summary>
    private AzureSpeechTranscriptionConfig ReadConfig()
        => _configReader.GetConfig<AzureSpeechTranscriptionConfig>(ProviderKind.Transcription, ProviderName);

    /// <summary>Builds the SDK config from settings, validating endpoint and key.</summary>
    private static SpeechConfig BuildSpeechConfig(AzureSpeechTranscriptionConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new ProviderException(
                ProviderName, "No API key is configured. Enter your Speech resource API key in Settings → Speech.",
                isConfigurationError: true);
        }

        var endpointText = config.Endpoint.Trim();
        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new ProviderException(
                ProviderName,
                $"'{config.Endpoint}' is not a valid http(s) URL. Check the endpoint in Settings → Speech.",
                isConfigurationError: true);
        }

        return SpeechConfig.FromEndpoint(endpoint, config.ApiKey);
    }

    /// <summary>
    /// Splits the configured language setting (comma-separated BCP-47 tags) into the
    /// candidate locale list; empty when unset, which keeps the service default.
    /// </summary>
    private static IReadOnlyList<string> ParseLocales(string language)
        => language.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Records the recognized audio duration for the cost dashboard.</summary>
    private void RecordUsage(long pcmBytes)
    {
        if (pcmBytes <= 0)
        {
            return;
        }

        _usageSink.Record(new UsageRecord(
            _timeProvider.GetUtcNow().UtcDateTime,
            UsageCategories.Speech,
            pcmBytes / (double)BytesPerSecond,
            PromptTokens: null,
            CompletionTokens: null));
    }

    /// <summary>Presents a completed WAV capture as a single PCM chunk (header stripped).</summary>
    private static async IAsyncEnumerable<AudioChunk> WholeCapture(byte[] wavBytes)
    {
        await Task.CompletedTask;
        if (wavBytes.Length > WavHeaderBytes)
        {
            yield return new AudioChunk(wavBytes.AsMemory(WavHeaderBytes));
        }
    }

    /// <summary>Copies the audio stream (from its current position) into a byte array.</summary>
    private static async Task<byte[]> ReadAllBytesAsync(Stream audio, CancellationToken cancellationToken)
    {
        if (audio is MemoryStream memory && memory.Position == 0 && memory.TryGetBuffer(out var buffer))
        {
            return buffer.Array is not null && buffer.Offset == 0 && buffer.Count == buffer.Array.Length
                ? buffer.Array
                : buffer.AsSpan().ToArray();
        }

        using var copy = new MemoryStream();
        await audio.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
        return copy.ToArray();
    }

    /// <summary>
    /// One continuous-recognition session: owns the push stream and recognizer, translates
    /// the SDK's <c>Recognizing</c>/<c>Recognized</c> events into
    /// <see cref="TranscriptionUpdate"/>s on <see cref="Updates"/>, and completes the channel
    /// (with a <see cref="ProviderException"/> on a service error) when the session ends.
    /// </summary>
    private sealed class RecognitionRun : IAsyncDisposable
    {
        private readonly PushAudioInputStream _pushStream;
        private readonly AudioConfig _audioConfig;
        private readonly SpeechRecognizer _recognizer;
        private readonly ILogger _logger;
        private readonly Channel<TranscriptionUpdate> _updates = Channel.CreateUnbounded<TranscriptionUpdate>(
            new UnboundedChannelOptions { SingleReader = true });

        /// <summary>Finalized segment texts, in order; guarded by the lock (SDK events fire on worker threads).</summary>
        private readonly List<string> _segments = [];
        private readonly object _gate = new();

        private long _bytesPushed;
        private bool _audioCompleted;
        private bool _started;

        public RecognitionRun(SpeechConfig speechConfig, IReadOnlyList<string> locales, ILogger logger)
        {
            _logger = logger;

            // The recorder captures 16 kHz / 16-bit / mono PCM — exactly what the service wants.
            _pushStream = AudioInputStream.CreatePushStream(AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1));
            _audioConfig = AudioConfig.FromStreamInput(_pushStream);

            if (locales.Count == 1)
            {
                speechConfig.SpeechRecognitionLanguage = locales[0];
                _recognizer = new SpeechRecognizer(speechConfig, _audioConfig);
            }
            else if (locales.Count > 1)
            {
                // Continuous language identification restricted to the configured candidates,
                // so mixed-language dictation keeps recognizing mid-session.
                speechConfig.SetProperty(PropertyId.SpeechServiceConnection_LanguageIdMode, "Continuous");
                _recognizer = new SpeechRecognizer(
                    speechConfig, AutoDetectSourceLanguageConfig.FromLanguages([.. locales]), _audioConfig);
            }
            else
            {
                _recognizer = new SpeechRecognizer(speechConfig, _audioConfig);
            }

            _recognizer.Recognizing += OnRecognizing;
            _recognizer.Recognized += OnRecognized;
            _recognizer.Canceled += OnCanceled;
            _recognizer.SessionStopped += OnSessionStopped;
        }

        /// <summary>Gets the partial-update stream; completes when the recognition session ends.</summary>
        public ChannelReader<TranscriptionUpdate> Updates => _updates.Reader;

        /// <summary>Gets the finalized transcript accumulated so far.</summary>
        public string FinalText
        {
            get
            {
                lock (_gate)
                {
                    return string.Join(" ", _segments);
                }
            }
        }

        /// <summary>Gets the number of PCM bytes pushed, for duration/cost accounting.</summary>
        public long BytesPushed => Interlocked.Read(ref _bytesPushed);

        /// <summary>Starts continuous recognition, mapping a start failure to <see cref="ProviderException"/>.</summary>
        public async Task StartAsync()
        {
            try
            {
                await _recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);
                _started = true;
            }
            catch (Exception ex)
            {
                throw new ProviderException(
                    ProviderName,
                    $"Could not start speech recognition: {ex.Message}. Check the endpoint and API key in Settings → Speech.",
                    ex,
                    isConfigurationError: true);
            }
        }

        /// <summary>Pushes one chunk of live PCM into the recognition session.</summary>
        public void PushAudio(AudioChunk chunk)
        {
            if (chunk.Pcm.IsEmpty)
            {
                return;
            }

            if (MemoryMarshal.TryGetArray(chunk.Pcm, out var segment) && segment is { Array: not null, Offset: 0 })
            {
                _pushStream.Write(segment.Array, segment.Count);
            }
            else
            {
                _pushStream.Write(chunk.Pcm.ToArray());
            }

            Interlocked.Add(ref _bytesPushed, chunk.Pcm.Length);
        }

        /// <summary>Signals end-of-audio; the service finalizes the transcript and stops the session.</summary>
        public void CompleteAudio()
        {
            lock (_gate)
            {
                if (_audioCompleted)
                {
                    return;
                }

                _audioCompleted = true;
            }

            _pushStream.Close();
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            CompleteAudio();
            _updates.Writer.TryComplete();

            if (_started)
            {
                try
                {
                    await _recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Stopping speech recognition failed during cleanup");
                }
            }

            _recognizer.Recognizing -= OnRecognizing;
            _recognizer.Recognized -= OnRecognized;
            _recognizer.Canceled -= OnCanceled;
            _recognizer.SessionStopped -= OnSessionStopped;
            _recognizer.Dispose();
            _audioConfig.Dispose();
            _pushStream.Dispose();
        }

        /// <summary>An in-flight hypothesis: finalized segments plus the current guess.</summary>
        private void OnRecognizing(object? sender, SpeechRecognitionEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Result.Text))
            {
                return;
            }

            string text;
            lock (_gate)
            {
                text = _segments.Count == 0 ? e.Result.Text : $"{string.Join(" ", _segments)} {e.Result.Text}";
            }

            _updates.Writer.TryWrite(new TranscriptionUpdate(text, IsFinal: false));
        }

        /// <summary>A finalized segment (the service closed one utterance).</summary>
        private void OnRecognized(object? sender, SpeechRecognitionEventArgs e)
        {
            if (e.Result.Reason != ResultReason.RecognizedSpeech || string.IsNullOrWhiteSpace(e.Result.Text))
            {
                return; // NoMatch (silence) contributes nothing.
            }

            string text;
            lock (_gate)
            {
                _segments.Add(e.Result.Text.Trim());
                text = string.Join(" ", _segments);
            }

            _updates.Writer.TryWrite(new TranscriptionUpdate(text, IsFinal: false));
        }

        /// <summary>End-of-stream is normal completion; anything else fails the session.</summary>
        private void OnCanceled(object? sender, SpeechRecognitionCanceledEventArgs e)
        {
            if (e.Reason != CancellationReason.Error)
            {
                _updates.Writer.TryComplete();
                return;
            }

            _logger.LogWarning(
                "Speech recognition canceled: {ErrorCode} — {ErrorDetails}", e.ErrorCode, e.ErrorDetails);
            _updates.Writer.TryComplete(MapError(e.ErrorCode, e.ErrorDetails));
        }

        private void OnSessionStopped(object? sender, SessionEventArgs e) => _updates.Writer.TryComplete();

        /// <summary>Maps an SDK cancellation to a user-presentable <see cref="ProviderException"/>.</summary>
        private static ProviderException MapError(CancellationErrorCode errorCode, string errorDetails)
            => errorCode switch
            {
                CancellationErrorCode.AuthenticationFailure or CancellationErrorCode.Forbidden
                    => new ProviderException(
                        ProviderName,
                        "The speech service rejected the credentials. Check your API key in Settings → Speech.",
                        isConfigurationError: true),
                CancellationErrorCode.ConnectionFailure
                    => new ProviderException(
                        ProviderName,
                        "Could not reach the speech service. Check the endpoint in Settings → Speech and your network connection.",
                        isConfigurationError: true),
                _ => new ProviderException(
                    ProviderName, $"Speech recognition failed ({errorCode}). {Shorten(errorDetails)}"),
            };

        /// <summary>Trims service error details for inline display.</summary>
        private static string Shorten(string details)
        {
            details = details.Trim();
            return details.Length <= 200 ? details : $"{details[..200]}…";
        }
    }
}
