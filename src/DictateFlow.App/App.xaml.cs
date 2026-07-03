using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using DictateFlow.App.Services;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.Prompts;
using DictateFlow.Core.Services.Startup;
using DictateFlow.Core.Services.Transcription;
using DictateFlow.Core.Services.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace DictateFlow.App;

/// <summary>
/// WPF application entry point. Builds and starts a Generic Host on startup, shows the tray
/// icon, and tears everything down cleanly on exit. No windows are shown at startup
/// (<c>ShutdownMode="OnExplicitShutdown"</c>).
/// </summary>
public partial class App : Application
{
    private IHost? _host;
    private ITrayIconService? _trayIconService;
    private Microsoft.Extensions.Logging.ILogger? _logger;

    /// <summary>
    /// Builds the host and shows the tray icon as fast as possible (target: &lt; 2 s,
    /// measured and logged), then finishes the non-critical initialization — database
    /// schema, prompt store, settings validation, startup-registration reconciliation and
    /// the first-run welcome — off the startup path.
    /// </summary>
    /// <param name="e">Startup arguments.</param>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterGlobalExceptionHandlers();
        var startupStopwatch = Stopwatch.StartNew();

        try
        {
            var appPaths = new AppPaths();
            appPaths.EnsureCreated();

            var builder = Host.CreateApplicationBuilder();
            ConfigureSerilog(appPaths, builder.Configuration);
            builder.Services.AddSerilog();
            builder.Services.AddDictateFlow(appPaths);

            _host = builder.Build();
            await _host.StartAsync();

            _logger = _host.Services.GetRequiredService<ILogger<App>>();
            _logger.LogInformation("Host built and started; app data at {Root}", appPaths.RootDirectory);

            // Materialize the dictation controller before settings load: its SettingsChanged
            // subscription arms the global hotkey from the loaded settings. The failure
            // notifier subscribes to the controller's events the same way.
            _host.Services.GetRequiredService<IDictationController>();
            _host.Services.GetRequiredService<IDictationFailureNotifier>();

            await _host.Services.GetRequiredService<ISettingsService>().LoadAsync();

            _trayIconService = _host.Services.GetRequiredService<ITrayIconService>();
            _trayIconService.Show();
            startupStopwatch.Stop();
            _logger.LogInformation(
                "Tray icon shown {ElapsedMs} ms after startup; deferring remaining initialization",
                startupStopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "DictateFlow failed to start");
            Log.CloseAndFlush();
            MessageBox.Show(
                $"DictateFlow failed to start:\n\n{ex.Message}",
                "DictateFlow",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        await DeferredStartupAsync();
    }

    /// <summary>
    /// The initialization that does not gate the tray icon: database schema, prompt store
    /// seeding/loading, settings validation (with mock fallback), launch-with-Windows
    /// reconciliation and the first-run welcome. Failures here are logged, never fatal.
    /// </summary>
    private async Task DeferredStartupAsync()
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            await _host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
            _logger?.LogInformation("Database initialized");

            // Load (and on first run seed) the prompt modes off the UI thread, so the
            // prompts folder is populated before the user first opens it.
            var promptModeStore = _host.Services.GetRequiredService<IPromptModeStore>();
            var promptModes = await Task.Run(promptModeStore.GetAll);
            _logger?.LogInformation("{Count} prompt modes available", promptModes.Count);

            ValidateSettingsAtStartup();
            ReconcileStartupRegistration();
            await ShowFirstRunWelcomeAsync();

            _logger?.LogInformation("Deferred startup work complete");
        }
        catch (Exception ex)
        {
            // The tray is already up — a deferred-init problem must not take the app down.
            _logger?.LogError(ex, "Deferred startup initialization failed");
        }
    }

    /// <summary>
    /// Runs the settings validator once at startup. All findings are logged; when the
    /// active speech/LLM configuration is unusable the affected provider falls back to
    /// Mock (in memory only — the user's file is left as-is to be fixed in Settings) and a
    /// tray notification says so. The app never fails to start over settings.
    /// </summary>
    private void ValidateSettingsAtStartup()
    {
        var settingsService = _host!.Services.GetRequiredService<ISettingsService>();
        var findings = _host.Services.GetRequiredService<ISettingsValidator>().Validate(settingsService.Current);

        foreach (var finding in findings)
        {
            if (finding.Severity == SettingsValidationSeverity.Error)
            {
                _logger?.LogError("Settings validation: [{Section}] {Message}", finding.Section, finding.Message);
            }
            else
            {
                _logger?.LogWarning("Settings validation: [{Section}] {Message}", finding.Section, finding.Message);
            }
        }

        var fallbacks = new List<string>();
        var activeProviders = settingsService.Current.ActiveProviders;
        if (findings.Any(f => f.Severity == SettingsValidationSeverity.Error && f.Section == "Speech"))
        {
            activeProviders.Transcription = MockTranscriptionProvider.RegistrationName;
            fallbacks.Add("speech");
        }

        if (findings.Any(f => f.Severity == SettingsValidationSeverity.Error && f.Section == "LLM"))
        {
            activeProviders.Llm = Core.Services.Llm.MockLLMProvider.RegistrationName;
            fallbacks.Add("LLM");
        }

        if (findings.Any(f => f.Severity == SettingsValidationSeverity.Error && f.Section == "Output"))
        {
            activeProviders.Output = "";
            fallbacks.Add("output");
        }

        if (fallbacks.Count > 0)
        {
            _logger?.LogWarning(
                "Active {Providers} provider configuration is unusable; falling back to defaults for this session",
                string.Join("/", fallbacks));
            var windowService = _host.Services.GetRequiredService<IWindowService>();
            _trayIconService?.ShowWarningNotification(
                "DictateFlow settings problem",
                $"The configured {string.Join(" and ", fallbacks)} provider is invalid — using the built-in fallback for now. Click to open Settings.",
                onClick: windowService.ShowSettingsWindow);
        }
    }

    /// <summary>
    /// Repairs the launch-with-Windows Run entry when the setting is on but the entry is
    /// missing or points at a stale executable path (e.g. the app was moved).
    /// </summary>
    private void ReconcileStartupRegistration()
    {
        var settings = _host!.Services.GetRequiredService<ISettingsService>().Current;
        var registration = _host.Services.GetRequiredService<IStartupRegistration>();
        if (registration.Reconcile(settings.General.LaunchAtStartup))
        {
            _logger?.LogInformation("Launch-with-Windows registration reconciled at startup");
        }
    }

    /// <summary>
    /// Shows the one-time welcome notification on a fresh install: the hotkey hint plus a
    /// pointer to Settings (clicking the notification opens it), then persists
    /// <c>General.FirstRunCompleted</c>.
    /// </summary>
    private async Task ShowFirstRunWelcomeAsync()
    {
        var settingsService = _host!.Services.GetRequiredService<ISettingsService>();
        if (settingsService.Current.General.FirstRunCompleted)
        {
            return;
        }

        var recording = settingsService.Current.Recording;
        var hotkey = !string.IsNullOrWhiteSpace(recording.PushToTalkHotkey)
            ? recording.PushToTalkHotkey
            : recording.ToggleHotkey;
        var windowService = _host.Services.GetRequiredService<IWindowService>();
        _trayIconService?.ShowInfoNotification(
            "Welcome to DictateFlow",
            $"Press {hotkey} to dictate into any app. Click here to configure speech and LLM providers in Settings.",
            onClick: windowService.ShowSettingsWindow);

        settingsService.Current.General.FirstRunCompleted = true;
        await settingsService.SaveAsync();
        _logger?.LogInformation("First-run welcome shown");
    }

    /// <summary>Disposes the tray icon, stops the host and flushes the log.</summary>
    /// <param name="e">Exit arguments.</param>
    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("Application shutting down");

        _trayIconService?.Dispose();

        if (_host is not null)
        {
            _host.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            _host.Dispose();
            _host = null;
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }

    /// <summary>
    /// Configures the static Serilog logger with a rolling daily file sink under the
    /// application's logs directory. The minimum level defaults to Information (Debug in
    /// debug builds), can be overridden by host configuration (<c>Logging:MinimumLevel</c>),
    /// and above all honors <c>Logging.MinimumLevel</c> from the app's own
    /// <c>settings.json</c>. It is read once here, before the host exists — changing the
    /// setting requires an application restart.
    /// </summary>
    /// <param name="appPaths">Provides the logs directory and the settings file location.</param>
    /// <param name="configuration">Host configuration used for the minimum level override.</param>
    private static void ConfigureSerilog(IAppPaths appPaths, IConfiguration configuration)
    {
#if DEBUG
        var minimumLevel = LogEventLevel.Debug;
#else
        var minimumLevel = LogEventLevel.Information;
#endif
        if (Enum.TryParse<LogEventLevel>(configuration["Logging:MinimumLevel"], ignoreCase: true, out var configured))
        {
            minimumLevel = configured;
        }

        if (TryReadSettingsFileLogLevel(appPaths.SettingsFilePath, out var fromSettings))
        {
            minimumLevel = fromSettings;
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(appPaths.LogsDirectory, "dictateflow-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.Debug()
            .CreateLogger();
    }

    /// <summary>
    /// Reads <c>Logging.MinimumLevel</c> from <c>settings.json</c> without the settings
    /// service (logging must be configured before the host is built). A missing or
    /// unreadable file, section or value simply yields <see langword="false"/> — startup
    /// never fails over a log-level preference.
    /// </summary>
    /// <param name="settingsFilePath">Full path of the app's <c>settings.json</c>.</param>
    /// <param name="level">The parsed level, when the method returns <see langword="true"/>.</param>
    private static bool TryReadSettingsFileLogLevel(string settingsFilePath, out LogEventLevel level)
    {
        level = default;
        try
        {
            if (!File.Exists(settingsFilePath))
            {
                return false;
            }

            var settings = System.Text.Json.JsonSerializer.Deserialize<Core.Models.AppSettings>(
                File.ReadAllText(settingsFilePath), SettingsService.SerializerOptions);
            return Enum.TryParse(settings?.Logging.MinimumLevel, ignoreCase: true, out level);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Wires the three global exception hooks: dispatcher, unobserved task and AppDomain.
    /// Errors are logged and surfaced as a non-blocking tray notification; only the
    /// AppDomain hook lets the process terminate (after flushing the log).
    /// </summary>
    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError(e.Exception, "Unhandled dispatcher exception");
        // No raw exception text reaches the user — the details are in the log.
        _trayIconService?.ShowErrorNotification(
            "DictateFlow error", "An unexpected error occurred — see the log for details.");
        // Keep the app alive: a tray app should not die because one UI action failed.
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogError(e.Exception, "Unobserved task exception");
        _trayIconService?.ShowErrorNotification(
            "DictateFlow error", "An unexpected error occurred — see the log for details.");
        e.SetObserved();
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        LogError(exception, $"Unhandled AppDomain exception (terminating: {e.IsTerminating})");
        Log.CloseAndFlush();
    }

    private void LogError(Exception? exception, string message)
    {
        if (_logger is not null)
        {
            _logger.LogError(exception, "{Message}", message);
        }
        else
        {
            Log.Error(exception, "{Message}", message);
        }
    }
}
