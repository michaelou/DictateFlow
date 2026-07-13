using System.Diagnostics;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Commands;
using DictateFlow.Core.Services.History;
using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Output;
using DictateFlow.Core.Services.Prompts;
using DictateFlow.Core.Services.Replacements;
using DictateFlow.Core.Services.Transcription;
using DictateFlow.Core.Services.Usage;
using DictateFlow.Core.Text;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Pipeline;

/// <summary>
/// Default <see cref="IDictationPipeline"/> implementation:
/// transcription → voice command detection → prompt-mode selection → prompt resolution →
/// LLM → output gate → history → output. Depends only on Core abstractions — the injected
/// providers are the registry-backed defaults, which resolve the active provider from
/// settings on every call, so changes apply live and the pipeline itself carries no provider
/// knowledge. Every run ends with one Debug summary line carrying the per-stage latencies.
/// An utterance recognized as a voice command (issue #26) branches off right after
/// transcription: the command executes and the run ends — no LLM enhancement, no history
/// entry, no paste.
/// </summary>
/// <remarks>
/// Failure policy: a transcription failure fails the run; an LLM failure degrades to the raw
/// transcript, offered through the gate with a warning, so a dictation is never lost to an
/// enhancement failure. History is written only when the gate confirms text for delivery —
/// a cancelled (or copy-only) preview leaves no history entry. The write happens just before
/// output delivery, so a paste failure cannot lose the confirmed text.
/// </remarks>
public sealed class DictationPipeline : IDictationPipeline
{
    private readonly ITranscriptionProvider _transcriptionProvider;
    private readonly IVoiceCommandService _voiceCommandService;
    private readonly ITextReplacementService _textReplacementService;
    private readonly IPromptModeSelector _promptModeSelector;
    private readonly IPromptResolver _promptResolver;
    private readonly ILLMProvider _llmProvider;
    private readonly IHistoryRepository _historyRepository;
    private readonly IOutputProvider _outputProvider;
    private readonly IOutputGate _outputGate;
    private readonly IUsageSink _usageSink;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DictationPipeline> _logger;

