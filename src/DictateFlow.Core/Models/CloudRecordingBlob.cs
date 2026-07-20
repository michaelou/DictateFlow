namespace DictateFlow.Core.Models;

/// <summary>
/// One recording blob discovered in the configured Azure Blob Storage container.
/// </summary>
/// <param name="Name">The blob name (its full path within the container), used as the stable identity.</param>
/// <param name="LastModifiedUtc">When the blob was last modified, in UTC, when the service reports it.</param>
/// <param name="SizeBytes">The blob size in bytes, when the service reports it.</param>
/// <param name="ContentType">The blob content type (e.g. <c>audio/mp4</c>), when the service reports it.</param>
public sealed record CloudRecordingBlob(
    string Name,
    DateTime? LastModifiedUtc,
    long? SizeBytes,
    string? ContentType);
