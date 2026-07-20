using System.IO;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.Transcription;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.CloudRecordings;

/// <summary>
/// Default <see cref="ICloudTranscriptionService"/> implementation. Lists the configured
/// container, and for every blob not already in the local database downloads it, decodes the
/// <c>.m4a</c> to 16 kHz/16-bit/mono WAV, runs it through the active transcription provider and
/// stores the transcript. Per-recording failures are logged and skipped so one bad file never
/// aborts the batch. Temporary files live under <c>%TEMP%\DictateFlow\CloudRecordings</c> and
/// are cleaned up after each recording.
/// </summary>
public sealed class CloudTranscriptionService : ICloudTranscriptionService
{
    private readonly ICloudRecordingSource _source;
    private readonly ICloudRecordingRepository _repository;
    private readonly IAudioDecoder _decoder;
    private readonly ITranscriptionProvider _transcriptionProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CloudTranscriptionService> _logger;

    /// <summary>Initializes a new instance of the <see cref="CloudTranscriptionService"/> class.</summary>
    /// <param name="source">Lists and downloads recording blobs from cloud storage.</param>
    /// <param name="repository">Tracks which blobs have been transcribed and stores the transcripts.</param>
    /// <param name="decoder">Decodes the downloaded <c>.m4a</c> to the WAV format the provider expects.</param>
    /// <param name="transcriptionProvider">The active transcription provider (resolved per call).</param>
    /// <param name="timeProvider">Timestamps the stored transcriptions (replaceable in tests).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public CloudTranscriptionService(
        ICloudRecordingSource source,
        ICloudRecordingRepository repository,
        IAudioDecoder decoder,
        ITranscriptionProvider transcriptionProvider,
        TimeProvider timeProvider,
        ILogger<CloudTranscriptionService> logger)
    {
        _source = source;
        _repository = repository;
        _decoder = decoder;
        _transcriptionProvider = transcriptionProvider;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> CheckAndTranscribeNewAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("Checking for new recordings…");
        var blobs = await _source.ListAsync(cancellationToken).ConfigureAwait(false);
        var processed = await _repository.GetProcessedBlobNamesAsync(cancellationToken).ConfigureAwait(false);

        var pending = blobs.Where(b => !processed.Contains(b.Name)).ToList();
        if (pending.Count == 0)
        {
            _logger.LogInformation("Cloud recordings check: {Total} blob(s), nothing new to transcribe", blobs.Count);
            progress?.Report("No new recordings.");
            return 0;
        }

        _logger.LogInformation(
            "Cloud recordings check: {Total} blob(s), {New} new to transcribe", blobs.Count, pending.Count);

        var workingDirectory = Path.Combine(Path.GetTempPath(), "DictateFlow", "CloudRecordings");
        Directory.CreateDirectory(workingDirectory);

        var transcribed = 0;
        for (var i = 0; i < pending.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var blob = pending[i];
            progress?.Report($"Transcribing {i + 1} of {pending.Count}…");

            if (await TryTranscribeAsync(blob, workingDirectory, cancellationToken).ConfigureAwait(false))
            {
                transcribed++;
            }
        }

        progress?.Report(transcribed == 1 ? "Transcribed 1 new recording." : $"Transcribed {transcribed} new recordings.");
        return transcribed;
    }

    /// <summary>
    /// Transcribes one blob end to end. Returns <see langword="false"/> (logged, not thrown) on
    /// any failure so the caller can keep going with the rest of the batch. Caller-initiated
    /// cancellation is rethrown.
    /// </summary>
    private async Task<bool> TryTranscribeAsync(
        CloudRecordingBlob blob, string workingDirectory, CancellationToken cancellationToken)
    {
        // A stable, collision-free stem per blob (blob names can contain path separators).
        var stem = Path.Combine(workingDirectory, Guid.NewGuid().ToString("N"));
        var extension = Path.GetExtension(blob.Name);
        var audioPath = stem + (string.IsNullOrEmpty(extension) ? ".m4a" : extension);
        var wavPath = stem + ".wav";

        try
        {
            await _source.DownloadToFileAsync(blob.Name, audioPath, cancellationToken).ConfigureAwait(false);
            await _decoder.DecodeToWav16kMonoAsync(audioPath, wavPath, cancellationToken).ConfigureAwait(false);

            TranscriptionResult result;
            await using (var wav = new FileStream(wavPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                result = await _transcriptionProvider.TranscribeAsync(wav, cancellationToken).ConfigureAwait(false);
            }

            await _repository.AddAsync(
                blob.Name,
                blob.LastModifiedUtc,
                _timeProvider.GetUtcNow().UtcDateTime,
                result.Text,
                result.AudioDurationSeconds,
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Transcribed cloud recording '{BlobName}'", blob.Name);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not transcribe cloud recording '{BlobName}'; skipping it", blob.Name);
            return false;
        }
        finally
        {
            TryDelete(audioPath);
            TryDelete(wavPath);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not delete temporary file '{Path}'", path);
        }
    }
}