    /// <summary>Initializes a new instance of the <see cref="DictationPipeline"/> class.</summary>
    /// <param name="transcriptionProvider">Converts the capture into text.</param>
    /// <param name="voiceCommandService">Handles the transcript as a voice command when it is one.</param>
    /// <param name="textReplacementService">Applies the replacement dictionary to the transcript (issue #35).</param>
    /// <param name="promptModeSelector">Picks the prompt mode from the application rules (or the active mode).</param>
    /// <param name="promptResolver">Builds the LLM prompt context for the transcript.</param>
    /// <param name="llmProvider">Enhances the transcript.</param>
    /// <param name="historyRepository">Persists the confirmed final text.</param>
    /// <param name="outputProvider">Delivers the confirmed text.</param>
    /// <param name="outputGate">Confirms (and possibly edits) the draft before delivery.</param>
    /// <param name="usageSink">Records the count of dictated words per delivered dictation.</param>
    /// <param name="timeProvider">Supplies the history timestamp (replaceable in tests).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public DictationPipeline(
        ITranscriptionProvider transcriptionProvider,
        IVoiceCommandService voiceCommandService,
        ITextReplacementService textReplacementService,
        IPromptModeSelector promptModeSelector,
        IPromptResolver promptResolver,
        ILLMProvider llmProvider,
        IHistoryRepository historyRepository,
        IOutputProvider outputProvider,
        IOutputGate outputGate,
        IUsageSink usageSink,
        TimeProvider timeProvider,
        ILogger<DictationPipeline> logger)
    {
        _transcriptionProvider = transcriptionProvider;
        _voiceCommandService = voiceCommandService;
        _textReplacementService = textReplacementService;
        _promptModeSelector = promptModeSelector;
        _promptResolver = promptResolver;
        _llmProvider = llmProvider;
        _historyRepository = historyRepository;
        _outputProvider = outputProvider;
        _outputGate = outputGate;
        _usageSink = usageSink;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PipelineResult> RunAsync(PipelineRequest request, CancellationToken cancellationToken)
    {
        var timings = new StageTimings();
        var totalStopwatch = Stopwatch.StartNew();
        try
        {
            return await RunStagesAsync(request, timings, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            timings.LogSummary(_logger, totalStopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>Runs the pipeline stages; split out so the latency summary logs on every exit path.</summary>
    private async Task<PipelineResult> RunStagesAsync(
        PipelineRequest request, StageTimings timings, CancellationToken cancellationToken)
    {
        // 1. Transcribe. A failure here fails the whole run — there is nothing to fall back to.
        //    A request carrying a streamed transcript skips the stage: the text was already
        //    produced while recording.
        string transcript;
        if (request.Transcript is not null)
        {
            transcript = request.Transcript;
            _logger.LogDebug(
                "Transcription step skipped: streamed transcript supplied ({CharCount} characters)", transcript.Length);
        }
        else
        {
            try
            {
                transcript = await TranscribeAsync(request.Audio, timings, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Providers curate their messages; anything else stays out of user-facing text.
                var failure = ex as ProviderException
                    ?? new ProviderException("Transcription", "Transcription failed unexpectedly — see the log for details.", ex);
                _logger.LogError(ex, "Pipeline failed at transcription ({ProviderName})", failure.ProviderName);
                return new PipelineResult(false, null, null, PresentableMessage(failure), failure.IsConfigurationError);
            }
        }

        // 2. Voice command branch (issue #26): runs on the raw transcript, before any LLM
        //    involvement, so commands stay deterministic. A handled command ends the run —
        //    nothing is enhanced, written to history, or pasted. Not-a-command (null) falls
        //    through to normal dictation unchanged.
        var commandStopwatch = Stopwatch.StartNew();
        var commandOutcome = await _voiceCommandService.TryHandleAsync(transcript, cancellationToken)
            .ConfigureAwait(false);
        if (commandOutcome is not null)
        {
            timings.CommandMs = commandStopwatch.ElapsedMilliseconds;
            _logger.LogInformation(
                "Voice command handled ({Status}) in {ElapsedMs} ms: {Message}",
                commandOutcome.Status, timings.CommandMs, commandOutcome.Message);
            // Executed and Declined are successful ends of the run; Failed and Unknown
            // surface the outcome message the way any pipeline failure does.
            var isCommandSuccess = commandOutcome.Status
                is CommandOutcomeStatus.Executed or CommandOutcomeStatus.Declined;
            return new PipelineResult(
                isCommandSuccess, null, transcript,
                isCommandSuccess ? null : commandOutcome.Message,
                Command: commandOutcome);
        }

        // 2.5 Replacement dictionary (issue #35): fix speech-to-text mishearings deterministically,
        //     after command detection (so wake/command phrases match the raw transcript) and before
        //     enhancement (so the LLM sees, and history records, the corrected text). Corrections
        //     therefore stick whether or not enhancement runs.
        var corrected = _textReplacementService.Apply(transcript);
        if (!ReferenceEquals(corrected, transcript) && corrected != transcript)
        {
            _logger.LogDebug("Replacement dictionary changed the transcript before enhancement");
            transcript = corrected;
        }

        // 3. Enhance. A failure degrades to the raw transcript with a warning for the gate.
        var (finalText, warning) = await EnhanceAsync(transcript, request.ApplicationName, timings, cancellationToken)
            .ConfigureAwait(false);

        // 4. Gate: pass-through in Automatic mode, preview dialog in Preview mode.
        string? confirmedText;
        var gateStopwatch = Stopwatch.StartNew();
        try
        {
            confirmedText = await _outputGate.ConfirmAsync(
                new PipelineResult(true, finalText, transcript, warning)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline failed at the output gate");
            return new PipelineResult(false, finalText, transcript, "Output confirmation failed — see the log for details.");
        }
        finally
        {
            timings.GateMs = gateStopwatch.ElapsedMilliseconds;
        }

        if (confirmedText is null)
        {
            _logger.LogInformation("Dictation cancelled at the output gate; nothing written or delivered");
            return new PipelineResult(true, null, transcript, null);
        }

        // 5. History — written only for text that is actually being delivered (see class remarks),
        //    and before delivery so a paste failure cannot lose the confirmed text.
        try
        {
            var stopwatch = Stopwatch.StartNew();
            await _historyRepository.AddAsync(
                    _timeProvider.GetUtcNow().UtcDateTime, confirmedText, transcript, timings.ModeName, cancellationToken)
                .ConfigureAwait(false);
            timings.HistoryMs = stopwatch.ElapsedMilliseconds;
            _logger.LogDebug("History step completed in {ElapsedMs} ms", timings.HistoryMs);
        }
        catch (Exception ex)
        {
            // History is best-effort: never fail a dictation over a bookkeeping write.
            _logger.LogError(ex, "History write failed; continuing with output");
        }

        // 5.5 Usage — record the raw dictated word count for the metrics, only for text actually
        //     delivered. Uses the corrected raw transcript (not the enhanced output) so the figure
        //     reflects words spoken. The sink swallows its own failures, so this never fails a run.
        _usageSink.Record(new UsageRecord(
            _timeProvider.GetUtcNow().UtcDateTime,
            UsageCategories.Dictation,
            DurationSeconds: null,
            PromptTokens: null,
            CompletionTokens: null,
            WordCount: WordCounter.CountWords(transcript)));

        // 6. Output. The injected provider is the registry-backed default, which selects the
        //    active provider from settings per call — changes apply live.
        try
        {
            var stopwatch = Stopwatch.StartNew();
            await _outputProvider.OutputAsync(confirmedText).ConfigureAwait(false);
            timings.OutputMs = stopwatch.ElapsedMilliseconds;
            _logger.LogDebug(
                "Output step ({ProviderName}) completed in {ElapsedMs} ms", _outputProvider.Name, timings.OutputMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline failed at output delivery");
            var isConfigurationError = ex is ProviderException { IsConfigurationError: true };
            var message = ex is ProviderException failure
                ? PresentableMessage(failure)
                : "Output failed — see the log for details.";
            return new PipelineResult(false, confirmedText, transcript, message, isConfigurationError);
        }

        return new PipelineResult(true, confirmedText, transcript, null);
    }

    /// <summary>Runs the transcription step with latency logging.</summary>
    private async Task<string> TranscribeAsync(Stream audio, StageTimings timings, CancellationToken cancellationToken)
    {
        if (audio.CanSeek)
        {
            audio.Position = 0;
        }

        var stopwatch = Stopwatch.StartNew();
        var transcription = await _transcriptionProvider.TranscribeAsync(audio, cancellationToken).ConfigureAwait(false);
        timings.TranscriptionMs = stopwatch.ElapsedMilliseconds;
        timings.AudioSeconds = transcription.AudioDurationSeconds;
        _logger.LogDebug(
            "Transcription step completed in {ElapsedMs} ms: {CharCount} characters from ~{DurationSeconds:F1} s of audio",
            timings.TranscriptionMs, transcription.Text.Length, transcription.AudioDurationSeconds);
        return transcription.Text;
    }

    /// <summary>
    /// Runs prompt-mode selection, prompt resolution and LLM enhancement. Never throws: a
    /// failure returns the raw transcript with a user-presentable warning, so the dictation
    /// is not lost.
    /// </summary>
    private async Task<(string FinalText, string? Warning)> EnhanceAsync(
        string transcript, string applicationName, StageTimings timings, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var modeName = _promptModeSelector.SelectMode(applicationName);
            timings.ModeName = modeName;
            var context = _promptResolver.Resolve(transcript, modeName);
            if (!context.LlmEnabled)
            {
                timings.EnhancementMs = stopwatch.ElapsedMilliseconds;
                _logger.LogInformation(
                    "LLM disabled for mode '{ModeName}'; delivering the raw transcript", context.ModeName);
                return (transcript, null);
            }

            var enhanced = await _llmProvider.ProcessAsync(context, cancellationToken).ConfigureAwait(false);
            timings.EnhancementMs = stopwatch.ElapsedMilliseconds;
            _logger.LogDebug(
                "Enhancement step completed in {ElapsedMs} ms with mode '{ModeName}': {CharCount} characters",
                timings.EnhancementMs, context.ModeName, enhanced.Length);
            return (enhanced, null);
        }
        catch (Exception ex)
        {
            timings.EnhancementMs = stopwatch.ElapsedMilliseconds;
            _logger.LogError(ex, "Enhancement failed; falling back to the raw transcript");
            // Only curated provider messages reach the user; unexpected details stay in the log.
            var detail = ex is ProviderException failure ? $" {failure.Message}" : "";
            return (transcript, $"AI enhancement failed — using the raw transcript.{detail}");
        }
    }

    /// <summary>Formats a provider failure for the user, pointing to Settings when configuration is at fault.</summary>
    private static string PresentableMessage(ProviderException failure)
        => failure.IsConfigurationError
            ? $"{failure.Message} Check your DictateFlow settings."
            : failure.Message;

    /// <summary>Per-stage latencies of one run, emitted as a single Debug summary line.</summary>
    private sealed class StageTimings
    {
        public long? TranscriptionMs { get; set; }
        public double? AudioSeconds { get; set; }
        public long? CommandMs { get; set; }
        public long? EnhancementMs { get; set; }
        public long? GateMs { get; set; }
        public long? HistoryMs { get; set; }
        public long? OutputMs { get; set; }
        public string? ModeName { get; set; }

        /// <summary>Writes the one-line latency summary; stages that did not run show as null.</summary>
        public void LogSummary(ILogger logger, long totalMs)
            => logger.LogDebug(
                "Dictation latency summary: total {TotalMs} ms (audio ~{AudioSeconds:F1} s, transcription {TranscriptionMs} ms, "
                + "command {CommandMs} ms, enhancement {EnhancementMs} ms, gate {GateMs} ms, history {HistoryMs} ms, "
                + "output {OutputMs} ms), mode '{ModeName}'",
                totalMs, AudioSeconds, TranscriptionMs, CommandMs, EnhancementMs, GateMs, HistoryMs, OutputMs, ModeName);
    }
}
