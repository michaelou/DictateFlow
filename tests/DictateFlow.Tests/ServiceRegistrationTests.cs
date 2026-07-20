using DictateFlow.App;
using DictateFlow.App.Services;
using DictateFlow.App.ViewModels;
using DictateFlow.App.Services.CloudRecordings;
using DictateFlow.App.Services.Commands;
using DictateFlow.App.Services.Output;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.CloudRecordings;
using DictateFlow.Core.Services.Commands;
using DictateFlow.Core.Services.History;
using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Output;
using DictateFlow.Core.Services.Pipeline;
using DictateFlow.Core.Services.Prompts;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Core.Services.Transcription;
using DictateFlow.Core.Services.Usage;
using DictateFlow.Core.Services.Models;
using DictateFlow.Providers.Anthropic;
using DictateFlow.Providers.AzureFoundry;
using DictateFlow.Providers.AzureSpeech;
using DictateFlow.Providers.Ollama;
using DictateFlow.Providers.OpenRouter;
using DictateFlow.Providers.Parakeet;
using DictateFlow.Providers.WhisperCpp;
using DictateFlow.Samples.NullOutput;
using Microsoft.Extensions.DependencyInjection;

namespace DictateFlow.Tests;

/// <summary>
/// Smoke tests proving the DI container can build the full object graph
/// (no UI resources are touched at construction time).
/// </summary>
public sealed class ServiceRegistrationTests : IDisposable
{
    private readonly TestAppPaths _paths = new();

    public void Dispose() => _paths.Dispose();

    private ServiceProvider BuildProvider()
        => new ServiceCollection()
            .AddLogging()
            .AddDictateFlow(_paths)
            .BuildServiceProvider();

    [Fact]
    public void AddDictateFlow_ResolvesAudioServices()
    {
        using var provider = BuildProvider();

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
        using var provider = BuildProvider();

        Assert.IsType<ActiveTranscriptionProvider>(provider.GetRequiredService<ITranscriptionProvider>());
        Assert.NotNull(provider.GetRequiredService<MockTranscriptionProvider>());
        Assert.NotNull(provider.GetRequiredService<AzureFoundryTranscriptionProvider>());
        Assert.NotNull(provider.GetRequiredService<AzureSpeechTranscriptionProvider>());
        Assert.NotNull(provider.GetRequiredService<WhisperCppTranscriptionProvider>());
        Assert.NotNull(provider.GetRequiredService<ParakeetTranscriptionProvider>());

        // One model manager per local engine; both are discoverable through IModelManager.
        var managers = provider.GetServices<IModelManager>().ToList();
        Assert.Contains(provider.GetRequiredService<WhisperCppModelManager>(), managers);
        Assert.Contains(provider.GetRequiredService<ParakeetModelManager>(), managers);
        Assert.NotNull(provider.GetRequiredService<IDictationFailureNotifier>());
    }

    [Fact]
    public void AddDictateFlow_ResolvesOutputPipelineServices()
    {
        using var provider = BuildProvider();

        Assert.IsType<DictationPipeline>(provider.GetRequiredService<IDictationPipeline>());
        Assert.IsType<SqliteHistoryRepository>(provider.GetRequiredService<IHistoryRepository>());
        Assert.NotNull(provider.GetRequiredService<IOutputGate>());

        // The single IOutputProvider is the registry-backed default; the concrete providers
        // live behind keyed registrations enumerated through the registry.
        Assert.IsType<ActiveOutputProvider>(provider.GetRequiredService<IOutputProvider>());
    }

    [Fact]
    public void AddDictateFlow_RegistersAllProvidersInTheRegistry()
    {
        using var provider = BuildProvider();
        var registry = provider.GetRequiredService<IProviderRegistry>();

        Assert.Equal(
            [MockTranscriptionProvider.RegistrationName, AzureFoundryProviders.RegistrationName, AzureSpeechProviders.RegistrationName, WhisperCppProviders.RegistrationName, ParakeetProviders.RegistrationName, OpenRouterProviders.RegistrationName],
            registry.GetNames(ProviderKind.Transcription));
        Assert.Equal(
            [MockLLMProvider.RegistrationName, AzureFoundryProviders.RegistrationName, AnthropicProviders.RegistrationName, OllamaProviders.RegistrationName, OpenRouterProviders.RegistrationName],
            registry.GetNames(ProviderKind.Llm));
        Assert.Equal(
            [OutputProviderNames.ClipboardPaste, OutputProviderNames.SimulatedKeyboard, NullOutputProvider.RegistrationName],
            registry.GetNames(ProviderKind.Output));
    }

    [Fact]
    public void AddDictateFlow_DefaultSettings_ResolveMockProviders()
    {
        using var provider = BuildProvider();
        var registry = provider.GetRequiredService<IProviderRegistry>();

        // Fresh settings activate the mocks, replacing the old "endpoint empty → mock" magic.
        Assert.IsType<MockTranscriptionProvider>(registry.ResolveTranscription());
        Assert.IsType<MockLLMProvider>(registry.ResolveLLM());
        Assert.IsType<ClipboardPasteOutputProvider>(registry.ResolveOutput()); // empty name → first registered
    }

