using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.History;

/// <summary>
/// Default <see cref="IHistoryRepository"/> implementation writing to the <c>History</c>
/// table bootstrapped by <see cref="IDatabaseInitializer"/>. Reads the
/// <c>History.Enabled</c> setting on every call, so toggling it in Settings applies
/// immediately without a restart.
/// </summary>
public sealed class SqliteHistoryRepository : IHistoryRepository
{
    private readonly IAppPaths _appPaths;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<SqliteHistoryRepository> _logger;

    /// <summary>Initializes a new instance of the <see cref="SqliteHistoryRepository"/> class.</summary>
    /// <param name="appPaths">Resolves the location of the database file.</param>
    /// <param name="settingsService">Supplies the <c>History.Enabled</c> switch.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public SqliteHistoryRepository(
        IAppPaths appPaths,
        ISettingsService settingsService,
        ILogger<SqliteHistoryRepository> logger)
    {
        _appPaths = appPaths;
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task AddAsync(DateTime timestampUtc, string finalText, CancellationToken cancellationToken = default)
    {
        if (!_settingsService.Current.History.Enabled)
        {
            _logger.LogDebug("History is disabled; entry not persisted");
            return;
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _appPaths.DatabaseFilePath,
            Mode = SqliteOpenMode.ReadWrite,
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO History (TimestampUtc, FinalText) VALUES ($timestamp, $text);";
        command.Parameters.AddWithValue("$timestamp",
            DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$text", finalText);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("History entry written ({CharCount} characters)", finalText.Length);
    }
}
