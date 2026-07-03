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
using DictateFlow.Core.Services.Transcription;
using DictateFlow.Core.Services.Usage;
using DictateFlow.Providers.AzureFoundry;
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

        // Transcription: the Azure provider behind the standard resilience pipeline, the mock
        // fallback, and a selector that picks per call based on whether an endpoint is set.
        // This is the only place (besides the Providers project itself) that names Azure types.
        services.AddAzureFoundryTranscription();
        services.AddSingleton<MockTranscriptionProvider>();
        services.AddSingleton<ITranscriptionProvider>(sp => new TranscriptionProviderSelector(
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<AzureFoundryTranscriptionProvider>,
            sp.GetRequiredService<MockTranscriptionProvider>,
            sp.GetRequiredService<ILogger<TranscriptionProviderSelector>>()));

        // LLM enhancement: prompt modes + resolver + per-application mode selection, the Azure
        // chat-completions provider behind the same resilience pipeline, the mock fallback,
        // and endpoint-empty selection. Usage lands in SQLite for the cost dashboard.
        services.AddSingleton<IUsageSink, SqliteUsageSink>();
        services.AddSingleton<ICostService, SqliteCostService>();
        services.AddSingleton<IForegroundAppService, ForegroundAppService>();
        services.AddSingleton<IPromptModeStore, PromptModeStore>();
        services.AddSingleton<IPromptResolver, PromptResolver>();
        services.AddSingleton<IPromptModeSelector, PromptModeSelector>();
        services.AddAzureFoundryLlm();
        services.AddSingleton<MockLLMProvider>();
        services.AddSingleton<ILLMProvider>(sp => new LLMProviderSelector(
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<AzureFoundryLLMProvider>,
            sp.GetRequiredService<MockLLMProvider>,
            sp.GetRequiredService<ILogger<LLMProviderSelector>>()));

        // Output pipeline (M5): history write, both output providers (the pipeline picks per
        // call from settings), the mode-aware confirmation gate, and the orchestrator itself.
        services.AddSingleton<IHistoryRepository, SqliteHistoryRepository>();
        services.AddSingleton<IOutputProvider, ClipboardPasteOutputProvider>();
        services.AddSingleton<IOutputProvider, SimulatedKeyboardOutputProvider>();
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
