using System.Globalization;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.History;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DictateFlow.Tests;

/// <summary>Tests for <see cref="SqliteHistoryRepository"/> against a temp-file SQLite database.</summary>
public sealed class HistoryRepositoryTests : IDisposable
{
    private readonly TestAppPaths _paths = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly AppSettings _appSettings = new();
    private readonly SqliteHistoryRepository _repository;

    public HistoryRepositoryTests()
    {
        _settings.SetupGet(s => s.Current).Returns(_appSettings);
        _repository = new SqliteHistoryRepository(_paths, _settings.Object, NullLogger<SqliteHistoryRepository>.Instance);

        // The repository writes into the schema the initializer bootstraps at app startup.
        new DatabaseInitializer(_paths, NullLogger<DatabaseInitializer>.Instance)
            .InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        // Release pooled connections so the temp directory can be deleted.
        SqliteConnection.ClearAllPools();
        _paths.Dispose();
    }

    [Fact]
    public async Task AddAsync_InsertsRowReadableFromTheDatabase()
    {
        var timestamp = new DateTime(2026, 7, 2, 13, 45, 30, DateTimeKind.Utc);

        await _repository.AddAsync(timestamp, "Hello from café 你好");

        var rows = await ReadHistoryAsync(_paths);
        var row = Assert.Single(rows);
        Assert.Equal("Hello from café 你好", row.FinalText);
        Assert.Equal(timestamp, DateTime.Parse(row.TimestampUtc, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));
    }

    [Fact]
    public async Task AddAsync_MultipleEntries_AllPersisted()
    {
        await _repository.AddAsync(DateTime.UtcNow, "first");
        await _repository.AddAsync(DateTime.UtcNow, "second");

        Assert.Equal(["first", "second"], await ReadFinalTextsAsync(_paths));
    }

    [Fact]
    public async Task AddAsync_HistoryDisabled_SkipsWrite()
    {
        _appSettings.History.Enabled = false;

        await _repository.AddAsync(DateTime.UtcNow, "should not be stored");

        Assert.Empty(await ReadFinalTextsAsync(_paths));
    }

    [Fact]
    public async Task AddAsync_HistoryReenabled_WritesAgain()
    {
        _appSettings.History.Enabled = false;
        await _repository.AddAsync(DateTime.UtcNow, "while disabled");

        // The setting is read per call — no restart needed.
        _appSettings.History.Enabled = true;
        await _repository.AddAsync(DateTime.UtcNow, "while enabled");

        Assert.Equal(["while enabled"], await ReadFinalTextsAsync(_paths));
    }

    /// <summary>Reads all History rows straight from the database file (Id order).</summary>
    private static async Task<List<(string TimestampUtc, string FinalText)>> ReadHistoryAsync(IAppPaths paths)
    {
        await using var connection = new SqliteConnection($"Data Source={paths.DatabaseFilePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TimestampUtc, FinalText FROM History ORDER BY Id;";
        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<(string, string)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        return rows;
    }

    /// <summary>Reads the FinalText column of all History rows (Id order); shared with the pipeline end-to-end test.</summary>
    internal static async Task<List<string>> ReadFinalTextsAsync(IAppPaths paths)
        => [.. (await ReadHistoryAsync(paths)).Select(r => r.FinalText)];
}
