using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.Transcription;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DictateFlow.Tests;

/// <summary>Tests for <see cref="MockTranscriptionProvider"/>.</summary>
public sealed class MockTranscriptionProviderTests
{
    [Fact]
    public async Task TranscribeAsync_ReturnsCannedTextAndComputedDuration()
    {
        var provider = new MockTranscriptionProvider { CannedText = "hello", Delay = TimeSpan.Zero };
        using var wav = SilentWavFactory.Create(TimeSpan.FromSeconds(2));

        var result = await provider.TranscribeAsync(wav, CancellationToken.None);

        Assert.Equal("hello", result.Text);
        Assert.NotNull(result.AudioDurationSeconds);
        Assert.Equal(2.0, result.AudioDurationSeconds!.Value, precision: 2);
    }

    [Fact]
    public async Task TranscribeAsync_HonorsCancellationDuringDelay()
    {
        var provider = new MockTranscriptionProvider { Delay = TimeSpan.FromSeconds(30) };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.TranscribeAsync(new MemoryStream(), cts.Token));
    }
}

/// <summary>Tests for <see cref="TranscriptionProviderSelector"/>.</summary>
public sealed class TranscriptionProviderSelectorTests
{
    private readonly Mock<ITranscriptionProvider> _configured = new();
    private readonly Mock<ITranscriptionProvider> _mock = new();
    private readonly AppSettings _appSettings = new();
    private readonly TranscriptionProviderSelector _selector;

    public TranscriptionProviderSelectorTests()
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Current).Returns(_appSettings);
        _configured.Setup(p => p.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionResult("real", null, null));
        _mock.Setup(p => p.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionResult("mock", null, null));

        _selector = new TranscriptionProviderSelector(
            settings.Object,
            () => _configured.Object,
            () => _mock.Object,
            NullLogger<TranscriptionProviderSelector>.Instance);
    }

    [Fact]
    public async Task TranscribeAsync_EmptyEndpoint_UsesMockProvider()
    {
        _appSettings.Speech.Endpoint = "";

        var result = await _selector.TranscribeAsync(new MemoryStream(), CancellationToken.None);

        Assert.Equal("mock", result.Text);
        _configured.Verify(p => p.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TranscribeAsync_EndpointConfigured_UsesConfiguredProvider()
    {
        _appSettings.Speech.Endpoint = "https://example.com";

        var result = await _selector.TranscribeAsync(new MemoryStream(), CancellationToken.None);

        Assert.Equal("real", result.Text);
        _mock.Verify(p => p.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TranscribeAsync_SelectionFollowsSettingsChangesWithoutRestart()
    {
        _appSettings.Speech.Endpoint = "";
        await _selector.TranscribeAsync(new MemoryStream(), CancellationToken.None);

        _appSettings.Speech.Endpoint = "https://example.com";
        var result = await _selector.TranscribeAsync(new MemoryStream(), CancellationToken.None);

        Assert.Equal("real", result.Text);
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
