namespace DictateFlow.Core.Models;

/// <summary>
/// One billable provider call, reported to <c>IUsageSink</c> after each successful request.
/// M6 persists these to SQLite and adds cost math; until then a no-op sink swallows them.
/// </summary>
/// <param name="TimestampUtc">When the call completed, in UTC.</param>
/// <param name="Category">What was billed — see <see cref="UsageCategories"/>.</param>
/// <param name="DurationSeconds">Audio duration for transcription calls; <see langword="null"/> for token-billed calls.</param>
/// <param name="PromptTokens">Prompt tokens for LLM calls; <see langword="null"/> for duration-billed calls.</param>
/// <param name="CompletionTokens">Completion tokens for LLM calls; <see langword="null"/> for duration-billed calls.</param>
public sealed record UsageRecord(
    DateTime TimestampUtc,
    string Category,
    double? DurationSeconds,
    int? PromptTokens,
    int? CompletionTokens);

/// <summary>Well-known <see cref="UsageRecord.Category"/> values.</summary>
public static class UsageCategories
{
    /// <summary>Speech-to-text calls, billed by audio duration.</summary>
    public const string Transcription = "Transcription";

    /// <summary>LLM enhancement calls, billed by tokens.</summary>
    public const string LlmEnhancement = "LlmEnhancement";
}
