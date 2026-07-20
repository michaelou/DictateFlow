namespace DictateFlow.Core.Services.CloudRecordings;

/// <summary>
/// Orchestrates the cloud recording workflow: list the container, transcribe blobs that have
/// not been processed yet with the active transcription provider, and persist the results.
/// </summary>
public interface ICloudTranscriptionService
{
    /// <summary>
    /// Lists the configured container, transcribes every recording not already in the local
    /// database, and stores each transcript. Per-recording failures are logged and skipped so
    /// one bad file never aborts the batch.
    /// </summary>
    /// <param name="progress">Optional status callback for the UI (e.g. "Transcribing 2 of 5…").</param>
    /// <param name="cancellationToken">Cancels the whole run.</param>
    /// <returns>The number of recordings newly transcribed.</returns>
    /// <exception cref="ProviderException">Listing the container failed; individual transcription failures do not surface here.</exception>
    Task<int> CheckAndTranscribeNewAsync(IProgress<string>? progress, CancellationToken cancellationToken);
}
