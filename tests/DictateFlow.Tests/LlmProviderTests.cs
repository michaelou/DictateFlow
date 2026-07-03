using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Providers;

namespace DictateFlow.Tests;

/// <summary>Tests for <see cref="MockLLMProvider"/>.</summary>
public sealed class MockLLMProviderTests
{
    private readonly MockLlmConfig _config = new() { DelayMs = 0 };
    private readonly MockLLMProvider _provider;

    public MockLLMProviderTests()
    {
        var configReader = new TestProviderConfigReader()
            .Set(ProviderKind.Llm, MockLLMProvider.RegistrationName, _config);
        _provider = new MockLLMProvider(configReader);
    }

    private static PromptContext Context(string transcript)
        => new("system", transcript, 0.2, 2000, "Raw");

    [Fact]
    public async Task ProcessAsync_ReturnsPrefixedTranscript()
    {
        var result = await _provider.ProcessAsync(Context("hello"), CancellationToken.None);

        Assert.Equal("[enhanced] hello", result);
    }

    [Fact]
    public async Task ProcessAsync_HonorsCancellationDuringDelay()
    {
        _config.DelayMs = 30_000;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _provider.ProcessAsync(Context("x"), cts.Token));
    }
}
