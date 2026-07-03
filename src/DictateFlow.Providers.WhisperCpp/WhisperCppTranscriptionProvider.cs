using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Core.Services.Transcription;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Providers.WhisperCpp;

/// <summary>
/// <see cref="ITranscriptionProvider"/> backed by a local
/// <see href="https://github.com/ggml-org/whisper.cpp">whisper.cpp</see> installation —
/// fully offline transcription. Each call writes the WAV to a temporary file, runs the
/// installed <c>whisper-cli.exe</c> with JSON output, and parses the transcript. Reads its
/// <see cref="WhisperCppTranscriptionConfig"/> section on every call, maps all failures to
/// <see cref="ProviderException"/>, and kills the whisper.cpp process immediately on
/// cancellation or timeout.
/// </summary>
public sealed class WhisperCppTranscriptionProvider : ITranscriptionProvider
{
    private const string ProviderName = WhisperCppProviders.RegistrationName;

    /// <summary>Size of the WAV header; subtracted when computing duration from byte length.</summary>
    private const int WavHeaderBytes = 44;

    /// <summary>Bytes of PCM data per second for 16 kHz × 16-bit × mono audio.</summary>
    private const int BytesPerSecond = 16000 * 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly WhisperCppModelManager _modelManager;
    private readonly IProviderConfigReader _configReader;
    private readonly IUsageSink _usageSink;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WhisperCppTranscriptionProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="WhisperCppTranscriptionProvider"/> class.</summary>
    /// <param name="modelManager">Locates the installed engine executable and model files.</param>
    /// <param name="configReader">Supplies the model, language, threads and timeout, read per call.</param>
    /// <param name="usageSink">Receives the audio duration after each successful call.</param>
    /// <param name="timeProvider">Timestamps usage records (replaceable in tests).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public WhisperCppTranscriptionProvider(
        WhisperCppModelManager modelManager,
        IProviderConfigReader configReader,
        IUsageSink usageSink,
        TimeProvider timeProvider,
        ILogger<WhisperCppTranscriptionProvider> logger)
    {
        _modelManager = modelManager;
        _configReader = configReader;
        _usageSink = usageSink;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TranscriptionResult> TranscribeAsync(Stream audio, CancellationToken cancellationToken)
    {
        var config = _configReader.GetConfig<WhisperCppTranscriptionConfig>(ProviderKind.Transcription, ProviderName);
        var (executablePath, modelPath, model) = ResolveInstallation(config);

        // The user-facing timeout: enforced here (instead of at process start) so a changed
        // TimeoutSeconds takes effect immediately, without a restart.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (config.TimeoutSeconds > 0)
        {
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));
        }

        var tempBase = Path.Combine(Path.GetTempPath(), $"dictateflow-whisper-{Guid.NewGuid():N}");
        var wavPath = tempBase + ".wav";
        var jsonPath = tempBase + ".json";
        try
        {
            var audioBytes = await WriteAudioFileAsync(audio, wavPath, timeoutCts.Token).ConfigureAwait(false);
            var duration = Math.Max(0, audioBytes - WavHeaderBytes) / (double)BytesPerSecond;

            var stopwatch = Stopwatch.StartNew();
            var (exitCode, errorOutput) = await RunWhisperAsync(
                executablePath, modelPath, wavPath, tempBase, config, timeoutCts.Token).ConfigureAwait(false);
            stopwatch.Stop();
            _logger.LogDebug(
                "whisper.cpp {EngineVersion} finished: model {Model}, {DurationSeconds:F1} s of audio, exit code {ExitCode} in {ElapsedMs} ms",
                _modelManager.GetInstalledEngineVersion(), model.Id, duration, exitCode, stopwatch.ElapsedMilliseconds);

            if (exitCode != 0)
            {
                _logger.LogWarning("whisper.cpp failed with exit code {ExitCode}: {ErrorOutput}", exitCode, errorOutput);
                throw new ProviderException(
                    ProviderName,
                    $"Local transcription failed (whisper.cpp exit code {exitCode}){FormatDetail(errorOutput)}. If it persists, verify the engine and model in Settings → Local Models.");
            }

            var (text, language) = ParseOutput(jsonPath);

            _usageSink.Record(new UsageRecord(
                _timeProvider.GetUtcNow().UtcDateTime,
                UsageCategories.Speech,
                duration,
                PromptTokens: null,
                CompletionTokens: null));

            _logger.LogDebug("Transcript received: {CharCount} characters, {DurationSeconds:F1} s of audio", text.Length, duration);
            return new TranscriptionResult(text, duration, language);
        }
        catch (ProviderException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Caller-initiated cancellation is not a provider failure.
        }
        catch (OperationCanceledException ex)
        {
            throw new ProviderException(
                ProviderName,
                $"Local transcription timed out after {config.TimeoutSeconds} s. Try a smaller model, or raise the timeout in Settings → Speech.",
                ex);
        }
        catch (Exception ex)
        {
            // Defensive: nothing rawer than ProviderException may escape the provider.
            throw new ProviderException(ProviderName, $"Local transcription failed: {ex.Message}", ex);
        }
        finally
        {
            TryDelete(wavPath);
            TryDelete(jsonPath);
        }
    }

    /// <summary>
    /// Maps the configured language to the whisper.cpp <c>-l</c> argument: the primary
    /// subtag of the first configured BCP-47 tag (whisper models know languages, not
    /// regions), or <c>auto</c> when empty so mixed-language dictation keeps working.
    /// </summary>
    /// <param name="configuredLanguage">The configured language setting.</param>
    public static string MapLanguage(string configuredLanguage)
    {
        var first = configuredLanguage
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrEmpty(first))
        {
            return "auto";
        }

        var primary = first.Split('-')[0].ToLowerInvariant();
        return primary.Length == 0 ? "auto" : primary;
    }

    /// <summary>
    /// Resolves the installed executable and model paths, or throws the user-actionable
    /// "not installed" error that points at the Local Models settings page.
    /// </summary>
    private (string ExecutablePath, string ModelPath, ModelDefinition Model) ResolveInstallation(
        WhisperCppTranscriptionConfig config)
    {
        var model = WhisperCppModelCatalog.FindModel(config.Model);
        if (model is null)
        {
            throw new ProviderException(
                ProviderName,
                $"'{config.Model}' is not a known Whisper model. Pick a model in Settings → Speech.",
                isConfigurationError: true);
        }

        var executablePath = _modelManager.GetEngineExecutablePath();
        if (executablePath is null || !_modelManager.IsInstalled(model))
        {
            throw new ProviderException(
                ProviderName,
                $"Local transcription is not installed. Download the Whisper.cpp engine and the {model.DisplayName} model in Settings → Local Models.",
                isConfigurationError: true);
        }

        return (executablePath, _modelManager.GetModelPath(model), model);
    }

    /// <summary>Writes the audio stream (from its current position) to the temporary WAV file.</summary>
    /// <returns>The number of bytes written.</returns>
    private static async Task<long> WriteAudioFileAsync(Stream audio, string wavPath, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(
            wavPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1 << 16, useAsync: true);
        await audio.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        return file.Length;
    }

    /// <summary>
    /// Runs <c>whisper-cli.exe</c> asynchronously with JSON output and no console transcript,
    /// capturing stderr for diagnostics. Cancellation (user or timeout) kills the whole
    /// process tree immediately.
    /// </summary>
    private async Task<(int ExitCode, string ErrorOutput)> RunWhisperAsync(
        string executablePath,
        string modelPath,
        string wavPath,
        string outputBase,
        WhisperCppTranscriptionConfig config,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            WorkingDirectory = Path.GetDirectoryName(executablePath),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add(modelPath);
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(wavPath);
        startInfo.ArgumentList.Add("-l");
        startInfo.ArgumentList.Add(MapLanguage(config.Language));
        startInfo.ArgumentList.Add("-oj");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add(outputBase);
        startInfo.ArgumentList.Add("-np");
        if (config.Threads > 0)
        {
            startInfo.ArgumentList.Add("-t");
            startInfo.ArgumentList.Add(config.Threads.ToString());
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new ProviderException(ProviderName, "The whisper.cpp process could not be started.");
        }

        // Drain both pipes while waiting so a chatty run can never fill a buffer and deadlock.
        var standardOutputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var standardErrorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process exited between the cancellation and the kill.
            }

            throw;
        }

        await standardOutputTask.ConfigureAwait(false);
        var errorOutput = await standardErrorTask.ConfigureAwait(false);
        return (process.ExitCode, errorOutput);
    }

    /// <summary>Parses the whisper.cpp JSON output file into the transcript text and detected language.</summary>
    private (string Text, string? Language) ParseOutput(string jsonPath)
    {
        if (!File.Exists(jsonPath))
        {
            throw new ProviderException(ProviderName, "whisper.cpp did not produce a transcript file.");
        }

        WhisperJsonOutput? output;
        try
        {
            output = JsonSerializer.Deserialize<WhisperJsonOutput>(File.ReadAllText(jsonPath), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ProviderException(ProviderName, "whisper.cpp returned an unreadable transcript file.", ex);
        }

        // An empty segment list (e.g. silence) is valid and must not fail — the
        // "Test connection" check relies on that.
        if (output?.Transcription is null)
        {
            throw new ProviderException(ProviderName, "The whisper.cpp output did not contain a transcript.");
        }

        var text = string.Join(
            " ",
            output.Transcription
                .Select(s => s.Text?.Trim())
                .Where(t => !string.IsNullOrEmpty(t)));
        return (text, output.Result?.Language);
    }

    /// <summary>Formats the stderr tail for inline use in an exception message; empty when blank.</summary>
    private static string FormatDetail(string errorOutput)
    {
        var detail = errorOutput.Trim();
        if (detail.Length == 0)
        {
            return "";
        }

        // The last lines carry the actual error; earlier ones are the model-load banner.
        var lastLine = detail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1];
        return lastLine.Length <= 300 ? $" — {lastLine}" : $" — {lastLine[..300]}…";
    }

    /// <summary>Best-effort removal of a temporary file.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Leftovers in %TEMP% are harmless and cleaned by the OS.
        }
    }

    /// <summary>Wire shape of the whisper.cpp <c>-oj</c> output file; unknown fields are ignored.</summary>
    private sealed record WhisperJsonOutput(
        [property: JsonPropertyName("result")] WhisperResult? Result,
        [property: JsonPropertyName("transcription")] IReadOnlyList<WhisperSegment>? Transcription);

    /// <summary>The run-level result; only the detected language is consumed.</summary>
    private sealed record WhisperResult(
        [property: JsonPropertyName("language")] string? Language);

    /// <summary>One transcribed segment; only its text is consumed.</summary>
    private sealed record WhisperSegment(
        [property: JsonPropertyName("text")] string? Text);
}
