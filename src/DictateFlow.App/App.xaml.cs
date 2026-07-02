using System.IO;
using System.Windows;
using System.Windows.Threading;
using DictateFlow.App.Services;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
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

    /// <summary>Builds the host, initializes services and shows the tray icon.</summary>
    /// <param name="e">Startup arguments.</param>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterGlobalExceptionHandlers();

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
            // subscription arms the global hotkey from the loaded settings. The result
            // presenter subscribes to the controller's transcription events the same way.
            _host.Services.GetRequiredService<IDictationController>();
            _host.Services.GetRequiredService<IDictationResultPresenter>();

            await _host.Services.GetRequiredService<ISettingsService>().LoadAsync();

            await _host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
            _logger.LogInformation("Database initialized");

            _trayIconService = _host.Services.GetRequiredService<ITrayIconService>();
            _trayIconService.Show();
            _logger.LogInformation("Tray icon shown; startup complete");
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
        }
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
    /// application's logs directory. The minimum level comes from configuration
    /// (<c>Logging:MinimumLevel</c>) and defaults to Information (Debug in debug builds).
    /// </summary>
    /// <param name="appPaths">Provides the logs directory.</param>
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
        _trayIconService?.ShowErrorNotification("DictateFlow error", e.Exception.Message);
        // Keep the app alive: a tray app should not die because one UI action failed.
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogError(e.Exception, "Unobserved task exception");
        _trayIconService?.ShowErrorNotification("DictateFlow error", e.Exception.GetBaseException().Message);
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
