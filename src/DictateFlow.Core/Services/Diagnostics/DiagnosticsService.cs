using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using DictateFlow.Core.Services.Transfer;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Diagnostics;

/// <summary>
/// Default <see cref="IDiagnosticsService"/> implementation. The log tail is read with a
/// permissive file share so it works while Serilog holds the file open for writing.
/// </summary>
public sealed class DiagnosticsService : IDiagnosticsService
{
    private readonly IAppPaths _appPaths;
    private readonly ISettingsService _settingsService;
    private readonly ISettingsTransfer _settingsTransfer;
    private readonly ILogger<DiagnosticsService> _logger;

    /// <summary>Initializes a new instance of the <see cref="DiagnosticsService"/> class.</summary>
    /// <param name="appPaths">Supplies the data and log locations.</param>
    /// <param name="settingsService">Supplies the current settings for the report.</param>
    /// <param name="settingsTransfer">Serializes the settings with secrets redacted.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public DiagnosticsService(
        IAppPaths appPaths,
        ISettingsService settingsService,
        ISettingsTransfer settingsTransfer,
        ILogger<DiagnosticsService> logger)
    {
        _appPaths = appPaths;
        _settingsService = settingsService;
        _settingsTransfer = settingsTransfer;
        _logger = logger;
    }

    /// <inheritdoc />
    public string AppVersion { get; } = ResolveAppVersion();

    /// <inheritdoc />
    public string RuntimeVersion { get; } = RuntimeInformation.FrameworkDescription;

    /// <inheritdoc />
    public IReadOnlyList<string> ReadLogTail(int maxLines)
    {
        try
        {
            var newestLog = Directory.Exists(_appPaths.LogsDirectory)
                ? Directory.EnumerateFiles(_appPaths.LogsDirectory, "*.log")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault()
                : null;
            if (newestLog is null)
            {
                return ["No log file found."];
            }

            // FileShare.ReadWrite lets us read while Serilog keeps the file open for writing.
            using var stream = new FileStream(newestLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            var tail = new Queue<string>(maxLines);
            while (reader.ReadLine() is { } line)
            {
                if (tail.Count == maxLines)
                {
                    tail.Dequeue();
                }

                tail.Enqueue(line);
            }

            return [.. tail];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the log tail");
            return [$"Could not read the log file: {ex.Message}"];
        }
    }

    /// <inheritdoc />
    public string BuildReport()
    {
        var report = new StringBuilder();
        report.AppendLine($"DictateFlow {AppVersion}");
        report.AppendLine($"Runtime: {RuntimeVersion}");
        report.AppendLine($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        report.AppendLine();
        report.AppendLine($"Settings file: {_appPaths.SettingsFilePath}");
        report.AppendLine($"Database:      {_appPaths.DatabaseFilePath}");
        report.AppendLine($"Logs:          {_appPaths.LogsDirectory}");
        report.AppendLine($"Prompts:       {_appPaths.PromptsDirectory}");
        report.AppendLine();
        report.AppendLine("Settings (secrets redacted):");
        report.AppendLine(_settingsTransfer.ExportJson(_settingsService.Current, includeSecrets: false));
        return report.ToString();
    }

    /// <summary>The entry assembly's informational version, falling back to the assembly version.</summary>
    private static string ResolveAppVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(DiagnosticsService).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}
