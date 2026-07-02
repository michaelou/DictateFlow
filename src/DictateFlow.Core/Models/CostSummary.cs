namespace DictateFlow.Core.Models;

/// <summary>
/// Aggregated usage and estimated cost for one reporting period of the cost dashboard.
/// </summary>
/// <param name="SpeechRequests">Number of speech-to-text calls.</param>
/// <param name="SpeechMinutes">Total minutes of audio transcribed.</param>
/// <param name="SpeechCost">Estimated cost of the speech calls.</param>
/// <param name="LlmRequests">Number of LLM enhancement calls.</param>
/// <param name="PromptTokens">Total prompt tokens sent.</param>
/// <param name="CompletionTokens">Total completion tokens received.</param>
/// <param name="LlmCost">Estimated cost of the LLM calls.</param>
public sealed record CostPeriod(
    int SpeechRequests,
    double SpeechMinutes,
    double SpeechCost,
    int LlmRequests,
    long PromptTokens,
    long CompletionTokens,
    double LlmCost)
{
    /// <summary>An all-zero period, used before any usage exists.</summary>
    public static CostPeriod Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

    /// <summary>Gets the combined estimated cost of the period.</summary>
    public double TotalCost => SpeechCost + LlmCost;
}

/// <summary>
/// The cost dashboard aggregate: usage and estimated cost for today, the current month and
/// the full lifetime of the database. Period boundaries are local-time; storage stays UTC.
/// </summary>
/// <param name="Today">Usage since local midnight.</param>
/// <param name="ThisMonth">Usage since the first of the current local month.</param>
/// <param name="Lifetime">All recorded usage.</param>
/// <param name="Currency">Display currency code from the pricing settings.</param>
public sealed record CostSummary(CostPeriod Today, CostPeriod ThisMonth, CostPeriod Lifetime, string Currency);
