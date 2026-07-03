using System.Globalization;
using DictateFlow.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.History;

/// <summary>
/// Default <see cref="IHistoryRepository"/> implementation writing to the <c>History</c>
/// table bootstrapped by <see cref="IDatabaseInitializer"/>. Reads the
/// <c>History.Enabled</c> and <c>History.MaxEntries</c> settings on every call, so
/// changing them in Settings applies immediately without a restart.
/// </summary>
public sealed class SqliteHistoryRepository : IHistoryRepository
{
    private readonly IAppPaths _appPaths;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<SqliteHistoryRepository> _logger;

    /// <summary>Initializes a new instance of the <see cref="SqliteHistoryRepository"/> class.</summary>
    /// <param name="appPaths">Resolves the location of the database file.</param>
    /// <param name="settingsService">Supplies the <c>History.Enabled</c> switch and the entry cap.</param>
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

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO History (TimestampUtc, FinalText) VALUES ($timestamp, $text);";
            command.Parameters.AddWithValue("$timestamp",
                DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$text", finalText);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogDebug("History entry written ({CharCount} characters)", finalText.Length);

        var maxEntries = _settingsService.Current.History.MaxEntries;
        if (maxEntries > 0)
        {
            await using var prune = connection.CreateCommand();
            prune.CommandText =
                """
                DELETE FROM History
                WHERE Id NOT IN (SELECT Id FROM History ORDER BY Id DESC LIMIT $max);
                """;
            prune.Parameters.AddWithValue("$max", maxEntries);
            var pruned = await prune.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (pruned > 0)
            {
                _logger.LogDebug("History pruned to {MaxEntries} entries ({PrunedCount} removed)", maxEntries, pruned);
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HistoryEntry>> SearchAsync(
        string? query, int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        if (string.IsNullOrWhiteSpace(query))
        {
            command.CommandText =
                "SELECT Id, TimestampUtc, FinalText FROM History ORDER BY Id DESC LIMIT $limit;";
        }
        else
        {
            command.CommandText =
                """
                SELECT Id, TimestampUtc, FinalText FROM History
                WHERE FinalText LIKE $pattern ESCAPE '\'
                ORDER BY Id DESC LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$pattern", $"%{EscapeLikePattern(query.Trim())}%");
        }

        command.Parameters.AddWithValue("$limit", limit);

        var entries = new List<HistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new HistoryEntry(
                reader.GetInt64(0),
                DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                reader.GetString(2)));
        }

        _logger.LogDebug("History search returned {Count} entries (query: {HasQuery})",
            entries.Count, !string.IsNullOrWhiteSpace(query));
        return entries;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM History WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("History entry {Id} deleted", id);
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM History;";
        var removed = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("History cleared ({RemovedCount} entries removed)", removed);
    }

    /// <summary>Opens a connection to the database file (schema is created by <see cref="IDatabaseInitializer"/>).</summary>
    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _appPaths.DatabaseFilePath,
            Mode = SqliteOpenMode.ReadWrite,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <summary>Escapes the SQL LIKE wildcards so user input matches literally.</summary>
    private static string EscapeLikePattern(string query)
        => query.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
