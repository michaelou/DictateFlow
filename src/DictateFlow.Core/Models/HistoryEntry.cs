namespace DictateFlow.Core.Models;

/// <summary>
/// One dictation history row as stored in the local database. Audio is never persisted, but the
/// raw transcript and the prompt mode that produced the final text are kept alongside it so the
/// enhancement can be reviewed and tuned.
/// </summary>
/// <param name="Id">Database identity of the entry.</param>
/// <param name="TimestampUtc">When the dictation was delivered, in UTC (convert to local time for display only).</param>
/// <param name="FinalText">The text that was delivered to the user.</param>
/// <param name="RawTranscript">The raw speech-to-text transcript before LLM enhancement; <see langword="null"/> for rows written before this was captured.</param>
/// <param name="PromptModeName">The prompt mode selected for the enhancement; <see langword="null"/> for rows written before this was captured.</param>
public sealed record HistoryEntry(
    long Id,
    DateTime TimestampUtc,
    string FinalText,
    string? RawTranscript = null,
    string? PromptModeName = null);
