using DictateFlow.App.Services;
using DictateFlow.App.Services.Audio;
using DictateFlow.App.Services.CloudRecordings;
using DictateFlow.App.Services.Commands;
using DictateFlow.App.Services.Output;
using DictateFlow.App.ViewModels;
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
using DictateFlow.Core.Services.Replacements;
using DictateFlow.Core.Services.Startup;
using DictateFlow.Core.Services.Transcription;
using DictateFlow.Core.Services.Transfer;
using DictateFlow.Core.Services.Updates;
using DictateFlow.Core.Services.Usage;
using DictateFlow.Core.Services.Validation;
using DictateFlow.Providers.Anthropic;
using DictateFlow.Providers.AzureBlobStorage;
using DictateFlow.Providers.AzureFoundry;
using DictateFlow.Providers.AzureSpeech;
using DictateFlow.Providers.Ollama;
using DictateFlow.Providers.OpenRouter;
using DictateFlow.Providers.Parakeet;
using DictateFlow.Providers.WhisperCpp;
using DictateFlow.Samples.NullOutput;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App;

/// <summary>
/// Registers all DictateFlow services so the application and tests can build the same container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all DictateFlow services to <paramref name="services"/>.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="appPaths">
    /// Optional path provider override; tests pass an instance rooted in a temporary
    /// directory. When omitted, the <c>%APPDATA%\DictateFlow\</c> paths are used.
    /// </param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddDictateFlow(this IServiceCollection services, IAppPaths? appPaths = null)
    {
        if (appPaths is null)
        {
            services.AddSingleton<IAppPaths, AppPaths>();
        }
        else
        {
            services.AddSingleton(appPaths);
        }

        services.AddSingleton<ISettingsMigration, LegacySettingsMigration>();
        services.AddSingleton<ISettingsMigration, RecordingHotkeyMigration>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();

        // Settings hardening (M8): validation, import/export and launch-with-Windows.
        services.AddSingleton<ISettingsValidator, SettingsValidator>();
        services.AddSingleton<ISettingsTransfer, SettingsTransfer>();
        services.AddSingleton<IPromptsArchive, PromptsArchive>();
        services.AddSingleton<Core.Services.Diagnostics.IDiagnosticsService, Core.Services.Diagnostics.DiagnosticsService>();
        services.AddSingleton<IRunKeyStore, RegistryRunKeyStore>();
        services.AddSingleton<IStartupRegistration, StartupRegistration>();

        // Manual "Check for updates" against the GitHub releases API. A short timeout keeps a
        // slow or unreachable network from hanging the click; failures come back as a message.
        services.AddHttpClient<IUpdateService, GitHubUpdateService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DictateFlow-UpdateCheck");
        });

        // Downloads the installer when the user chooses "Download & install". The self-contained
        // installer is large, so this client has no overall timeout — progress and cancellation
        // come from the download itself, not a stopwatch.
        services.AddHttpClient<IUpdateDownloader, UpdateDownloader>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DictateFlow-UpdateCheck");
        });

        services.AddSingleton<IWindowService, WindowService>();
        services.AddSingleton<IShutdownService, ShutdownService>();
        services.AddSingleton<ITrayIconService, TrayIconService>();
        services.AddSingleton<IDialogService, DialogService>();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAudioRecorder, NAudioRecorder>();
        services.AddSingleton<IMicrophoneEnumerator, MicrophoneEnumerator>();
        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<IRecordingOverlay, RecordingOverlayService>();
        services.AddSingleton<IDictationController, DictationController>();

        // Provider registry (M7): named registration is the one mechanism through which
        // providers exist; ActiveProviders in settings picks per kind. This bootstrap is the
        // single place that lists concrete providers — adding one is one registration line.
        services.AddSingleton<IProviderRegistry, ProviderRegistry>();
        services.AddSingleton<IProviderConfigReader, ProviderConfigReader>();

        // The Azure typed HTTP clients (standard resilience pipeline) must be registered
        // before their AddXProvider lines so their typed-client lifetime wins.
        services.AddAzureFoundryTranscription();
        services.AddAzureFoundryLlm();
        services.AddAnthropicLlm();
        services.AddOllamaLlm();
        // OpenRouter (issue #34): one LLM provider (OpenAI-compatible chat completions) and one
        // transcription provider (audio sent as multimodal input to an audio-capable model).
        services.AddOpenRouterLlm();
        services.AddOpenRouterTranscription();

        // Local whisper.cpp: model manager (downloads/verifies the engine and models) plus
        // its download HTTP client; the provider itself is registered below like any other.
        services.AddWhisperCppTranscription();

        // Local Parakeet TDT v3 (sherpa-onnx, in-process): model manager plus its download
        // HTTP client; the provider itself is registered below like any other.
        services.AddParakeetTranscription();

        // Mock providers are registered first: the first name of a kind is the fallback used
        // when the configured active name is unknown (and the dropdown default). Mocks and
        // the local whisper.cpp provider need no endpoint/credentials, so they register with
        // requiresConnection: false and settings validation leaves them alone.
        services.AddTranscriptionProvider<MockTranscriptionProvider>(
            MockTranscriptionProvider.RegistrationName, requiresConnection: false);
        services.AddTranscriptionProvider<AzureFoundryTranscriptionProvider>(AzureFoundryProviders.RegistrationName);
        // Azure real-time speech (streaming-capable, issue #20). Registered with
        // requiresConnection: false because it has no DeploymentName — the generic connection
        // validation doesn't fit; the provider validates its endpoint/key itself per call.
        services.AddTranscriptionProvider<AzureSpeechTranscriptionProvider>(
            AzureSpeechProviders.RegistrationName, requiresConnection: false);
        services.AddTranscriptionProvider<WhisperCppTranscriptionProvider>(
            WhisperCppProviders.RegistrationName, requiresConnection: false);
        // Local Parakeet TDT v3: no endpoint/credentials — the provider validates its own
        // installation state per call, like WhisperCpp.
        services.AddTranscriptionProvider<ParakeetTranscriptionProvider>(
            ParakeetProviders.RegistrationName, requiresConnection: false);
        // OpenRouter transcription self-validates its key/model per call (it has no
        // Endpoint/DeploymentName connection shape), so it registers with requiresConnection: false.
        services.AddTranscriptionProvider<OpenRouterTranscriptionProvider>(
            OpenRouterProviders.RegistrationName, requiresConnection: false);
        services.AddLLMProvider<MockLLMProvider>(MockLLMProvider.RegistrationName, requiresConnection: false);
        services.AddLLMProvider<AzureFoundryLLMProvider>(AzureFoundryProviders.RegistrationName);
        // Anthropic and Ollama validate their key/URL themselves per call — the generic
        // Endpoint/DeploymentName connection validation does not fit either of them.
        services.AddLLMProvider<AnthropicLLMProvider>(AnthropicProviders.RegistrationName, requiresConnection: false);
        services.AddLLMProvider<OllamaLLMProvider>(OllamaProviders.RegistrationName, requiresConnection: false);
        // OpenRouter LLM self-validates its key/model per call, like Anthropic and Ollama.
        services.AddLLMProvider<OpenRouterLLMProvider>(OpenRouterProviders.RegistrationName, requiresConnection: false);
        services.AddOutputProvider<ClipboardPasteOutputProvider>(OutputProviderNames.ClipboardPaste);
        services.AddOutputProvider<SimulatedKeyboardOutputProvider>(OutputProviderNames.SimulatedKeyboard);
        // The sample provider proving the extensibility claim: this line is its entire integration.
        services.AddOutputProvider<NullOutputProvider>(NullOutputProvider.RegistrationName);

        // Streaming transcription (issue #20): starts a session per recording when enabled in
        // settings and the active provider implements IStreamingTranscriptionProvider.
        services.AddSingleton<IStreamingTranscriptionCoordinator, StreamingTranscriptionCoordinator>();

        // Registry-backed defaults: consumers keep injecting the plain provider interfaces
        // and always get the provider that is active in settings at call time.
        services.AddSingleton<ITranscriptionProvider, ActiveTranscriptionProvider>();
        services.AddSingleton<ILLMProvider, ActiveLLMProvider>();
        services.AddSingleton<IOutputProvider, ActiveOutputProvider>();

        // LLM enhancement: prompt modes + resolver + per-application mode selection.
        // Usage lands in SQLite for the cost dashboard.
        services.AddSingleton<IUsageSink, SqliteUsageSink>();
        services.AddSingleton<ICostService, SqliteCostService>();
        services.AddSingleton<IForegroundAppService, ForegroundAppService>();
        services.AddSingleton<IPromptModeStore, PromptModeStore>();
        services.AddSingleton<IPromptResolver, PromptResolver>();
        services.AddSingleton<IPromptModeSelector, PromptModeSelector>();

        // Replacement dictionary (issue #35): deterministic transcript corrections applied by
        // the pipeline before enhancement. Rules live in AppSettings and are read per call.
        services.AddSingleton<ITextReplacementService, TextReplacementService>();

        // Voice command framework (issue #26): the deterministic command branch of the
        // pipeline. Action types register like providers — one AddCommandAction line each;
        // the catalog is the fixed allowlist of what a command can execute.
        services.AddSingleton<IWakePhraseDetector, WakePhraseDetector>();
        services.AddSingleton<ICommandMatcher, CommandMatcher>();
        services.AddSingleton<ICommandActionResolver, CommandActionResolver>();
        services.AddSingleton<IVoiceCommandService, VoiceCommandService>();

        // App integration (issue #30): the real confirmation dialog and command feedback
        // (overlay command state + sounds). Both are registered before the Core defaults so
        // their TryAdd fallbacks (fail-closed deny / no-op feedback) are skipped.
        services.AddSingleton<IAppActions, AppActions>();

        // Bridges the global DictatePad hotkey to opening the window; materialized at startup so
        // its DictatePadPressed subscription is armed, mirroring the dictation controller.
        services.AddSingleton<DictatePadHotkeyListener>();

        services.AddSingleton<ICommandSoundPlayer, CommandSoundPlayer>();
        services.AddSingleton<ICommandConfirmationService, CommandConfirmationService>();
        services.AddSingleton<ICommandFeedback, CommandFeedbackService>();
        services.TryAddSingleton<ICommandConfirmationService, DenyingCommandConfirmationService>();
        services.TryAddSingleton<ICommandFeedback, NullCommandFeedback>();

        services.AddSingleton<ICommandDefinitionSource, MockCommandDefinitionSource>();
        services.AddCommandAction<MockCommandAction>(MockCommandAction.RegistrationName);

        // Built-in DictateFlow app actions (issue #30): Settings/History/Cost Dashboard/update
        // check and prompt-mode switching by voice, sharing the tray's code via IAppActions.
        services.AddCommandAction<DictateFlowAction>(DictateFlowAction.RegistrationName);
        services.AddSingleton<ICommandDefinitionSource, DictateFlowCommandDefinitionSource>();

        // Built-in launch actions (issue #27): each registers like a provider — one line — and
        // becomes part of the fixed allowlist. All three share one process-launch seam so they
        // stay testable. The built-in command set plus the user JSON command store are two more
        // definition sources the matcher aggregates automatically.
        services.TryAddSingleton<IProcessLauncher, ProcessLauncher>();
        services.AddCommandAction<ProcessStartAction>(ProcessStartAction.RegistrationName);
        services.AddCommandAction<OpenUrlAction>(OpenUrlAction.RegistrationName);
        services.AddCommandAction<OpenFolderAction>(OpenFolderAction.RegistrationName);
        services.AddSingleton<ICommandDefinitionSource, BuiltInCommandDefinitionSource>();
        services.AddSingleton<CommandStore>();
        services.AddSingleton<ICommandDefinitionSource>(sp => sp.GetRequiredService<CommandStore>());
        services.AddSingleton<IVoiceCommandStore>(sp => sp.GetRequiredService<CommandStore>());

        // Output pipeline (M5): history write, the mode-aware confirmation gate, and the
        // orchestrator itself.
        services.AddSingleton<IHistoryRepository, SqliteHistoryRepository>();
        services.AddSingleton<IOutputGate>(sp => new OutputGate(
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IForegroundAppService>(),
            sp.GetRequiredService<ITrayIconService>,
            sp.GetRequiredService<ILogger<OutputGate>>()));
        services.AddSingleton<IDictationPipeline, DictationPipeline>();

        services.AddSingleton<IDictationFailureNotifier, DictationFailureNotifier>();

        // Cloud recordings: poll an Azure Blob container for uploaded .m4a recordings
        // and transcribe new ones with the active transcription provider. The blob source is the
        // one vendor SDK (Azure.Storage.Blobs); everything else is Core/App. The poller is the
        // app's first hosted service — registered as a singleton so the review VM can subscribe to
        // its NewRecordingsTranscribed event, and added to the host to start with it.
        services.AddAzureBlobRecordings();
        services.AddSingleton<IAudioDecoder, MediaFoundationAudioDecoder>();
        services.AddSingleton<ICloudRecordingRepository, SqliteCloudRecordingRepository>();
        services.AddSingleton<ICloudTranscriptionService, CloudTranscriptionService>();
        services.AddSingleton<ICloudRecordingNotifier, CloudRecordingNotifier>();
        services.AddTransient<IRecordingPlayer, RecordingPlayer>();
        services.AddSingleton<CloudRecordingPollerService>();
        services.AddHostedService(sp => sp.GetRequiredService<CloudRecordingPollerService>());

        services.AddSingleton<TrayViewModel>();
        services.AddSingleton<OverlayViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<CostDashboardViewModel>();
        services.AddTransient<DictatePadViewModel>();
        services.AddTransient<CloudRecordingsViewModel>();

        return services;
    }
}
