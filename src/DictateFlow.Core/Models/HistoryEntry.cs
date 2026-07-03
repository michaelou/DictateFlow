namespace DictateFlow.Core.Models;

/// <summary>
/// One dictation history row as stored in the local database. Only the delivery timestamp
/// and the final text are ever persisted — never audio or raw transcripts.
/// </summary>
/// <param name="Id">Database identity of the entry.</param>
/// <param name="TimestampUtc">When the dictation was delivered, in UTC (convert to local time for display only).</param>
/// <param name="FinalText">The text that was delivered to the user.</param>
public sealed record HistoryEntry(long Id, DateTime TimestampUtc, string FinalText);
