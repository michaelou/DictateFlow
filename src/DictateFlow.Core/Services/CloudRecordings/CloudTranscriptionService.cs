using System.IO;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.History;
using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Prompts;
using DictateFlow.Core.Services.Transcription;
using DictateFlow.Core.Text;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.CloudRecordings;

/// <summary>
/// Default <see cref="ICloudTranscriptionService"/> implementation. Lists the configured
/// container, and for every blob not already in the local database downloads it, decodes the
/// <c>.m4a</c> to 16 kHz/16-bit/mono WAV, runs it through the active transcription provider,
/// optionally enhances the transcript with the configured prompt mode, and stores the result.
/// Each stored recording is also written to the dictation history and counted in the usage
/// metrics. Per-recording failures are logged and skipped so one bad file never aborts the
/// batch. Temporary files live under <c>%TEMP%\DictateFlow\CloudRecordings</c> and are cleaned
/// up after each recording.
/// </summary>
public sealed class CloudTranscriptionService : ICloudTranscriptionService
{
    private readonly ICloudRecordingSource _source;
    private readonly ICloudRecordingRepository _repository;
    private readonly IAudioDecoder _decoder;
    private readonly ITranscriptionProvider _transcriptionProvider;
    private readonly IPromptResolver _promptResolver;
    private readonly ILLMProvider _llmProvider;
    private readonly IHistoryRepository _historyRepository;
    private readonly IUsageSink _usageSink;
    private readonly ISettingsService _settingsService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CloudTranscriptionService> _logger;

    /// <summary>Initializes a new instance of the <see cref="CloudTranscriptionService"/> class.</summary>
    /// <param name="source">Lists and downloads recording blobs from cloud storage.</param>
    /// <param name="repository">Tracks which blobs have been transcribed and stores the transcripts.</param>
    /// <param name="decoder">Decodes the downloaded <c>.m4a</c> to the WAV format the provider expects.</param>
    /// <param name="transcriptionProvider">The active transcription provider (resolved per call).</param>
    /// <param name="promptResolver">Builds the LLM prompt context for the configured prompt mode.</param>
    /// <param name="llmProvider">Enhances the transcript with the active LLM provider (resolved per call).</param>
    /// <param name="historyRepository">Records each stored recording in the dictation history.</param>
    /// <param name="usageSink">Records the dictated word count so cloud recordings appear in the usage counters.</param>
    /// <param name="settingsService">Supplies the configured prompt mode, read per run.</param>
    /// <param name="timeProvider">Timestamps the stored transcriptions (replaceable in tests).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public CloudTranscriptionService(
        ICloudRecordingSource source,
        ICloudRecordingRepository repository,
        IAudioDecoder decoder,
        ITranscriptionProvider transcriptionProvider,
        IPromptResolver promptResolver,
        ILLMProvider llmProvider,
        IHistoryRepository historyRepository,
        IUsageSink usageSink,
        ISettingsService settingsService,
        TimeProvider timeProvider,
        ILogger<CloudTranscriptionService> logger)
    {
        _source = source;
        _repository = repository;
        _decoder = decoder;
        _transcriptionProvider = transcriptionProvider;
        _promptResolver = promptResolver;
        _llmProvider = llmProvider;
        _historyRepository = historyRepository;
        _usageSink = usageSink;
        _settingsService = settingsService;
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

            var rawTranscript = result.Text;
            var (finalText, modeName) = await EnhanceAsync(rawTranscript, cancellationToken).ConfigureAwait(false);
            var transcribedUtc = _timeProvider.GetUtcNow().UtcDateTime;

            await _repository.AddAsync(
                blob.Name,
                blob.LastModifiedUtc,
                transcribedUtc,
                finalText,
                result.AudioDurationSeconds,
                cancellationToken).ConfigureAwait(false);

            // Surface the recording alongside dictations: a history entry plus a Dictation usage
            // record (raw word count) so the counters include cloud recordings. Both are
            // best-effort — a bookkeeping failure must not fail the transcription.
            await RecordHistoryAndUsageAsync(finalText, rawTranscript, modeName, transcribedUtc, cancellationToken)
                .ConfigureAwait(false);

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

    /// <summary>
    /// Applies the configured prompt mode to <paramref name="rawTranscript"/> via the active LLM
    /// provider. Returns the raw transcript unchanged when no mode is configured, the mode has the
    /// LLM disabled, or enhancement fails (logged, never thrown) so a recording is never lost to an
    /// enhancement error. The second tuple item is the mode name actually applied, or
    /// <see langword="null"/> when the transcript was stored raw.
    /// </summary>
    private async Task<(string FinalText, string? ModeName)> EnhanceAsync(
        string rawTranscript, CancellationToken cancellationToken)
    {
        var modeName = _settingsService.Current.CloudRecordings.PromptMode?.Trim();
        if (string.IsNullOrEmpty(modeName) || string.IsNullOrWhiteSpace(rawTranscript))
        {
            return (rawTranscript, null);
        }

        try
        {
            var context = _promptResolver.Resolve(rawTranscript, modeName);
            if (!context.LlmEnabled)
            {
                return (rawTranscript, context.ModeName);
            }

            var enhanced = await _llmProvider.ProcessAsync(context, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug(
                "Enhanced cloud recording with mode '{ModeName}': {CharCount} characters", context.ModeName, enhanced.Length);
            return (enhanced, context.ModeName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not enhance a cloud recording with mode '{ModeName}'; storing the raw transcript", modeName);
            return (rawTranscript, null);
        }
    }

    /// <summary>
    /// Writes the recording to history and records its dictated word count in the usage metrics.
    /// Best-effort: any failure is logged and swallowed so bookkeeping never fails a transcription.
    /// </summary>
    private async Task RecordHistoryAndUsageAsync(
        string finalText, string rawTranscript, string? modeName, DateTime timestampUtc, CancellationToken cancellationToken)
    {
        try
        {
            await _historyRepository.AddAsync(timestampUtc, finalText, rawTranscript, modeName, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not write a cloud recording to history");
        }

        try
        {
            // Uses the raw transcript's word count so the figure reflects words spoken, mirroring
            // the dictation pipeline. The sink swallows its own failures.
            _usageSink.Record(new UsageRecord(
                timestampUtc,
                UsageCategories.Dictation,
                DurationSeconds: null,
                PromptTokens: null,
                CompletionTokens: null,
                WordCount: WordCounter.CountWords(rawTranscript)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not record cloud recording usage");
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
