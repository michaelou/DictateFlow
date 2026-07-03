using System.Text.Json;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Providers;
using Microsoft.Extensions.Logging;
using Moq;

namespace DictateFlow.Tests;

/// <summary>Tests for <see cref="ProviderConfigReader"/> over real <see cref="AppSettings"/>.</summary>
public sealed class ProviderConfigReaderTests
{
    private sealed class FakeConfig
    {
        public string Endpoint { get; set; } = "default-endpoint";
        public int TimeoutSeconds { get; set; } = 30;
    }

    private readonly AppSettings _appSettings = new();
    private readonly Mock<ILogger<ProviderConfigReader>> _logger = new();
    private readonly ProviderConfigReader _reader;

    public ProviderConfigReaderTests()
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Current).Returns(_appSettings);
        _reader = new ProviderConfigReader(settings.Object, _logger.Object);
    }

    [Fact]
    public void GetConfig_DeserializesTheMatchingSubsection()
    {
        _appSettings.Providers.Transcription["Fake"] = JsonSerializer.SerializeToElement(
            new { Endpoint = "https://example.com", TimeoutSeconds = 5 });

        var config = _reader.GetConfig<FakeConfig>(ProviderKind.Transcription, "Fake");

        Assert.Equal("https://example.com", config.Endpoint);
        Assert.Equal(5, config.TimeoutSeconds);
    }

    [Fact]
    public void GetConfig_SectionKeyLookupIsCaseInsensitive()
    {
        _appSettings.Providers.Llm["fake"] = JsonSerializer.SerializeToElement(new { Endpoint = "e" });

        var config = _reader.GetConfig<FakeConfig>(ProviderKind.Llm, "FAKE");

        Assert.Equal("e", config.Endpoint);
    }

    [Fact]
    public void GetConfig_MissingSection_ReturnsDefaultsAndWarns()
    {
        var config = _reader.GetConfig<FakeConfig>(ProviderKind.Transcription, "NoSuchSection");

        Assert.Equal("default-endpoint", config.Endpoint);
        Assert.Equal(30, config.TimeoutSeconds);
        _logger.Verify(l => l.Log(
            LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public void GetConfig_MissingProperties_KeepDefaults()
    {
        // A section carrying only some fields (e.g. the Mock section read as sampling
        // defaults) fills the rest from the config type's defaults.
        _appSettings.Providers.Transcription["Fake"] = JsonSerializer.SerializeToElement(
            new { SomethingElse = 42 });

        var config = _reader.GetConfig<FakeConfig>(ProviderKind.Transcription, "Fake");

        Assert.Equal("default-endpoint", config.Endpoint);
    }

    [Fact]
    public void GetConfig_UnreadableSection_ReturnsDefaultsAndWarns()
    {
        // A string where an object is expected cannot deserialize into FakeConfig.
        _appSettings.Providers.Transcription["Fake"] = JsonSerializer.SerializeToElement("not an object");

        var config = _reader.GetConfig<FakeConfig>(ProviderKind.Transcription, "Fake");

        Assert.Equal("default-endpoint", config.Endpoint);
    }

    [Fact]
    public void SetConfig_RoundTripsThroughGetConfig()
    {
        _reader.SetConfig(ProviderKind.Llm, "Fake", new FakeConfig { Endpoint = "written", TimeoutSeconds = 7 });

        var config = _reader.GetConfig<FakeConfig>(ProviderKind.Llm, "Fake");

        Assert.Equal("written", config.Endpoint);
        Assert.Equal(7, config.TimeoutSeconds);
    }

    [Fact]
    public void SetConfig_ReplacesExistingSectionCaseInsensitively()
    {
        _appSettings.Providers.Output["fake"] = JsonSerializer.SerializeToElement(new { Old = true });

        _reader.SetConfig(ProviderKind.Output, "Fake", new FakeConfig());

        var key = Assert.Single(_appSettings.Providers.Output.Keys);
        Assert.Equal("fake", key); // the stored key is reused, no duplicate appears
    }
}
