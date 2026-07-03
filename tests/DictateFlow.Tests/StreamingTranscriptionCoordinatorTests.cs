using System.Runtime.CompilerServices;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Core.Services.Transcription;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="StreamingTranscriptionCoordinator"/>: when streaming applies, the
/// audio/update bridging of the session, and the null-result fallback contract on failures.
/// </summary>
public sealed class StreamingTranscriptionCoordinatorTests
{
    private readonly Mock<IProviderRegistry> _registry = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly AppSettings _appSettings = new();

    public StreamingTranscriptionCoordinatorTests()
    {
        _settings.SetupGet(s => s.Current).Returns(_appSettings);
        _appSettings.Recording.EnableStreamingTranscription = true;
    }

    private StreamingTranscriptionCoordinator CreateCoordinator()
        => new(_registry.Object, _settings.Object, NullLoggerFactory.Instance);

    [Fact]
    public void TryBegin_StreamingDisabled_ReturnsNull()
    {
        _appSettings.Recording.EnableStreamingTranscription = false;
        _registry.Setup(r => r.ResolveTranscription()).Returns(new StreamingFakeProvider());

        Assert.Null(CreateCoordinator().TryBegin());
        _registry.Verify(r => r.ResolveTranscription(), Times.Never);
    }

    [Fact]
    public void TryBegin_ProviderWithoutStreamingSupport_ReturnsNull()
    {
        _registry.Setup(r => r.ResolveTranscription()).Returns(Mock.Of<ITranscriptionProvider>());

        Assert.Null(CreateCoordinator().TryBegin());
    }

    [Fact]
    public void TryBegin_ProviderResolutionFails_ReturnsNull()
    {
        _registry.Setup(r => r.ResolveTranscription())
            .Throws(new ProviderException("Speech", "not configured", isConfigurationError: true));

        Assert.Null(CreateCoordinator().TryBegin());
    }

    [Fact]
    public async Task Session_DeliversAudioToTheProviderAndRaisesPartials()
    {
        var provider = new StreamingFakeProvider();
        _registry.Setup(r => r.ResolveTranscription()).Returns(provider);
        await using var session = CreateCoordinator().TryBegin()!;
        var partials = new List<string>();
        session.PartialTranscriptChanged += (_, text) => partials.Add(text);

        session.AddAudio(new AudioChunk(new byte[] { 1 }));
        session.AddAudio(new AudioChunk(new byte[] { 2, 3 }));
        var final = await session.CompleteAsync();

        // The fake echoes one update per chunk plus a final summary once the audio completes.
        Assert.Equal("done: 2 chunks", final);
        Assert.Contains("chunk 1", partials);
        Assert.Contains("chunk 2", partials);
        Assert.Contains("done: 2 chunks", partials);
    }

    [Fact]
    public async Task Session_ProviderEndsWithoutFinalUpdate_LatestTextWins()
    {
        var provider = new StreamingFakeProvider { EmitFinal = false };
        _registry.Setup(r => r.ResolveTranscription()).Returns(provider);
        await using var session = CreateCoordinator().TryBegin()!;

        session.AddAudio(new AudioChunk(new byte[] { 1 }));
        var final = await session.CompleteAsync();

        Assert.Equal("chunk 1", final);
    }

    [Fact]
    public async Task Session_ProviderProducesNoUpdates_CompletesWithNull()
    {
        var provider = new StreamingFakeProvider { EmitPartials = false, EmitFinal = false };
        _registry.Setup(r => r.ResolveTranscription()).Returns(provider);
        await using var session = CreateCoordinator().TryBegin()!;

        Assert.Null(await session.CompleteAsync());
    }

    [Fact]
    public async Task Session_ProviderProducesOnlyWhitespace_CompletesWithNull()
    {
        var provider = new StreamingFakeProvider { FinalText = "   " };
        _registry.Setup(r => r.ResolveTranscription()).Returns(provider);
        await using var session = CreateCoordinator().TryBegin()!;

        Assert.Null(await session.CompleteAsync()); // empty text falls back to standard transcription
    }

    [Fact]
    public async Task Session_ProviderThrows_CompletesWithNull()
    {
        var provider = new StreamingFakeProvider { Failure = new ProviderException("Speech", "socket dropped") };
        _registry.Setup(r => r.ResolveTranscription()).Returns(provider);
        await using var session = CreateCoordinator().TryBegin()!;

        session.AddAudio(new AudioChunk(new byte[] { 1 }));

        Assert.Null(await session.CompleteAsync());
    }

    /// <summary>
    /// Streaming provider double: echoes one partial per received chunk and, when the audio
    /// completes, a final summary — or throws when <see cref="Failure"/> is set.
    /// </summary>
    private sealed class StreamingFakeProvider : ITranscriptionProvider, IStreamingTranscriptionProvider
    {
        public bool EmitPartials { get; init; } = true;

        public bool EmitFinal { get; init; } = true;

        public string? FinalText { get; init; }

        public Exception? Failure { get; init; }

        public Task<TranscriptionResult> TranscribeAsync(Stream audio, CancellationToken cancellationToken)
            => Task.FromResult(new TranscriptionResult("non-streaming", null, null));

        public async IAsyncEnumerable<TranscriptionUpdate> TranscribeStreamingAsync(
            IAsyncEnumerable<AudioChunk> audio,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var chunks = 0;
            await foreach (var _ in audio.WithCancellation(cancellationToken))
            {
                chunks++;
                if (Failure is not null)
                {
                    throw Failure;
                }

                if (EmitPartials)
                {
                    yield return new TranscriptionUpdate($"chunk {chunks}", IsFinal: false);
                }
            }

            if (EmitFinal)
            {
                yield return new TranscriptionUpdate(FinalText ?? $"done: {chunks} chunks", IsFinal: true);
            }
        }
    }
}
