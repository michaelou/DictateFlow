using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Core.Services.Transcription;

namespace DictateFlow.Tests;

/// <summary>Tests for <see cref="MockTranscriptionProvider"/>.</summary>
public sealed class MockTranscriptionProviderTests
{
    private readonly MockTranscriptionConfig _config = new() { Text = "hello", DelayMs = 0 };
    private readonly MockTranscriptionProvider _provider;

    public MockTranscriptionProviderTests()
    {
        var configReader = new TestProviderConfigReader()
            .Set(ProviderKind.Transcription, MockTranscriptionProvider.RegistrationName, _config);
        _provider = new MockTranscriptionProvider(configReader);
    }

    [Fact]
    public async Task TranscribeAsync_ReturnsConfiguredTextAndComputedDuration()
    {
        using var wav = SilentWavFactory.Create(TimeSpan.FromSeconds(2));

        var result = await _provider.TranscribeAsync(wav, CancellationToken.None);

        Assert.Equal("hello", result.Text);
        Assert.NotNull(result.AudioDurationSeconds);
        Assert.Equal(2.0, result.AudioDurationSeconds!.Value, precision: 2);
    }

    [Fact]
    public async Task TranscribeAsync_ReadsConfigPerCall_SoEditsApplyLive()
    {
        await _provider.TranscribeAsync(new MemoryStream(), CancellationToken.None);

        _config.Text = "changed";
        var result = await _provider.TranscribeAsync(new MemoryStream(), CancellationToken.None);

        Assert.Equal("changed", result.Text);
    }

    [Fact]
    public async Task TranscribeAsync_HonorsCancellationDuringDelay()
    {
        _config.DelayMs = 30_000;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _provider.TranscribeAsync(new MemoryStream(), cts.Token));
    }

    [Fact]
    public async Task TranscribeStreamingAsync_EndsWithTheFullTextAsFinal()
    {
        _config.Text = "one two three";

        var updates = new List<TranscriptionUpdate>();
        await foreach (var update in _provider.TranscribeStreamingAsync(EmptyAudio(), CancellationToken.None))
        {
            updates.Add(update);
        }

        Assert.NotEmpty(updates);
        var final = updates[^1];
        Assert.True(final.IsFinal);
        Assert.Equal("one two three", final.Text);
        Assert.All(updates[..^1], u => Assert.False(u.IsFinal));
    }

    [Fact]
    public async Task TranscribeStreamingAsync_PartialUpdatesGrowTowardTheFullText()
    {
        _config.Text = "alpha beta gamma";

        var updates = new List<TranscriptionUpdate>();
        await foreach (var update in _provider.TranscribeStreamingAsync(
            SlowAudio(chunks: 6, intervalMs: 250), CancellationToken.None))
        {
            updates.Add(update);
        }

        var partials = updates.Where(u => !u.IsFinal).Select(u => u.Text).ToList();
        Assert.NotEmpty(partials); // audio ran long enough for at least one partial
        Assert.Equal("alpha", partials[0]);
        Assert.All(partials, p => Assert.StartsWith(p, "alpha beta gamma"));
    }

    /// <summary>An audio sequence that completes immediately, like a recording stopped at once.</summary>
    private static async IAsyncEnumerable<AudioChunk> EmptyAudio()
    {
        await Task.CompletedTask;
        yield break;
    }

    /// <summary>An audio sequence delivering <paramref name="chunks"/> chunks, one per interval.</summary>
    private static async IAsyncEnumerable<AudioChunk> SlowAudio(int chunks, int intervalMs)
    {
        for (var i = 0; i < chunks; i++)
        {
            await Task.Delay(intervalMs);
            yield return new AudioChunk(new byte[320]);
        }
    }
}

/// <summary>Tests for <see cref="SilentWavFactory"/>.</summary>
public sealed class SilentWavFactoryTests
{
    [Fact]
    public void Create_HalfSecond_ProducesValidWavOfExpectedSize()
    {
        using var wav = SilentWavFactory.Create(TimeSpan.FromSeconds(0.5));

        Assert.Equal(0, wav.Position);
        Assert.Equal(44 + 16000, wav.Length); // 0.5 s × 16,000 samples/s × 2 bytes

        var header = new byte[44];
        Assert.Equal(44, wav.Read(header, 0, 44));
        Assert.Equal("RIFF"u8.ToArray(), header[..4]);
        Assert.Equal("WAVE"u8.ToArray(), header[8..12]);
        Assert.Equal("data"u8.ToArray(), header[36..40]);
        Assert.Equal(16000, BitConverter.ToInt32(header, 40)); // data chunk size
        Assert.Equal(16000, BitConverter.ToInt32(header, 24)); // sample rate
    }
}
