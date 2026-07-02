using System.Diagnostics;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services.History;
using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Output;
using DictateFlow.Core.Services.Prompts;
using DictateFlow.Core.Services.Transcription;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Pipeline;

/// <summary>
/// Default <see cref="IDictationPipeline"/> implementation:
/// transcription → prompt resolution → LLM → output gate → history → output.
/// Depends only on Core abstractions — the concrete output providers and the preview
/// interaction are injected. Output settings are read on every run, so changes apply live.
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
    private readonly IPromptResolver _promptResolver;
    private readonly ILLMProvider _llmProvider;
    private readonly IHistoryRepository _historyRepository;
    private readonly IEnumerable<IOutputProvider> _outputProviders;
    private readonly IOutputGate _outputGate;
    private readonly ISettingsService _settingsService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DictationPipeline> _logger;

    /// <summary>Initializes a new instance of the <see cref="DictationPipeline"/> class.</summary>
    /// <param name="transcriptionProvider">Converts the capture into text.</param>
    /// <param name="promptResolver">Builds the LLM prompt context for the transcript.</param>
    /// <param name="llmProvider">Enhances the transcript.</param>
    /// <param name="historyRepository">Persists the confirmed final text.</param>
    /// <param name="outputProviders">All registered output providers; the active one is picked per run from settings.</param>
    /// <param name="outputGate">Confirms (and possibly edits) the draft before delivery.</param>
    /// <param name="settingsService">Supplies the active prompt mode and output provider name.</param>
    /// <param name="timeProvider">Supplies the history timestamp (replaceable in tests).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public DictationPipeline(
        ITranscriptionProvider transcriptionProvider,
        IPromptResolver promptResolver,
        ILLMProvider llmProvider,
        IHistoryRepository historyRepository,
        IEnumerable<IOutputProvider> outputProviders,
        IOutputGate outputGate,
        ISettingsService settingsService,
        TimeProvider timeProvider,
        ILogger<DictationPipeline> logger)
    {
        _transcriptionProvider = transcriptionProvider;
        _promptResolver = promptResolver;
        _llmProvider = llmProvider;
        _historyRepository = historyRepository;
        _outputProviders = outputProviders;
        _outputGate = outputGate;
        _settingsService = settingsService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PipelineResult> RunAsync(PipelineRequest request, CancellationToken cancellationToken)
    {
        // 1. Transcribe. A failure here fails the whole run — there is nothing to fall back to.
        string transcript;
        try
        {
            transcript = await TranscribeAsync(request.Audio, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var failure = ex as ProviderException
                ?? new ProviderException("Transcription", $"Transcription failed unexpectedly: {ex.Message}", ex);
            _logger.LogError(ex, "Pipeline failed at transcription ({ProviderName})", failure.ProviderName);
            return new PipelineResult(false, null, null, PresentableMessage(failure));
        }

        // 2. Enhance. A failure degrades to the raw transcript with a warning for the gate.
        var (finalText, warning) = await EnhanceAsync(transcript, cancellationToken).ConfigureAwait(false);

        // 3. Gate: pass-through in Automatic mode, preview dialog in Preview mode.
        string? confirmedText;
        try
        {
            confirmedText = await _outputGate.ConfirmAsync(
                new PipelineResult(true, finalText, transcript, warning)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline failed at the output gate");
            return new PipelineResult(false, finalText, transcript, $"Output confirmation failed: {ex.Message}");
        }

        if (confirmedText is null)
        {
            _logger.LogInformation("Dictation cancelled at the output gate; nothing written or delivered");
            return new PipelineResult(true, null, transcript, null);
        }

        // 4. History — written only for text that is actually being delivered (see class remarks),
        //    and before delivery so a paste failure cannot lose the confirmed text.
        try
        {
            var stopwatch = Stopwatch.StartNew();
            await _historyRepository.AddAsync(_timeProvider.GetUtcNow().UtcDateTime, confirmedText, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogDebug("History step completed in {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // History is best-effort: never fail a dictation over a bookkeeping write.
            _logger.LogError(ex, "History write failed; continuing with output");
        }

        // 5. Output through the provider selected in settings (read per run — applied live).
        try
        {
            var provider = SelectOutputProvider();
            var stopwatch = Stopwatch.StartNew();
            await provider.OutputAsync(confirmedText).ConfigureAwait(false);
            _logger.LogDebug(
                "Output step ({ProviderName}) completed in {ElapsedMs} ms", provider.Name, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline failed at output delivery");
            var message = ex is ProviderException failure ? PresentableMessage(failure) : $"Output failed: {ex.Message}";
            return new PipelineResult(false, confirmedText, transcript, message);
        }

        return new PipelineResult(true, confirmedText, transcript, null);
    }

    /// <summary>Runs the transcription step with latency logging.</summary>
    private async Task<string> TranscribeAsync(Stream audio, CancellationToken cancellationToken)
    {
        if (audio.CanSeek)
        {
            audio.Position = 0;
        }

        var stopwatch = Stopwatch.StartNew();
        var transcription = await _transcriptionProvider.TranscribeAsync(audio, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug(
            "Transcription step completed in {ElapsedMs} ms: {CharCount} characters from ~{DurationSeconds:F1} s of audio",
            stopwatch.ElapsedMilliseconds, transcription.Text.Length, transcription.AudioDurationSeconds);
        return transcription.Text;
    }

    /// <summary>
    /// Runs prompt resolution and LLM enhancement. Never throws: a failure returns the raw
    /// transcript with a user-presentable warning, so the dictation is not lost.
    /// </summary>
    private async Task<(string FinalText, string? Warning)> EnhanceAsync(string transcript, CancellationToken cancellationToken)
    {
        var modeName = _settingsService.Current.ActivePromptMode;
        try
        {
            var context = _promptResolver.Resolve(transcript, modeName);
            var stopwatch = Stopwatch.StartNew();
            var enhanced = await _llmProvider.ProcessAsync(context, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug(
                "Enhancement step completed in {ElapsedMs} ms with mode '{ModeName}': {CharCount} characters",
                stopwatch.ElapsedMilliseconds, context.ModeName, enhanced.Length);
            return (enhanced, null);
        }
        catch (Exception ex)
        {
            var failure = ex as ProviderException
                ?? new ProviderException("LLM", $"Enhancement failed unexpectedly: {ex.Message}", ex);
            _logger.LogError(ex, "Enhancement failed ({ProviderName}); falling back to the raw transcript", failure.ProviderName);
            return (transcript, $"AI enhancement failed — using the raw transcript. {failure.Message}");
        }
    }

    /// <summary>
    /// Picks the output provider named in settings, falling back to the first registered
    /// provider (with a warning) when the configured name is unknown.
    /// </summary>
    private IOutputProvider SelectOutputProvider()
    {
        var configuredName = _settingsService.Current.Output.Provider;
        var provider = _outputProviders.FirstOrDefault(
            p => string.Equals(p.Name, configuredName, StringComparison.OrdinalIgnoreCase));
        if (provider is not null)
        {
            return provider;
        }

        provider = _outputProviders.FirstOrDefault()
            ?? throw new InvalidOperationException("No output providers are registered.");
        _logger.LogWarning(
            "Unknown output provider '{ConfiguredName}' in settings; falling back to '{FallbackName}'",
            configuredName, provider.Name);
        return provider;
    }

    /// <summary>Formats a provider failure for the user, pointing to Settings when configuration is at fault.</summary>
    private static string PresentableMessage(ProviderException failure)
        => failure.IsConfigurationError
            ? $"{failure.Message} Check your DictateFlow settings."
            : failure.Message;
}