    [Fact]
    public void AddDictateFlow_ResolvesLlmServices()
    {
        using var provider = BuildProvider();

        Assert.IsType<ActiveLLMProvider>(provider.GetRequiredService<ILLMProvider>());
        Assert.NotNull(provider.GetRequiredService<MockLLMProvider>());
        Assert.NotNull(provider.GetRequiredService<AzureFoundryLLMProvider>());
        Assert.NotNull(provider.GetRequiredService<AnthropicLLMProvider>());
        Assert.NotNull(provider.GetRequiredService<OllamaLLMProvider>());
        Assert.NotNull(provider.GetRequiredService<IPromptModeStore>());
        Assert.NotNull(provider.GetRequiredService<IPromptResolver>());
        Assert.NotNull(provider.GetRequiredService<IForegroundAppService>());
        Assert.IsType<SqliteUsageSink>(provider.GetRequiredService<IUsageSink>());
        Assert.IsType<SqliteCostService>(provider.GetRequiredService<ICostService>());
        Assert.IsType<PromptModeSelector>(provider.GetRequiredService<IPromptModeSelector>());
    }

    [Fact]
    public void AddDictateFlow_ResolvesM8PolishServices()
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<Core.Services.Validation.ISettingsValidator>());
        Assert.NotNull(provider.GetRequiredService<Core.Services.Transfer.ISettingsTransfer>());
        Assert.NotNull(provider.GetRequiredService<Core.Services.Transfer.IPromptsArchive>());
        Assert.NotNull(provider.GetRequiredService<Core.Services.Diagnostics.IDiagnosticsService>());
        Assert.NotNull(provider.GetRequiredService<Core.Services.Startup.IStartupRegistration>());
        Assert.NotNull(provider.GetRequiredService<IDialogService>());
    }

    [Fact]
    public void AddDictateFlow_ResolvesVoiceCommandServices()
    {
        using var provider = BuildProvider();

        Assert.IsType<VoiceCommandService>(provider.GetRequiredService<IVoiceCommandService>());
        Assert.NotNull(provider.GetRequiredService<IWakePhraseDetector>());
        Assert.NotNull(provider.GetRequiredService<ICommandMatcher>());
        // Issue #30 registers the dialog implementation, replacing the fail-closed default,
        // and the overlay/sound command feedback (replacing the no-op default).
        Assert.IsType<CommandConfirmationService>(provider.GetRequiredService<ICommandConfirmationService>());
        Assert.IsType<CommandFeedbackService>(provider.GetRequiredService<ICommandFeedback>());

        // The allowlist is the mock, the DictateFlow app action (issue #30) and the three
        // built-in launch actions (issue #27).
        var resolver = provider.GetRequiredService<ICommandActionResolver>();
        Assert.Equal(
            [
                MockCommandAction.RegistrationName,
                DictateFlowAction.RegistrationName,
                ProcessStartAction.RegistrationName,
                OpenUrlAction.RegistrationName,
                OpenFolderAction.RegistrationName,
            ],
            resolver.GetActionTypes());
        Assert.True(resolver.TryResolve(MockCommandAction.RegistrationName, out var action));
        Assert.IsType<MockCommandAction>(action);
        Assert.IsType<ProcessLauncher>(provider.GetRequiredService<IProcessLauncher>());

        // The aggregated sources include the mock's "test command", the built-in launch
        // commands (code) and the seeded user JSON commands.
        var definitions = provider.GetServices<ICommandDefinitionSource>()
            .SelectMany(s => s.GetDefinitions()).ToList();
        Assert.Contains(definitions, d => d.ActionType == MockCommandAction.RegistrationName);
        Assert.Contains(definitions, d => d.Name == "Open Notepad" && d.ActionType == ProcessStartAction.RegistrationName);
        Assert.Contains(definitions, d => d.ActionType == OpenUrlAction.RegistrationName);
    }

    [Fact]
    public void AddDictateFlow_ResolvesDictatePadServices()
    {
        using var provider = BuildProvider();

        // The view model resolves (its enhancement dependencies are all satisfiable), and the
        // hotkey→window listener resolves and subscribes without throwing.
        Assert.NotNull(provider.GetRequiredService<DictatePadViewModel>());
        Assert.NotNull(provider.GetRequiredService<DictatePadHotkeyListener>());
    }

    [Fact]
    public void AddDictateFlow_ResolvesCloudRecordingServices()
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<ICloudRecordingSource>());
        Assert.NotNull(provider.GetRequiredService<ICloudRecordingRepository>());
        Assert.NotNull(provider.GetRequiredService<ICloudTranscriptionService>());
        Assert.NotNull(provider.GetRequiredService<IAudioDecoder>());
        // The review view model resolves its whole dependency graph (player, poller, source…).
        Assert.NotNull(provider.GetRequiredService<CloudRecordingsViewModel>());
        // The poller is registered both as a singleton and as a hosted service.
        Assert.NotNull(provider.GetRequiredService<CloudRecordingPollerService>());
        Assert.Contains(
            provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>(),
            s => s is CloudRecordingPollerService);
    }

    [Fact]
    public void AddDictateFlow_DictationControllerIsSingleton()
    {
        using var provider = BuildProvider();

        Assert.Same(
            provider.GetRequiredService<IDictationController>(),
            provider.GetRequiredService<IDictationController>());
    }
}
