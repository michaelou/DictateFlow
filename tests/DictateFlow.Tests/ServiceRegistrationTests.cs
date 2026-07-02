using DictateFlow.App;
using DictateFlow.App.Services;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.Transcription;
using DictateFlow.Providers.AzureFoundry;
using Microsoft.Extensions.DependencyInjection;

namespace DictateFlow.Tests;

/// <summary>
/// Smoke tests proving the DI container can build the M2/M3 object graph
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
        Assert.NotNull(provider.GetRequiredService<IDictationResultPresenter>());
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
