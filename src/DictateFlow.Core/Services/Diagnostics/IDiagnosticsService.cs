namespace DictateFlow.Core.Services.Diagnostics;

/// <summary>
/// Supplies the information shown on the Diagnostics settings page: version numbers, the
/// tail of the current log file and a copyable bug-report text with all secrets redacted.
/// </summary>
public interface IDiagnosticsService
{
    /// <summary>Gets the application version (informational version when available).</summary>
    string AppVersion { get; }

    /// <summary>Gets the .NET runtime description (e.g. <c>.NET 10.0.1</c>).</summary>
    string RuntimeVersion { get; }

    /// <summary>
    /// Reads the last lines of the newest log file. Never throws — an unreadable or missing
    /// log yields a single explanatory line.
    /// </summary>
    /// <param name="maxLines">The maximum number of lines returned.</param>
    IReadOnlyList<string> ReadLogTail(int maxLines);

    /// <summary>
    /// Builds the "Copy diagnostics" bug-report text: versions, OS, data paths and the
    /// current settings as JSON with every API key redacted.
    /// </summary>
    string BuildReport();
}
