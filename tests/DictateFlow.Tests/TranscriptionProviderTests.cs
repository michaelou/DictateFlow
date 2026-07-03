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
