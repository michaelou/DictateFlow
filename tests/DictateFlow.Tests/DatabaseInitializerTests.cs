using DictateFlow.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;

namespace DictateFlow.Tests;

/// <summary>Tests for <see cref="DatabaseInitializer"/>.</summary>
public sealed class DatabaseInitializerTests : IDisposable
{
    private readonly TestAppPaths _paths = new();
    private readonly DatabaseInitializer _initializer;

    public DatabaseInitializerTests()
    {
        _initializer = new DatabaseInitializer(_paths, Mock.Of<ILogger<DatabaseInitializer>>());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _paths.Dispose();
    }

    [Fact]
    public async Task InitializeAsync_CreatesDatabaseFile()
    {
        await _initializer.InitializeAsync();

        Assert.True(File.Exists(_paths.DatabaseFilePath));
    }

    [Fact]
    public async Task InitializeAsync_CreatesHistoryAndUsageRecordsTables()
    {
        await _initializer.InitializeAsync();

        var tables = await GetTableNamesAsync();
        Assert.Contains("History", tables);
        Assert.Contains("UsageRecords", tables);
    }

    [Fact]
    public async Task InitializeAsync_RunTwice_IsIdempotent()
    {
        await _initializer.InitializeAsync();
        await _initializer.InitializeAsync();

        var tables = await GetTableNamesAsync();
        Assert.Contains("History", tables);
        Assert.Contains("UsageRecords", tables);
    }

    [Fact]
    public async Task InitializeAsync_HistoryTableHasExpectedColumns()
    {
        await _initializer.InitializeAsync();

        var columns = await GetColumnNamesAsync("History");
        Assert.Equal(["Id", "TimestampUtc", "FinalText", "RawTranscript", "PromptModeName"], columns);
    }

    [Fact]
    public async Task InitializeAsync_MigratesLegacyHistoryTableWithoutTheNewColumns()
    {
        // Seed a pre-existing database shaped like an older release: no RawTranscript /
        // PromptModeName columns, and a row already present.
        await using (var connection = new SqliteConnection($"Data Source={_paths.DatabaseFilePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE History (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TimestampUtc TEXT NOT NULL,
                    FinalText TEXT NOT NULL
                );
                INSERT INTO History (TimestampUtc, FinalText) VALUES ('2020-01-01T00:00:00.0000000Z', 'legacy');
                """;
            await command.ExecuteNonQueryAsync();
        }
        SqliteConnection.ClearAllPools();

        await _initializer.InitializeAsync();

        var columns = await GetColumnNamesAsync("History");
        Assert.Equal(["Id", "TimestampUtc", "FinalText", "RawTranscript", "PromptModeName"], columns);

        // The migration must preserve the existing row (ALTER TABLE ADD COLUMN, not a rebuild).
        await using var verify = new SqliteConnection($"Data Source={_paths.DatabaseFilePath}");
        await verify.OpenAsync();
        await using var count = verify.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM History WHERE FinalText = 'legacy';";
        Assert.Equal(1L, Convert.ToInt64(await count.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task InitializeAsync_UsageRecordsTableHasExpectedColumns()
    {
        await _initializer.InitializeAsync();

        var columns = await GetColumnNamesAsync("UsageRecords");
        Assert.Equal(
            ["Id", "TimestampUtc", "Category", "Requests", "DurationSeconds", "PromptTokens", "CompletionTokens", "WordCount", "EstimatedCost"],
            columns);
    }

    [Fact]
    public async Task InitializeAsync_MigratesLegacyUsageRecordsTableWithoutWordCount()
    {
        // Seed a pre-existing database shaped like an older release: UsageRecords without the
        // WordCount column, with a row already present.
        await using (var connection = new SqliteConnection($"Data Source={_paths.DatabaseFilePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE UsageRecords (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TimestampUtc TEXT NOT NULL,
                    Category TEXT NOT NULL,
                    Requests INTEGER NOT NULL DEFAULT 1,
                    DurationSeconds REAL NULL,
                    PromptTokens INTEGER NULL,
                    CompletionTokens INTEGER NULL,
                    EstimatedCost REAL NOT NULL DEFAULT 0
                );
                INSERT INTO UsageRecords (TimestampUtc, Category, DurationSeconds, EstimatedCost)
                VALUES ('2020-01-01T00:00:00.0000000Z', 'Speech', 60, 0.006);
                """;
            await command.ExecuteNonQueryAsync();
        }
        SqliteConnection.ClearAllPools();

        await _initializer.InitializeAsync();

        var columns = await GetColumnNamesAsync("UsageRecords");
        Assert.Contains("WordCount", columns);

        // The migration must preserve the existing row (ALTER TABLE ADD COLUMN, not a rebuild).
        await using var verify = new SqliteConnection($"Data Source={_paths.DatabaseFilePath}");
        await verify.OpenAsync();
        await using var count = verify.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM UsageRecords WHERE Category = 'Speech';";
        Assert.Equal(1L, Convert.ToInt64(await count.ExecuteScalarAsync()));
    }

    private async Task<List<string>> GetTableNamesAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={_paths.DatabaseFilePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private async Task<List<string>> GetColumnNamesAsync(string table)
    {
        await using var connection = new SqliteConnection($"Data Source={_paths.DatabaseFilePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(1));
        }

        return names;
    }
}
