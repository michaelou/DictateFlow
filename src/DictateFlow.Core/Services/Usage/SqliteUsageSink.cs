using System.Globalization;
using DictateFlow.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Usage;

/// <summary>
/// <see cref="IUsageSink"/> implementation persisting each billable call to the
/// <c>UsageRecords</c> table bootstrapped by <see cref="IDatabaseInitializer"/>. The
/// estimated cost is computed from the <c>Pricing</c> rates in effect at insert time, so
/// later rate changes do not rewrite past costs. Never throws — a failed write is logged
/// and the dictation continues.
/// </summary>
public sealed class SqliteUsageSink : IUsageSink
{
    private readonly IAppPaths _appPaths;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<SqliteUsageSink> _logger;

    /// <summary>Initializes a new instance of the <see cref="SqliteUsageSink"/> class.</summary>
    /// <param name="appPaths">Resolves the location of the database file.</param>
    /// <param name="settingsService">Supplies the pricing rates used for the cost estimate.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public SqliteUsageSink(
        IAppPaths appPaths,
        ISettingsService settingsService,
        ILogger<SqliteUsageSink> logger)
    {
        _appPaths = appPaths;
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Record(UsageRecord record)
    {
        try
        {
            var estimatedCost = EstimateCost(record, _settingsService.Current.Pricing);

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _appPaths.DatabaseFilePath,
                Mode = SqliteOpenMode.ReadWrite,
            }.ToString();

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO UsageRecords
                    (TimestampUtc, Category, Requests, DurationSeconds, PromptTokens, CompletionTokens, WordCount, EstimatedCost)
                VALUES ($timestamp, $category, 1, $duration, $promptTokens, $completionTokens, $wordCount, $cost);
                """;
            command.Parameters.AddWithValue("$timestamp",
                DateTime.SpecifyKind(record.TimestampUtc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$category", record.Category);
            command.Parameters.AddWithValue("$duration", (object?)record.DurationSeconds ?? DBNull.Value);
            command.Parameters.AddWithValue("$promptTokens", (object?)record.PromptTokens ?? DBNull.Value);
            command.Parameters.AddWithValue("$completionTokens", (object?)record.CompletionTokens ?? DBNull.Value);
            command.Parameters.AddWithValue("$wordCount", (object?)record.WordCount ?? DBNull.Value);
            command.Parameters.AddWithValue("$cost", estimatedCost);
            command.ExecuteNonQuery();

            _logger.LogDebug(
                "Usage recorded: {Category}, {DurationSeconds:F1} s, {PromptTokens}+{CompletionTokens} tokens, {WordCount} words, estimated cost {EstimatedCost:F6}",
                record.Category, record.DurationSeconds, record.PromptTokens, record.CompletionTokens, record.WordCount, estimatedCost);
        }
        catch (Exception ex)
        {
            // The sink must never fail a dictation over bookkeeping.
            _logger.LogError(ex, "Failed to persist a usage record ({Category})", record.Category);
        }
    }

    /// <summary>Computes the estimated cost of one call from the given pricing rates.</summary>
    /// <param name="record">The call to price.</param>
    /// <param name="pricing">The rates in effect.</param>
    /// <returns>The estimated cost in the configured currency.</returns>
    public static double EstimateCost(UsageRecord record, PricingSettings pricing)
        => record.Category switch
        {
            UsageCategories.Speech => (record.DurationSeconds ?? 0) / 60.0 * pricing.SpeechPerMinute,
            UsageCategories.Llm => ((record.PromptTokens ?? 0) / 1_000_000.0 * pricing.LlmPromptPer1M)
                + ((record.CompletionTokens ?? 0) / 1_000_000.0 * pricing.LlmCompletionPer1M),
            _ => 0,
        };
}
