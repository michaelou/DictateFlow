using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Usage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="SqliteUsageSink"/> and <see cref="SqliteCostService"/> against a
/// temp-file SQLite database: cost math at insert time and local-time day/month grouping
/// with a fixed injected clock.
/// </summary>
public sealed class UsageSinkAndCostServiceTests : IDisposable
{
    private readonly TestAppPaths _paths = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly AppSettings _appSettings = new();
    private readonly TestTimeProvider _time = new();
    private readonly SqliteUsageSink _sink;
    private readonly SqliteCostService _costService;

    public UsageSinkAndCostServiceTests()
    {
        _settings.SetupGet(s => s.Current).Returns(_appSettings);
        _sink = new SqliteUsageSink(_paths, _settings.Object, NullLogger<SqliteUsageSink>.Instance);
        _costService = new SqliteCostService(_paths, _settings.Object, _time, NullLogger<SqliteCostService>.Instance);

        new DatabaseInitializer(_paths, NullLogger<DatabaseInitializer>.Instance)
            .InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _paths.Dispose();
    }

    private static UsageRecord SpeechRecord(DateTime timestampUtc, double seconds)
        => new(timestampUtc, UsageCategories.Speech, seconds, null, null);

    private static UsageRecord LlmRecord(DateTime timestampUtc, int promptTokens, int completionTokens)
        => new(timestampUtc, UsageCategories.Llm, null, promptTokens, completionTokens);

    [Fact]
    public void Record_SpeechCall_CostIsMinutesTimesRate()
    {
        _appSettings.Pricing.SpeechPerMinute = 0.006;

        _sink.Record(SpeechRecord(_time.UtcNow.UtcDateTime, seconds: 90)); // 1.5 minutes

        var row = Assert.Single(ReadUsageRows());
        Assert.Equal(UsageCategories.Speech, row.Category);
        Assert.Equal(90, row.DurationSeconds);
        Assert.Equal(1.5 * 0.006, row.EstimatedCost, precision: 10);
    }

    [Fact]
    public void Record_LlmCall_CostIsTokensPerMillionTimesRates()
    {
        _appSettings.Pricing.LlmPromptPer1M = 2.50;
        _appSettings.Pricing.LlmCompletionPer1M = 10.00;

        _sink.Record(LlmRecord(_time.UtcNow.UtcDateTime, promptTokens: 2_000_000, completionTokens: 500_000));

        var row = Assert.Single(ReadUsageRows());
        Assert.Equal(UsageCategories.Llm, row.Category);
        // 2M prompt × 2.50/1M + 0.5M completion × 10.00/1M = 5.00 + 5.00
        Assert.Equal(10.00, row.EstimatedCost, precision: 10);
    }

    [Fact]
    public void Record_RateChangesDoNotRewritePastRows()
    {
        _appSettings.Pricing.SpeechPerMinute = 0.006;
        _sink.Record(SpeechRecord(_time.UtcNow.UtcDateTime, seconds: 60));

        _appSettings.Pricing.SpeechPerMinute = 0.60;
        _sink.Record(SpeechRecord(_time.UtcNow.UtcDateTime, seconds: 60));

        var rows = ReadUsageRows();
        Assert.Equal(2, rows.Count);
        Assert.Equal(0.006, rows[0].EstimatedCost, precision: 10);
        Assert.Equal(0.60, rows[1].EstimatedCost, precision: 10);
    }

    [Fact]
    public void Record_DatabaseMissing_DoesNotThrow()
    {
        File.Delete(_paths.DatabaseFilePath);

        _sink.Record(SpeechRecord(_time.UtcNow.UtcDateTime, seconds: 60)); // must never throw
    }

    [Fact]
    public async Task GetSummaryAsync_EmptyDatabase_AllZeros()
    {
        var summary = await _costService.GetSummaryAsync();

        Assert.Equal(CostPeriod.Empty, summary.Today);
        Assert.Equal(CostPeriod.Empty, summary.ThisMonth);
        Assert.Equal(CostPeriod.Empty, summary.Lifetime);
        Assert.Equal("USD", summary.Currency);
    }

