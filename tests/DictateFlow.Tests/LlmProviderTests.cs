using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DictateFlow.Tests;

/// <summary>Tests for <see cref="MockLLMProvider"/>.</summary>
public sealed class MockLLMProviderTests
{
    private static PromptContext Context(string transcript)
        => new("system", transcript, 0.2, 2000, "Raw");

    [Fact]
    public async Task ProcessAsync_ReturnsPrefixedTranscript()
    {
        var provider = new MockLLMProvider { Delay = TimeSpan.Zero };

        var result = await provider.ProcessAsync(Context("hello"), CancellationToken.None);

        Assert.Equal("[enhanced] hello", result);
    }

    [Fact]
    public async Task ProcessAsync_HonorsCancellationDuringDelay()
    {
        var provider = new MockLLMProvider { Delay = TimeSpan.FromSeconds(30) };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ProcessAsync(Context("x"), cts.Token));
    }
}

/// <summary>Tests for <see cref="LLMProviderSelector"/>.</summary>
public sealed class LLMProviderSelectorTests
{
    private readonly Mock<ILLMProvider> _configured = new();
    private readonly Mock<ILLMProvider> _mock = new();
    private readonly AppSettings _appSettings = new();
    private readonly LLMProviderSelector _selector;

    public LLMProviderSelectorTests()
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Current).Returns(_appSettings);
        _configured.Setup(p => p.ProcessAsync(It.IsAny<PromptContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("real");
        _mock.Setup(p => p.ProcessAsync(It.IsAny<PromptContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("mock");

        _selector = new LLMProviderSelector(
            settings.Object,
            () => _configured.Object,
            () => _mock.Object,
            NullLogger<LLMProviderSelector>.Instance);
    }

    private static PromptContext Context() => new("system", "x", 0.2, 2000, "Raw");

    [Fact]
    public async Task ProcessAsync_EmptyEndpoint_UsesMockProvider()
    {
        _appSettings.Llm.Endpoint = "";

        var result = await _selector.ProcessAsync(Context(), CancellationToken.None);

        Assert.Equal("mock", result);
        _configured.Verify(p => p.ProcessAsync(It.IsAny<PromptContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_EndpointConfigured_UsesConfiguredProvider()
    {
        _appSettings.Llm.Endpoint = "https://example.com";

        var result = await _selector.ProcessAsync(Context(), CancellationToken.None);

        Assert.Equal("real", result);
        _mock.Verify(p => p.ProcessAsync(It.IsAny<PromptContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_SelectionFollowsSettingsChangesWithoutRestart()
    {
        _appSettings.Llm.Endpoint = "";
        await _selector.ProcessAsync(Context(), CancellationToken.None);

        _appSettings.Llm.Endpoint = "https://example.com";
        var result = await _selector.ProcessAsync(Context(), CancellationToken.None);

        Assert.Equal("real", result);
    }
}
