using DictateFlow.App.Services;
using DictateFlow.App.Services.Audio;
using DictateFlow.App.Services.Output;
using DictateFlow.App.ViewModels;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.History;
using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Output;
using DictateFlow.Core.Services.Pipeline;
using DictateFlow.Core.Services.Prompts;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Core.Services.Transcription;
using DictateFlow.Core.Services.Usage;
using DictateFlow.Providers.AzureFoundry;
using DictateFlow.Samples.NullOutput;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();

        services.AddSingleton<IWindowService, WindowService>();
        services.AddSingleton<IShutdownService, ShutdownService>();
        services.AddSingleton<ITrayIconService, TrayIconService>();

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

        // Mock providers are registered first: the first name of a kind is the fallback used
        // when the configured active name is unknown (and the dropdown default).
        services.AddTranscriptionProvider<MockTranscriptionProvider>(MockTranscriptionProvider.RegistrationName);
        services.AddTranscriptionProvider<AzureFoundryTranscriptionProvider>(AzureFoundryProviders.RegistrationName);
        services.AddLLMProvider<MockLLMProvider>(MockLLMProvider.RegistrationName);
        services.AddLLMProvider<AzureFoundryLLMProvider>(AzureFoundryProviders.RegistrationName);
        services.AddOutputProvider<ClipboardPasteOutputProvider>(OutputProviderNames.ClipboardPaste);
        services.AddOutputProvider<SimulatedKeyboardOutputProvider>(OutputProviderNames.SimulatedKeyboard);
        // The sample provider proving the extensibility claim: this line is its entire integration.
        services.AddOutputProvider<NullOutputProvider>(NullOutputProvider.RegistrationName);

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

        services.AddSingleton<TrayViewModel>();
        services.AddSingleton<OverlayViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<CostDashboardViewModel>();

        return services;
    }
}
