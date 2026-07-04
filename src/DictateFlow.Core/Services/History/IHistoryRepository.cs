using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.History;

/// <summary>
/// Access to the dictation history stored in the local SQLite database. Audio is never
/// persisted, but the raw transcript and prompt mode are kept alongside the final text.
/// </summary>
public interface IHistoryRepository
{
    /// <summary>
    /// Appends a history entry, then prunes the oldest entries beyond the
    /// <c>History.MaxEntries</c> cap. A no-op when the <c>History.Enabled</c> setting is off.
    /// </summary>
    /// <param name="timestampUtc">When the dictation completed, in UTC.</param>
    /// <param name="finalText">The text that was delivered to the user.</param>
    /// <param name="rawTranscript">The raw speech-to-text transcript before enhancement, or <see langword="null"/> when unavailable.</param>
    /// <param name="promptModeName">The prompt mode that produced the final text, or <see langword="null"/> when unavailable.</param>
    /// <param name="cancellationToken">Cancels the pending database I/O.</param>
    Task AddAsync(
        DateTime timestampUtc,
        string finalText,
        string? rawTranscript = null,
        string? promptModeName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns entries newest-first, optionally filtered by a case-insensitive
    /// <c>LIKE %query%</c> match on the final text or the raw transcript.
    /// </summary>
    /// <param name="query">Substring to match; <see langword="null"/> or whitespace returns everything.</param>
    /// <param name="limit">Maximum number of entries to return.</param>
    /// <param name="cancellationToken">Cancels the pending database I/O.</param>
    Task<IReadOnlyList<HistoryEntry>> SearchAsync(string? query, int limit, CancellationToken cancellationToken = default);

    /// <summary>Deletes one entry by id; a no-op when the id does not exist.</summary>
    /// <param name="id">Database identity of the entry to delete.</param>
    /// <param name="cancellationToken">Cancels the pending database I/O.</param>
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Deletes all history entries.</summary>
    /// <param name="cancellationToken">Cancels the pending database I/O.</param>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
