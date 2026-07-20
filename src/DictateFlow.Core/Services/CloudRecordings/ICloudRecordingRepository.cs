using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.CloudRecordings;

/// <summary>
/// Access to the transcribed cloud recordings stored in the local SQLite database. Only the
/// blob name and transcript are kept — the audio stays in the container.
/// </summary>
public interface ICloudRecordingRepository
{
    /// <summary>Returns the set of blob names already transcribed, so the poller can skip them.</summary>
    /// <param name="cancellationToken">Cancels the pending database I/O.</param>
    Task<IReadOnlySet<string>> GetProcessedBlobNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a transcribed recording. A no-op (idempotent) when a row for the same blob name
    /// already exists, so a re-run never inserts duplicates.
    /// </summary>
    /// <param name="blobName">The blob name within the container.</param>
    /// <param name="lastModifiedUtc">The blob's last-modified time in UTC, when known.</param>
    /// <param name="transcribedUtc">When the transcription completed, in UTC.</param>
    /// <param name="transcript">The transcribed text.</param>
    /// <param name="durationSeconds">Duration of the audio in seconds, when known.</param>
    /// <param name="cancellationToken">Cancels the pending database I/O.</param>
    Task AddAsync(
        string blobName,
        DateTime? lastModifiedUtc,
        DateTime transcribedUtc,
        string transcript,
        double? durationSeconds,
        CancellationToken cancellationToken = default);

    /// <summary>Returns all transcribed recordings, newest first.</summary>
    /// <param name="cancellationToken">Cancels the pending database I/O.</param>
    Task<IReadOnlyList<CloudRecordingEntry>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Deletes one entry by id; a no-op when the id does not exist.</summary>
    /// <param name="id">Database identity of the entry to delete.</param>
    /// <param name="cancellationToken">Cancels the pending database I/O.</param>
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
