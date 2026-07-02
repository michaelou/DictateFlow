namespace DictateFlow.Core.Services.History;

/// <summary>
/// Write access to the dictation history stored in the local SQLite database. Only the final
/// text is ever persisted — never audio or raw transcripts. The browsing/search UI arrives
/// in M6; M5 only needs the write path.
/// </summary>
public interface IHistoryRepository
{
    /// <summary>
    /// Appends a history entry. A no-op when the <c>History.Enabled</c> setting is off.
    /// </summary>
    /// <param name="timestampUtc">When the dictation completed, in UTC.</param>
    /// <param name="finalText">The text that was delivered to the user.</param>
    /// <param name="cancellationToken">Cancels the pending database I/O.</param>
    Task AddAsync(DateTime timestampUtc, string finalText, CancellationToken cancellationToken = default);
}
