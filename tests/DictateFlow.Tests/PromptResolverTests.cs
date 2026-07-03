using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Prompts;
using DictateFlow.Core.Services.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DictateFlow.Tests;

/// <summary>Tests for <see cref="PromptResolver"/>.</summary>
public sealed class PromptResolverTests
{
    /// <summary>A <see cref="TimeProvider"/> pinned to 2026-07-02 local time.</summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
            => new(new DateTime(2026, 7, 2, 12, 0, 0, DateTimeKind.Utc));

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private readonly Mock<IPromptModeStore> _store = new();
    private readonly Mock<IForegroundAppService> _foregroundApp = new();
    private readonly AppSettings _appSettings = new();
    private readonly LlmSamplingDefaults _samplingDefaults = new();
    private readonly TestProviderConfigReader _configReader = new();

    public PromptResolverTests()
    {
        _foregroundApp.SetupGet(f => f.LastCaptured).Returns("");
        // The resolver reads the sampling defaults from the active LLM provider's section.
        _configReader.Set(ProviderKind.Llm, _appSettings.ActiveProviders.Llm, _samplingDefaults);
    }

    private PromptResolver CreateResolver()
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Current).Returns(_appSettings);
        return new PromptResolver(
            _store.Object, settings.Object, _configReader, _foregroundApp.Object,
            new FixedTimeProvider(), NullLogger<PromptResolver>.Instance);
    }

    private void SetupMode(string name, string systemPrompt, double? temperature = null, bool llmEnabled = true)
        => _store.Setup(s => s.GetByName(name))
            .Returns(new PromptMode(name, "", systemPrompt, temperature, llmEnabled));

    [Fact]
    public void Resolve_SubstitutesAllVariables()
    {
        _appSettings.TechnicalDictionary = ["DictateFlow", "xUnit"];
        _foregroundApp.SetupGet(f => f.LastCaptured).Returns("OUTLOOK");
        SetupMode("Email", "T:{{Transcript}} A:{{ApplicationName}} M:{{Mode}} D:{{CurrentDate}} X:{{TechnicalDictionary}}");
        var resolver = CreateResolver();

        var context = resolver.Resolve("hello", "Email");

        Assert.Equal("T:hello A:OUTLOOK M:Email D:2026-07-02 X:DictateFlow, xUnit", context.SystemPrompt);
        Assert.Equal("hello", context.Transcript);
        Assert.Equal("Email", context.ModeName);
    }

    [Fact]
    public void Resolve_VariableNamesAreCaseInsensitive()
    {
        SetupMode("Email", "{{TRANSCRIPT}}|{{transcript}}|{{TrAnScRiPt}}");
        var resolver = CreateResolver();

        var context = resolver.Resolve("x", "Email");

        Assert.Equal("x|x|x", context.SystemPrompt);
    }

    [Fact]
    public void Resolve_UnknownVariable_LeftAsIs()
    {
        SetupMode("Email", "before {{NotAVariable}} after");
        var resolver = CreateResolver();

        var context = resolver.Resolve("x", "Email");

        Assert.Equal("before {{NotAVariable}} after", context.SystemPrompt);
    }

    [Fact]
    public void Resolve_EmptyTechnicalDictionary_SubstitutesEmptyString()
    {
        _appSettings.TechnicalDictionary = [];
        SetupMode("Email", "[{{TechnicalDictionary}}]");
        var resolver = CreateResolver();

        var context = resolver.Resolve("x", "Email");

        Assert.Equal("[]", context.SystemPrompt);
    }

    [Fact]
    public void Resolve_UnknownMode_FallsBackToRaw()
    {
        _store.Setup(s => s.GetByName("Nope")).Returns((PromptMode?)null);
        SetupMode("Raw", "raw:{{Transcript}}", temperature: 0.0);
        var resolver = CreateResolver();

        var context = resolver.Resolve("x", "Nope");

        Assert.Equal("Raw", context.ModeName);
        Assert.Equal("raw:x", context.SystemPrompt);
    }

    [Fact]
    public void Resolve_UnknownModeAndNoRawFile_FallsBackToBuiltInRaw()
    {
        _store.Setup(s => s.GetByName(It.IsAny<string>())).Returns((PromptMode?)null);
        var resolver = CreateResolver();

        var context = resolver.Resolve("some words", "Nope");

        Assert.Equal("Raw", context.ModeName);
        Assert.Contains("some words", context.SystemPrompt);
        Assert.Equal(0.0, context.Temperature); // built-in Raw pins temperature to 0
    }

    [Fact]
    public void Resolve_ModeTemperature_OverridesProviderDefaults()
    {
        _samplingDefaults.Temperature = 0.7;
        SetupMode("Email", "p", temperature: 0.1);
        var resolver = CreateResolver();

        Assert.Equal(0.1, resolver.Resolve("x", "Email").Temperature);
    }

    [Fact]
    public void Resolve_NoModeTemperature_UsesActiveProviderTemperatureAndMaxTokens()
    {
        _samplingDefaults.Temperature = 0.7;
        _samplingDefaults.MaxTokens = 1234;
        SetupMode("Email", "p", temperature: null);
        var resolver = CreateResolver();

        var context = resolver.Resolve("x", "Email");

        Assert.Equal(0.7, context.Temperature);
        Assert.Equal(1234, context.MaxTokens);
    }

    [Fact]
    public void Resolve_LlmDisabledMode_SkipsResolutionAndFlagsContext()
    {
        SetupMode("Verbatim", "should never be resolved {{Transcript}}", llmEnabled: false);
        var resolver = CreateResolver();

        var context = resolver.Resolve("hello", "Verbatim");

        Assert.False(context.LlmEnabled);
        Assert.Equal("", context.SystemPrompt);
        Assert.Equal("hello", context.Transcript);
        Assert.Equal("Verbatim", context.ModeName);
    }

    [Fact]
    public void Resolve_LlmEnabledMode_FlagsContextEnabled()
    {
        SetupMode("Email", "p");
        var resolver = CreateResolver();

        Assert.True(resolver.Resolve("x", "Email").LlmEnabled);
    }

    [Fact]
    public void Resolve_ProviderSectionWithoutSamplingFields_UsesBuiltInDefaults()
    {
        // e.g. the Mock section, which only has DelayMs — the resolver still gets sane values.
        _appSettings.ActiveProviders.Llm = "SectionlessProvider";
        SetupMode("Email", "p", temperature: null);
        var resolver = CreateResolver();

        var context = resolver.Resolve("x", "Email");

        Assert.Equal(0.2, context.Temperature);
        Assert.Equal(2000, context.MaxTokens);
    }
}
