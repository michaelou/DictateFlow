using DictateFlow.App;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Pipeline;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Core.Services.Transcription;
using DictateFlow.Samples.NullOutput;
using Microsoft.Extensions.DependencyInjection;

namespace DictateFlow.Tests;

/// <summary>
/// Proves the M7 extensibility claim with the <see cref="NullOutputProvider"/> sample: the
/// provider is one class plus one <c>AddOutputProvider</c> line in the App bootstrap — it
/// shows up in the registry (and therefore the settings dropdown, which binds to
/// <c>GetNames(ProviderKind.Output)</c>), resolves when selected, and receives a full
/// dictation from the unmodified pipeline.
/// </summary>
public sealed class NullOutputProviderDemonstrationTests : IDisposable
{
    private readonly TestAppPaths _paths = new();

    public void Dispose() => _paths.Dispose();

    private ServiceProvider BuildProvider()
        => new ServiceCollection()
            .AddLogging()
            .AddDictateFlow(_paths)
            .BuildServiceProvider();

    [Fact]
    public void NullProvider_AppearsInTheRegistryNames_SoTheDropdownPicksItUp()
    {
        using var provider = BuildProvider();
        var registry = provider.GetRequiredService<IProviderRegistry>();

        Assert.Contains(NullOutputProvider.RegistrationName, registry.GetNames(ProviderKind.Output));
    }

    [Fact]
    public void NullProvider_SelectedInSettings_IsResolvedByTheRegistry()
    {
        using var provider = BuildProvider();
        var settings = provider.GetRequiredService<ISettingsService>();
        settings.Current.ActiveProviders.Output = NullOutputProvider.RegistrationName;

        var registry = provider.GetRequiredService<IProviderRegistry>();

        Assert.IsType<NullOutputProvider>(registry.ResolveOutput());
    }

    [Fact]
    public async Task NullProvider_SelectedInSettings_ReceivesAFullDictationFromTheRealPipeline()
    {
        using var provider = BuildProvider();
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync();

        // Configure entirely through settings + config reader — no code changes anywhere:
        // mock speech/LLM (no delays) and the Null output provider.
        var settings = provider.GetRequiredService<ISettingsService>();
        settings.Current.ActiveProviders.Output = NullOutputProvider.RegistrationName;
        provider.GetRequiredService<IProviderConfigReader>().SetConfig(
            ProviderKind.Transcription, MockTranscriptionProvider.RegistrationName,
            new MockTranscriptionConfig { Text = "dictated into the void", DelayMs = 0 });
        provider.GetRequiredService<IProviderConfigReader>().SetConfig(
            ProviderKind.Llm, MockLLMProvider.RegistrationName, new MockLlmConfig { DelayMs = 0 });

        var pipeline = provider.GetRequiredService<IDictationPipeline>();
        var result = await pipeline.RunAsync(
            new PipelineRequest(new MemoryStream(new byte[44 + 32000]), "notepad", 1234),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("[enhanced] dictated into the void", result.FinalText);
        Assert.Equal(
            "[enhanced] dictated into the void",
            provider.GetRequiredService<NullOutputProvider>().LastText);
    }
}
