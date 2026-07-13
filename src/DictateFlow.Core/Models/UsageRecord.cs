namespace DictateFlow.Core.Models;

/// <summary>
/// One billable provider call, reported to <c>IUsageSink</c> after each successful request
/// and persisted to the <c>UsageRecords</c> table with a cost estimated from the pricing
/// rates in effect at insert time.
/// </summary>
/// <param name="TimestampUtc">When the call completed, in UTC.</param>
/// <param name="Category">What was billed — see <see cref="UsageCategories"/>.</param>
/// <param name="DurationSeconds">Audio duration for transcription calls; <see langword="null"/> for token-billed calls.</param>
/// <param name="PromptTokens">Prompt tokens for LLM calls; <see langword="null"/> for duration-billed calls.</param>
/// <param name="CompletionTokens">Completion tokens for LLM calls; <see langword="null"/> for duration-billed calls.</param>
/// <param name="WordCount">Words dictated for a <see cref="UsageCategories.Dictation"/> record; <see langword="null"/> for billed calls.</param>
public sealed record UsageRecord(
    DateTime TimestampUtc,
    string Category,
    double? DurationSeconds,
    int? PromptTokens,
    int? CompletionTokens,
    int? WordCount = null);

/// <summary>Well-known <see cref="UsageRecord.Category"/> values (also stored in the <c>Category</c> column).</summary>
public static class UsageCategories
{
    /// <summary>Speech-to-text calls, billed by audio duration.</summary>
    public const string Speech = "Speech";

    /// <summary>LLM enhancement calls, billed by tokens.</summary>
    public const string Llm = "Llm";

    /// <summary>One delivered dictation, carrying the count of raw dictated words. Not billed.</summary>
    public const string Dictation = "Dictation";
}
