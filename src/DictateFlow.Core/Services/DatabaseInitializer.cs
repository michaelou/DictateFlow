using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services;

/// <summary>
/// Default <see cref="IDatabaseInitializer"/> implementation. Creates <c>dictateflow.db</c>
/// and bootstraps the <c>History</c> (M5/M6) and <c>UsageRecords</c> (M6) tables using
/// idempotent <c>CREATE TABLE IF NOT EXISTS</c> statements.
/// </summary>
public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private const string SchemaSql =
        """
        CREATE TABLE IF NOT EXISTS History (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            TimestampUtc TEXT NOT NULL,
            FinalText TEXT NOT NULL,
            RawTranscript TEXT NULL,
            PromptModeName TEXT NULL
        );
        CREATE TABLE IF NOT EXISTS UsageRecords (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            TimestampUtc TEXT NOT NULL,
            Category TEXT NOT NULL,
            Requests INTEGER NOT NULL DEFAULT 1,
            DurationSeconds REAL NULL,
            PromptTokens INTEGER NULL,
            CompletionTokens INTEGER NULL,
            EstimatedCost REAL NOT NULL DEFAULT 0
        );
        """;

    private readonly IAppPaths _appPaths;
    private readonly ILogger<DatabaseInitializer> _logger;

    /// <summary>Initializes a new instance of the <see cref="DatabaseInitializer"/> class.</summary>
    /// <param name="appPaths">Resolves the location of the database file.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public DatabaseInitializer(IAppPaths appPaths, ILogger<DatabaseInitializer> logger)
    {
        _appPaths = appPaths;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var path = _appPaths.DatabaseFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = SchemaSql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Migrate databases created before these columns existed. CREATE TABLE IF NOT EXISTS
        // leaves an already-present table untouched, so the columns are added explicitly.
        await EnsureColumnAsync(connection, "History", "RawTranscript", "TEXT NULL", cancellationToken)
            .ConfigureAwait(false);
        await EnsureColumnAsync(connection, "History", "PromptModeName", "TEXT NULL", cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Database initialized at {Path}", path);
    }

    /// <summary>Adds a column to a table when it is missing; a no-op when the column already exists.</summary>
    private static async Task EnsureColumnAsync(
        SqliteConnection connection, string table, string column, string definition, CancellationToken cancellationToken)
    {
        await using (var check = connection.CreateCommand())
        {
            check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $column;";
            check.Parameters.AddWithValue("$column", column);
            var exists = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) > 0;
            if (exists)
            {
                return;
            }
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