    [Fact]
    public async Task GetSummaryAsync_AggregatesSpeechAndLlmSeparately()
    {
        var now = _time.UtcNow.UtcDateTime; // 2026-07-02 12:00 UTC, zone = UTC
        _appSettings.Pricing.SpeechPerMinute = 0.006;
        _appSettings.Pricing.LlmPromptPer1M = 2.50;
        _appSettings.Pricing.LlmCompletionPer1M = 10.00;
        _sink.Record(SpeechRecord(now, seconds: 120));
        _sink.Record(SpeechRecord(now, seconds: 60));
        _sink.Record(LlmRecord(now, promptTokens: 1000, completionTokens: 200));

        var summary = await _costService.GetSummaryAsync();

        var today = summary.Today;
        Assert.Equal(2, today.SpeechRequests);
        Assert.Equal(3.0, today.SpeechMinutes, precision: 10);
        Assert.Equal(3.0 * 0.006, today.SpeechCost, precision: 10);
        Assert.Equal(1, today.LlmRequests);
        Assert.Equal(1000, today.PromptTokens);
        Assert.Equal(200, today.CompletionTokens);
        Assert.Equal(1000 / 1_000_000.0 * 2.50 + 200 / 1_000_000.0 * 10.00, today.LlmCost, precision: 10);
        Assert.Equal(today.SpeechCost + today.LlmCost, today.TotalCost, precision: 10);
        Assert.Equal(today, summary.ThisMonth);
        Assert.Equal(today, summary.Lifetime);
    }

    [Fact]
    public async Task GetSummaryAsync_DayAndMonthBoundaries_GroupCorrectly()
    {
        // Clock pinned to 2026-07-02 12:00 UTC with a UTC local zone.
        var today = new DateTime(2026, 7, 2, 8, 0, 0, DateTimeKind.Utc);
        var yesterday = new DateTime(2026, 7, 1, 23, 0, 0, DateTimeKind.Utc);
        var lastMonth = new DateTime(2026, 6, 30, 23, 0, 0, DateTimeKind.Utc);
        _sink.Record(SpeechRecord(today, seconds: 60));
        _sink.Record(SpeechRecord(yesterday, seconds: 60));
        _sink.Record(SpeechRecord(lastMonth, seconds: 60));

        var summary = await _costService.GetSummaryAsync();

        Assert.Equal(1, summary.Today.SpeechRequests);
        Assert.Equal(2, summary.ThisMonth.SpeechRequests);
        Assert.Equal(3, summary.Lifetime.SpeechRequests);
    }

    [Fact]
    public async Task GetSummaryAsync_BoundariesAreLocalTime()
    {
        // Fixed zone UTC+10 (no DST): local now is 2026-07-02 22:00. A record at 13:30 UTC
        // (23:30 local, same local day) is "today"; one at 11:00 UTC on July 1st
        // (21:00 local July 1st) is yesterday but still this month.
        _time.Zone = TimeZoneInfo.CreateCustomTimeZone("Test+10", TimeSpan.FromHours(10), "Test+10", "Test+10");
        _sink.Record(SpeechRecord(new DateTime(2026, 7, 2, 13, 30, 0, DateTimeKind.Utc), seconds: 60));
        _sink.Record(SpeechRecord(new DateTime(2026, 7, 1, 11, 0, 0, DateTimeKind.Utc), seconds: 60));
        // June 30th 15:00 UTC is July 1st 01:00 local — this month despite the June UTC date.
        _sink.Record(SpeechRecord(new DateTime(2026, 6, 30, 15, 0, 0, DateTimeKind.Utc), seconds: 60));
        // June 30th 13:00 UTC is June 30th 23:00 local — last month.
        _sink.Record(SpeechRecord(new DateTime(2026, 6, 30, 13, 0, 0, DateTimeKind.Utc), seconds: 60));

        var summary = await _costService.GetSummaryAsync();

        Assert.Equal(1, summary.Today.SpeechRequests);
        Assert.Equal(3, summary.ThisMonth.SpeechRequests);
        Assert.Equal(4, summary.Lifetime.SpeechRequests);
    }

    [Fact]
    public async Task GetSummaryAsync_CurrencyComesFromSettings()
    {
        _appSettings.Pricing.Currency = "EUR";

        var summary = await _costService.GetSummaryAsync();

        Assert.Equal("EUR", summary.Currency);
    }

    /// <summary>Reads all UsageRecords rows straight from the database file (Id order).</summary>
    private List<(string Category, double? DurationSeconds, double EstimatedCost)> ReadUsageRows()
    {
        using var connection = new SqliteConnection($"Data Source={_paths.DatabaseFilePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Category, DurationSeconds, EstimatedCost FROM UsageRecords ORDER BY Id;";
        using var reader = command.ExecuteReader();

        var rows = new List<(string, double?, double)>();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetDouble(1), reader.GetDouble(2)));
        }

        return rows;
    }
}
