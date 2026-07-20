namespace DictateFlow.Core.Models;

/// <summary>
/// A transcribed cloud recording stored in the local database. The blob itself stays in the
/// container untouched; this row records that the blob was processed and the resulting text.
/// </summary>
/// <param name="Id">Database identity of the row.</param>
/// <param name="BlobName">The blob name within the container; unique, used to skip reprocessing.</param>
/// <param name="LastModifiedUtc">The blob's last-modified time in UTC at the time it was transcribed, when known.</param>
/// <param name="TranscribedUtc">When the transcription completed, in UTC.</param>
/// <param name="Transcript">The transcribed text.</param>
/// <param name="DurationSeconds">Duration of the audio in seconds, when the transcription provider reports it.</param>
public sealed record CloudRecordingEntry(
    long Id,
    string BlobName,
    DateTime? LastModifiedUtc,
    DateTime TranscribedUtc,
    string Transcript,
    double? DurationSeconds);
