using DictateFlow.App;
using DictateFlow.App.Services;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.History;
using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Output;
using DictateFlow.Core.Services.Pipeline;
using DictateFlow.Core.Services.Prompts;
using DictateFlow.Core.Services.Transcription;
using DictateFlow.Core.Services.Usage;
using DictateFlow.Providers.AzureFoundry;
using Microsoft.Extensions.DependencyInjection;

namespace DictateFlow.Tests;

/// <summary>
/// Smoke tests proving the DI container can build the M2/M3/M4/M5 object graph
/// (no UI resources are touched at construction time).
/// </summary>
public sealed class ServiceRegistrationTests : IDisposable
{
    private readonly TestAppPaths _paths = new();

    public void Dispose() => _paths.Dispose();

    [Fact]
    public void AddDictateFlow_ResolvesAudioServices()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddDictateFlow(_paths)
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IDictationController>());
        Assert.NotNull(provider.GetRequiredService<IAudioRecorder>());
        Assert.NotNull(provider.GetRequiredService<IHotkeyService>());
        Assert.NotNull(provider.GetRequiredService<IRecordingOverlay>());
        Assert.NotNull(provider.GetRequiredService<IMicrophoneEnumerator>());
        Assert.NotNull(provider.GetRequiredService<ISettingsService>());
    }

    [Fact]
    public void AddDictateFlow_ResolvesTranscriptionServices()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddDictateFlow(_paths)
            .BuildServiceProvider();

        Assert.IsType<TranscriptionProviderSelector>(provider.GetRequiredService<ITranscriptionProvider>());
        Assert.NotNull(provider.GetRequiredService<MockTranscriptionProvider>());
        Assert.NotNull(provider.GetRequiredService<AzureFoundryTranscriptionProvider>());
        Assert.NotNull(provider.GetRequiredService<IDictationFailureNotifier>());
    }

    [Fact]
    public void AddDictateFlow_ResolvesOutputPipelineServices()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddDictateFlow(_paths)
            .BuildServiceProvider();

        Assert.IsType<DictationPipeline>(provider.GetRequiredService<IDictationPipeline>());
        Assert.IsType<SqliteHistoryRepository>(provider.GetRequiredService<IHistoryRepository>());
        Assert.NotNull(provider.GetRequiredService<IOutputGate>());

        var outputProviders = provider.GetServices<IOutputProvider>().ToList();
        Assert.Equal(2, outputProviders.Count);
        Assert.Contains(outputProviders, p => p.Name == OutputProviderNames.ClipboardPaste);
        Assert.Contains(outputProviders, p => p.Name == OutputProviderNames.SimulatedKeyboard);
    }

    [Fact]
    public void AddDictateFlow_ResolvesLlmServices()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddDictateFlow(_paths)
            .BuildServiceProvider();

        Assert.IsType<LLMProviderSelector>(provider.GetRequiredService<ILLMProvider>());
        Assert.NotNull(provider.GetRequiredService<MockLLMProvider>());
        Assert.NotNull(provider.GetRequiredService<AzureFoundryLLMProvider>());
        Assert.NotNull(provider.GetRequiredService<IPromptModeStore>());
        Assert.NotNull(provider.GetRequiredService<IPromptResolver>());
        Assert.NotNull(provider.GetRequiredService<IForegroundAppService>());
        Assert.IsType<SqliteUsageSink>(provider.GetRequiredService<IUsageSink>());
        Assert.IsType<SqliteCostService>(provider.GetRequiredService<ICostService>());
        Assert.IsType<PromptModeSelector>(provider.GetRequiredService<IPromptModeSelector>());
    }

    [Fact]
    public void AddDictateFlow_DictationControllerIsSingleton()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddDictateFlow(_paths)
            .BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<IDictationController>(),
            provider.GetRequiredService<IDictationController>());
    }
}
