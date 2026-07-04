using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Providers.Parakeet;
using Microsoft.Extensions.Logging.Abstractions;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="ParakeetTranscriptionProvider"/> that do not need the real model
/// files — the installation gate and configuration handling. Actual inference is exercised
/// manually (the int8 model is a 660 MB download).
/// </summary>
public sealed class ParakeetTranscriptionProviderTests : IDisposable
{
    private readonly TestAppPaths _appPaths = new();
    private readonly RecordingUsageSink _usageSink = new();

    public void Dispose() => _appPaths.Dispose();

    private ParakeetTranscriptionProvider CreateProvider()
    {
        var handler = new FakeHttpMessageHandler(System.Net.HttpStatusCode.NotFound);
        var manager = new ParakeetModelManager(
            new FakeHttpClientFactory(handler), _appPaths, NullLogger<ParakeetModelManager>.Instance);
        var configReader = new TestProviderConfigReader()
            .Set(ProviderKind.Transcription, ParakeetProviders.RegistrationName, new ParakeetTranscriptionConfig());
        return new ParakeetTranscriptionProvider(
            manager, configReader, _usageSink, TimeProvider.System,
            NullLogger<ParakeetTranscriptionProvider>.Instance);
    }

    [Fact]
    public async Task TranscribeAsync_NotInstalled_ConfigurationErrorPointsAtLocalModels()
    {
        var provider = CreateProvider();
        using var silence = SilentWavFactory.Create(TimeSpan.FromSeconds(0.5));

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.TranscribeAsync(silence, CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
        Assert.Contains("Local Models", ex.Message);
        Assert.Empty(_usageSink.Records);
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
