using System.Globalization;
using DictateFlow.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Usage;

/// <summary>
/// Default <see cref="ICostService"/> implementation reading the <c>UsageRecords</c> table.
/// Rows are loaded once per call and aggregated in memory — usage volumes are tiny (a
/// handful of rows per dictation), so this stays simple and keeps the local-time boundary
/// math out of SQL. Costs come from the <c>EstimatedCost</c> column written at insert time,
/// so past rows keep the rates that were in effect when they were recorded.
/// </summary>
public sealed class SqliteCostService : ICostService
{
    private readonly IAppPaths _appPaths;
    private readonly ISettingsService _settingsService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SqliteCostService> _logger;

    /// <summary>Initializes a new instance of the <see cref="SqliteCostService"/> class.</summary>
    /// <param name="appPaths">Resolves the location of the database file.</param>
    /// <param name="settingsService">Supplies the display currency.</param>
    /// <param name="timeProvider">Supplies "now" and the local time zone for period boundaries (replaceable in tests).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public SqliteCostService(
        IAppPaths appPaths,
        ISettingsService settingsService,
        TimeProvider timeProvider,
        ILogger<SqliteCostService> logger)
    {
        _appPaths = appPaths;
        _settingsService = settingsService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CostSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var currency = _settingsService.Current.Pricing.Currency;
        if (!File.Exists(_appPaths.DatabaseFilePath))
        {
            return new CostSummary(CostPeriod.Empty, CostPeriod.Empty, CostPeriod.Empty, currency);
        }

        // Local-time period boundaries, converted to the UTC instants stored in the table.
        // TimeZoneInfo handles DST transitions that a fixed offset would get wrong.
        var zone = _timeProvider.LocalTimeZone;
        var localNow = _timeProvider.GetLocalNow();
        var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localNow.Date, DateTimeKind.Unspecified), zone);
        var monthStartUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(new DateTime(localNow.Year, localNow.Month, 1), DateTimeKind.Unspecified), zone);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _appPaths.DatabaseFilePath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT TimestampUtc, Category, DurationSeconds, PromptTokens, CompletionTokens, WordCount, EstimatedCost FROM UsageRecords;";

        var today = new PeriodAccumulator();
        var thisMonth = new PeriodAccumulator();
        var lifetime = new PeriodAccumulator();
        var rowCount = 0;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rowCount++;
            var timestampUtc = DateTime.Parse(reader.GetString(0), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            var category = reader.GetString(1);
            var duration = reader.IsDBNull(2) ? 0 : reader.GetDouble(2);
            var promptTokens = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);
            var completionTokens = reader.IsDBNull(4) ? 0 : reader.GetInt64(4);
            var words = reader.IsDBNull(5) ? 0 : reader.GetInt64(5);
            var cost = reader.GetDouble(6);

            lifetime.Add(category, duration, promptTokens, completionTokens, words, cost);
            if (timestampUtc >= monthStartUtc)
            {
                thisMonth.Add(category, duration, promptTokens, completionTokens, words, cost);
            }

            if (timestampUtc >= dayStartUtc)
            {
                today.Add(category, duration, promptTokens, completionTokens, words, cost);
            }
        }

        _logger.LogDebug("Cost summary computed from {RowCount} usage records", rowCount);
        return new CostSummary(today.ToPeriod(), thisMonth.ToPeriod(), lifetime.ToPeriod(), currency);
    }

    /// <summary>Mutable accumulator for one reporting period.</summary>
    private sealed class PeriodAccumulator
    {
        private int _speechRequests;
        private double _speechSeconds;
        private double _speechCost;
        private int _llmRequests;
        private long _promptTokens;
        private long _completionTokens;
        private double _llmCost;
        private long _words;

        public void Add(string category, double durationSeconds, long promptTokens, long completionTokens, long words, double cost)
        {
            if (string.Equals(category, UsageCategories.Speech, StringComparison.OrdinalIgnoreCase))
            {
                _speechRequests++;
                _speechSeconds += durationSeconds;
                _speechCost += cost;
            }
            else if (string.Equals(category, UsageCategories.Llm, StringComparison.OrdinalIgnoreCase))
            {
                _llmRequests++;
                _promptTokens += promptTokens;
                _completionTokens += completionTokens;
                _llmCost += cost;
            }
            else if (string.Equals(category, UsageCategories.Dictation, StringComparison.OrdinalIgnoreCase))
            {
                _words += words;
            }
        }

        public CostPeriod ToPeriod()
            => new(_speechRequests, _speechSeconds / 60.0, _speechCost,
                _llmRequests, _promptTokens, _completionTokens, _llmCost, _words);
    }
}
