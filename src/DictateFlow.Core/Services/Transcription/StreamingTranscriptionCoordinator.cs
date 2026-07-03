using System.Threading.Channels;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Providers;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Transcription;

/// <summary>
/// Default <see cref="IStreamingTranscriptionCoordinator"/> implementation. Resolves the
/// active transcription provider per recording (so settings changes apply to the very next
/// dictation) and starts a session only when streaming is enabled in settings and the
/// provider implements <see cref="IStreamingTranscriptionProvider"/>. Every failure path
/// returns <see langword="null"/> instead of throwing — streaming is an optimization, and
/// the standard workflow must always remain available as the fallback.
/// </summary>
public sealed class StreamingTranscriptionCoordinator : IStreamingTranscriptionCoordinator
{
    private readonly IProviderRegistry _registry;
    private readonly ISettingsService _settingsService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<StreamingTranscriptionCoordinator> _logger;

    /// <summary>Initializes a new instance of the <see cref="StreamingTranscriptionCoordinator"/> class.</summary>
    /// <param name="registry">Resolves the active transcription provider, per session.</param>
    /// <param name="settingsService">Supplies the streaming on/off setting.</param>
    /// <param name="loggerFactory">Creates the per-session logger.</param>
    public StreamingTranscriptionCoordinator(
        IProviderRegistry registry,
        ISettingsService settingsService,
        ILoggerFactory loggerFactory)
    {
        _registry = registry;
        _settingsService = settingsService;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<StreamingTranscriptionCoordinator>();
    }

    /// <inheritdoc />
    public IStreamingTranscriptionSession? TryBegin()
    {
        if (!_settingsService.Current.Recording.EnableStreamingTranscription)
        {
            return null;
        }

        ITranscriptionProvider provider;
        try
        {
            provider = _registry.ResolveTranscription();
        }
        catch (Exception ex)
        {
            // An unresolvable provider fails the same way in the standard workflow, where the
            // failure surfaces to the user — no need to duplicate the reporting here.
            _logger.LogDebug(ex, "Streaming skipped: the active transcription provider could not be resolved");
            return null;
        }

        if (provider is not IStreamingTranscriptionProvider streamingProvider)
        {
            _logger.LogDebug(
                "Streaming skipped: provider {ProviderType} does not support streaming", provider.GetType().Name);
            return null;
        }

        _logger.LogDebug("Streaming session started against {ProviderType}", provider.GetType().Name);
        return new Session(streamingProvider, _loggerFactory.CreateLogger<Session>());
    }

    /// <summary>
    /// The session: bridges push-style audio callbacks into the provider's pull-style
    /// <see cref="IAsyncEnumerable{T}"/> through an unbounded channel and consumes the
    /// update stream on a background task.
    /// </summary>
    private sealed class Session : IStreamingTranscriptionSession
    {
        /// <summary>How long <see cref="CompleteAsync"/> waits for the final transcript before giving up.</summary>
        private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(30);

        private readonly Channel<AudioChunk> _audio = Channel.CreateUnbounded<AudioChunk>(
            new UnboundedChannelOptions { SingleReader = true });

        private readonly CancellationTokenSource _cancellation = new();
        private readonly ILogger<Session> _logger;
        private readonly Task _consumeTask;

        private string? _latestText;
        private string? _finalText;

        public Session(IStreamingTranscriptionProvider provider, ILogger<Session> logger)
        {
            _logger = logger;
            _consumeTask = Task.Run(() => ConsumeUpdatesAsync(provider));
        }

        /// <inheritdoc />
        public event EventHandler<string>? PartialTranscriptChanged;

        /// <inheritdoc />
        public void AddAudio(AudioChunk chunk) => _audio.Writer.TryWrite(chunk);

        /// <inheritdoc />
        public async Task<string?> CompleteAsync()
        {
            _audio.Writer.TryComplete();

            var completed = await Task.WhenAny(_consumeTask, Task.Delay(CompletionTimeout))
                .ConfigureAwait(false);
            if (completed != _consumeTask)
            {
                _logger.LogWarning(
                    "Streaming transcription did not finish within {Timeout}; falling back to standard transcription",
                    CompletionTimeout);
                _cancellation.Cancel();
                return null;
            }

            try
            {
                await _consumeTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Streaming transcription failed; falling back to standard transcription");
                return null;
            }

            // An empty result also falls back — the standard workflow may still hear something
            // in the capture, and it already handles silence gracefully.
            var transcript = _finalText ?? _latestText;
            return string.IsNullOrWhiteSpace(transcript) ? null : transcript;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            _audio.Writer.TryComplete();
            _cancellation.Cancel();
            try
            {
                await _consumeTask.ConfigureAwait(false);
            }
            catch
            {
                // The outcome was already reported through CompleteAsync (or abandoned).
            }

            _cancellation.Dispose();
        }

        /// <summary>Drives the provider's update stream, recording the running and final texts.</summary>
        private async Task ConsumeUpdatesAsync(IStreamingTranscriptionProvider provider)
        {
            var updates = provider.TranscribeStreamingAsync(
                _audio.Reader.ReadAllAsync(_cancellation.Token), _cancellation.Token);
            await foreach (var update in updates.WithCancellation(_cancellation.Token).ConfigureAwait(false))
            {
                _latestText = update.Text;
                if (update.IsFinal)
                {
                    _finalText = update.Text;
                }

                PartialTranscriptChanged?.Invoke(this, update.Text);
            }
        }
    }
}
