using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Providers.AzureSpeech;
using Microsoft.Extensions.Logging;
using Moq;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="AzureSpeechTranscriptionProvider"/>. The Speech SDK's recognizer
/// cannot be faked (sealed, websocket transport), so these cover the configuration guards —
/// which must fail fast with actionable <see cref="ProviderException"/>s before any SDK or
/// network work happens.
/// </summary>
public sealed class AzureSpeechTranscriptionProviderTests
{
    private readonly AzureSpeechTranscriptionConfig _config = new()
    {
        Endpoint = "https://example.cognitiveservices.azure.com/",
        ApiKey = "test-key",
    };

    private readonly Mock<IUsageSink> _usageSink = new();
    private readonly AzureSpeechTranscriptionProvider _provider;

    public AzureSpeechTranscriptionProviderTests()
    {
        var configReader = new TestProviderConfigReader()
            .Set(ProviderKind.Transcription, AzureSpeechProviders.RegistrationName, _config);
        _provider = new AzureSpeechTranscriptionProvider(
            configReader, _usageSink.Object, TimeProvider.System,
            Mock.Of<ILogger<AzureSpeechTranscriptionProvider>>());
    }

    [Fact]
    public async Task TranscribeAsync_MissingApiKey_ThrowsConfigurationError()
    {
        _config.ApiKey = "";

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => _provider.TranscribeAsync(new MemoryStream(new byte[44]), CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
        Assert.Contains("API key", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://example.com")]
    public async Task TranscribeAsync_InvalidEndpoint_ThrowsConfigurationError(string endpoint)
    {
        _config.Endpoint = endpoint;

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => _provider.TranscribeAsync(new MemoryStream(new byte[44]), CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
        Assert.Contains("endpoint", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TranscribeStreamingAsync_MissingApiKey_ThrowsConfigurationErrorOnFirstMoveNext()
    {
        _config.ApiKey = "";

        var ex = await Assert.ThrowsAsync<ProviderException>(async () =>
        {
            await foreach (var _ in _provider.TranscribeStreamingAsync(NoAudio(), CancellationToken.None))
            {
            }
        });

        Assert.True(ex.IsConfigurationError);
    }

    private static async IAsyncEnumerable<AudioChunk> NoAudio()
    {
        await Task.CompletedTask;
        yield break;
    }
}
