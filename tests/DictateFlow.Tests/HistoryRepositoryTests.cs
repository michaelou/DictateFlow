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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public async Task AddAsync_EmptyOrWhitespaceText_SkipsWrite(string text)
    {
        // Nothing said during dictation resolves to blank text; it must not clutter history.
        await _repository.AddAsync(DateTime.UtcNow, text);

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

    [Fact]
    public async Task SearchAsync_NoQuery_ReturnsEverythingNewestFirst()
    {
        await _repository.AddAsync(DateTime.UtcNow, "first");
        await _repository.AddAsync(DateTime.UtcNow, "second");
        await _repository.AddAsync(DateTime.UtcNow, "third");

        var entries = await _repository.SearchAsync(null, limit: 100);

        Assert.Equal(["third", "second", "first"], entries.Select(e => e.FinalText));
    }

    [Fact]
    public async Task SearchAsync_Query_MatchesSubstringCaseInsensitively()
    {
        await _repository.AddAsync(DateTime.UtcNow, "Meeting notes about Belugga");
        await _repository.AddAsync(DateTime.UtcNow, "Grocery list");
        await _repository.AddAsync(DateTime.UtcNow, "belugga follow-up");

        var entries = await _repository.SearchAsync("BELUGGA", limit: 100);

        Assert.Equal(["belugga follow-up", "Meeting notes about Belugga"], entries.Select(e => e.FinalText));
    }

    [Fact]
    public async Task SearchAsync_LimitCapsTheResultCount()
    {
        for (var i = 1; i <= 5; i++)
        {
            await _repository.AddAsync(DateTime.UtcNow, $"entry {i}");
        }

        var entries = await _repository.SearchAsync(null, limit: 2);

        Assert.Equal(["entry 5", "entry 4"], entries.Select(e => e.FinalText));
    }

    [Fact]
    public async Task SearchAsync_LikeWildcardsInQuery_MatchLiterally()
    {
        await _repository.AddAsync(DateTime.UtcNow, "100% done");
        await _repository.AddAsync(DateTime.UtcNow, "100 percent done");

        var entries = await _repository.SearchAsync("100%", limit: 100);

        Assert.Equal(["100% done"], entries.Select(e => e.FinalText));
    }

    [Fact]
    public async Task SearchAsync_ReturnsUtcTimestampAndId()
    {
        var timestamp = new DateTime(2026, 7, 2, 13, 45, 30, DateTimeKind.Utc);
        await _repository.AddAsync(timestamp, "hello");

        var entry = Assert.Single(await _repository.SearchAsync(null, limit: 10));

        Assert.True(entry.Id > 0);
        Assert.Equal(timestamp, entry.TimestampUtc);
        Assert.Equal(DateTimeKind.Utc, entry.TimestampUtc.Kind);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyTheGivenEntry()
    {
        await _repository.AddAsync(DateTime.UtcNow, "keep me");
        await _repository.AddAsync(DateTime.UtcNow, "delete me");
        var target = (await _repository.SearchAsync("delete me", limit: 10)).Single();

        await _repository.DeleteAsync(target.Id);

        Assert.Equal(["keep me"], await ReadFinalTextsAsync(_paths));
    }

    [Fact]
    public async Task ClearAsync_RemovesEverything()
    {
        await _repository.AddAsync(DateTime.UtcNow, "one");
        await _repository.AddAsync(DateTime.UtcNow, "two");

        await _repository.ClearAsync();

        Assert.Empty(await ReadFinalTextsAsync(_paths));
    }

    [Fact]
    public async Task AddAsync_BeyondMaxEntries_PrunesTheOldest()
    {
        _appSettings.History.MaxEntries = 3;

        for (var i = 1; i <= 5; i++)
        {
            await _repository.AddAsync(DateTime.UtcNow, $"entry {i}");
        }

        Assert.Equal(["entry 3", "entry 4", "entry 5"], await ReadFinalTextsAsync(_paths));
    }

    [Fact]
    public async Task AddAsync_MaxEntriesZero_KeepsEverything()
    {
        _appSettings.History.MaxEntries = 0;

        for (var i = 1; i <= 5; i++)
        {
            await _repository.AddAsync(DateTime.UtcNow, $"entry {i}");
        }

        Assert.Equal(5, (await ReadFinalTextsAsync(_paths)).Count);
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
