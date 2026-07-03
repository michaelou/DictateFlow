using System.Net;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Providers.WhisperCpp;
using Microsoft.Extensions.Logging.Abstractions;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="WhisperCppTranscriptionProvider"/>. Running the real whisper.cpp
/// binary needs a gigabyte-scale download, so these cover the pre-flight behavior (missing
/// installation, unknown model) and the language mapping; the process handling is exercised
/// manually via the Settings test connection.
/// </summary>
public sealed class WhisperCppTranscriptionProviderTests : IDisposable
{
    private readonly TestAppPaths _appPaths = new();
    private readonly RecordingUsageSink _usageSink = new();
    private readonly WhisperCppTranscriptionConfig _config = new();

    public void Dispose() => _appPaths.Dispose();

    private WhisperCppTranscriptionProvider CreateProvider()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.NotFound, "{}");
        var manager = new WhisperCppModelManager(
            new FakeHttpClientFactory(handler), _appPaths, NullLogger<WhisperCppModelManager>.Instance);
        var configReader = new TestProviderConfigReader()
            .Set(ProviderKind.Transcription, WhisperCppProviders.RegistrationName, _config);
        return new WhisperCppTranscriptionProvider(
            manager, configReader, _usageSink, TimeProvider.System,
            NullLogger<WhisperCppTranscriptionProvider>.Instance);
    }

    [Fact]
    public async Task TranscribeAsync_NothingInstalled_ThrowsConfigurationErrorPointingAtLocalModels()
    {
        var provider = CreateProvider();
        using var audio = SilentWavFactory.Create(TimeSpan.FromSeconds(0.5));

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.TranscribeAsync(audio, CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
        Assert.Contains("not installed", ex.Message);
        Assert.Contains("Local Models", ex.Message);
        Assert.Empty(_usageSink.Records);
    }

    [Fact]
    public async Task TranscribeAsync_UnknownModel_ThrowsConfigurationError()
    {
        _config.Model = "ggml-nonexistent";
        var provider = CreateProvider();
        using var audio = SilentWavFactory.Create(TimeSpan.FromSeconds(0.5));

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.TranscribeAsync(audio, CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
        Assert.Contains("ggml-nonexistent", ex.Message);
    }

    [Theory]
    [InlineData("", "auto")]
    [InlineData("   ", "auto")]
    [InlineData("en", "en")]
    [InlineData("en-US", "en")]
    [InlineData("EL-GR", "el")]
    [InlineData("en-US, el-GR", "en")] // whisper.cpp takes a single language; the first tag wins
    public void MapLanguage_ReducesBcp47TagsToWhisperLanguageCodes(string configured, string expected)
        => Assert.Equal(expected, WhisperCppTranscriptionProvider.MapLanguage(configured));

    /// <summary>Minimal <see cref="IHttpClientFactory"/> handing out clients over a fixed handler.</summary>
    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
