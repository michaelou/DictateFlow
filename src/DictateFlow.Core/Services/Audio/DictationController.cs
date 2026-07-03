using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Pipeline;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Audio;

/// <summary>
/// Default <see cref="IDictationController"/> implementation. Subscribes to hotkey and
/// settings events, drives the recorder and overlay, auto-stops after
/// <see cref="RecordingSettings.SilenceTimeoutSeconds"/> of silence, and hands each
/// completed capture (plus the target-application context captured at record-start) to
/// <see cref="IDictationPipeline"/>.
/// </summary>
/// <remarks>
/// The silence timeout is evaluated on <see cref="IAudioRecorder.LevelChanged"/> events,
/// which the recorder raises continuously (per captured buffer) while recording — no
/// separate timer is needed. Levels at or above <see cref="SilenceThreshold"/> reset the
/// countdown. Time is measured through the injected <see cref="TimeProvider"/> so tests
/// can simulate silence without waiting.
/// </remarks>
public sealed class DictationController : IDictationController, IDisposable
{
    /// <summary>Peak levels below this value (0..1) count as silence.</summary>
    private const float SilenceThreshold = 0.02f;

    /// <summary>Size of the WAV header; captures at or below this size contain no audio.</summary>
    private const int WavHeaderBytes = 44;

    /// <summary>How long the Success overlay state stays visible before auto-hiding.</summary>
    private static readonly TimeSpan SuccessOverlayDuration = TimeSpan.FromSeconds(1.5);

    /// <summary>How long the Error overlay state stays visible before auto-hiding.</summary>
    private static readonly TimeSpan ErrorOverlayDuration = TimeSpan.FromSeconds(3);

    private readonly IAudioRecorder _recorder;
    private readonly IHotkeyService _hotkeyService;
    private readonly IRecordingOverlay _overlay;
    private readonly IDictationPipeline _pipeline;
    private readonly IForegroundAppService _foregroundAppService;
    private readonly ISettingsService _settingsService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DictationController> _logger;
    private readonly object _gate = new();

    private bool _isRecording;
    private long _lastLoudTimestamp;

    /// <summary>Initializes a new instance of the <see cref="DictationController"/> class.</summary>
    /// <param name="recorder">Captures microphone audio.</param>
    /// <param name="hotkeyService">Raises global hotkey events; re-armed when settings change.</param>
    /// <param name="overlay">The on-screen recording indicator.</param>
    /// <param name="pipeline">Runs each completed capture through transcription, enhancement, history and output.</param>
    /// <param name="foregroundAppService">Captures the target application at record-start.</param>
    /// <param name="settingsService">Supplies recording mode, hotkey and silence timeout.</param>
    /// <param name="timeProvider">Measures silence duration (replaceable in tests).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public DictationController(
        IAudioRecorder recorder,
        IHotkeyService hotkeyService,
        IRecordingOverlay overlay,
        IDictationPipeline pipeline,
        IForegroundAppService foregroundAppService,
        ISettingsService settingsService,
        TimeProvider timeProvider,
        ILogger<DictationController> logger)
    {
        _recorder = recorder;
        _hotkeyService = hotkeyService;
        _overlay = overlay;
        _pipeline = pipeline;
        _foregroundAppService = foregroundAppService;
        _settingsService = settingsService;
        _timeProvider = timeProvider;
        _logger = logger;

        _hotkeyService.TogglePressed += OnTogglePressed;
        _hotkeyService.PushToTalkPressed += OnPushToTalkPressed;
        _hotkeyService.PushToTalkReleased += OnPushToTalkReleased;
        _recorder.LevelChanged += OnLevelChanged;
        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    /// <inheritdoc />
    public bool IsRecording
    {
        get
        {
            lock (_gate)
            {
                return _isRecording;
            }
        }
    }

    /// <inheritdoc />
    public Stream? LastCapture { get; private set; }

    /// <inheritdoc />
    public event EventHandler<DictationFailedEventArgs>? DictationFailed;

    /// <inheritdoc />
    public async Task StartRecordingAsync()
    {
        lock (_gate)
        {
            if (_isRecording)
            {
                _logger.LogDebug("Start requested while already recording; ignored");
                return;
            }

            _isRecording = true;
            _lastLoudTimestamp = _timeProvider.GetTimestamp();
        }

        // Capture the target app before any DictateFlow UI can steal the foreground; the
        // pipeline uses the name for {{ApplicationName}} and the handle to re-focus for output.
        _foregroundAppService.Capture();

        try
        {
            await _recorder.StartAsync(CancellationToken.None).ConfigureAwait(false);
            _overlay.ShowListening();
            _logger.LogInformation("Recording started");
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _isRecording = false;
            }

            _overlay.Hide();
            _logger.LogError(ex, "Failed to start recording");
        }
    }

    /// <inheritdoc />
    public async Task StopRecordingAsync()
    {
        lock (_gate)
        {
            if (!_isRecording)
            {
                _logger.LogDebug("Stop requested while not recording; ignored");
                return;
            }

            _isRecording = false;
        }

        Stream capture;
        try
        {
            capture = await _recorder.StopAsync().ConfigureAwait(false);

            var previous = LastCapture;
            LastCapture = capture;
            previous?.Dispose();

            // 16 kHz × 16-bit × mono = 32,000 audio bytes per second after the 44-byte WAV header.
            var seconds = Math.Max(0, capture.Length - WavHeaderBytes) / 32000.0;
            _logger.LogDebug("Recording stopped: {ByteCount} bytes, ~{DurationSeconds:F1} s of audio", capture.Length, seconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop recording");
            _overlay.Hide();
            return;
        }

        if (capture.Length <= WavHeaderBytes)
        {
            _logger.LogDebug("Capture contains no audio; skipping the pipeline");
            _overlay.Hide();
            return;
        }

        await ProcessCaptureAsync(capture).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a completed capture through the dictation pipeline. The overlay stays in
    /// Processing until the pipeline returns, then shows Success (auto-hidden), hides on a
    /// user cancel, or shows Error (auto-hidden) and raises <see cref="DictationFailed"/>.
    /// </summary>
    private async Task ProcessCaptureAsync(Stream capture)
    {
        _overlay.ShowProcessing();

        var request = new PipelineRequest(
            capture,
            _foregroundAppService.LastCaptured,
            _foregroundAppService.LastCapturedWindowHandle);

        PipelineResult result;
        try
        {
            result = await _pipeline.RunAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The pipeline contract is to never throw; guard anyway so the app stays alive.
            // No raw exception text reaches the user — the details are in the log.
            _logger.LogError(ex, "Dictation pipeline threw unexpectedly");
            result = new PipelineResult(false, null, null, "Dictation failed unexpectedly — see the log for details.");
        }

        if (!result.Success)
        {
            var message = result.ErrorMessage ?? "Dictation failed.";
            _logger.LogWarning("Dictation failed: {Message}", message);
            // The overlay auto-hides quickly, so it only carries a short summary; the tray
            // notification raised below has room for the full actionable message.
            _overlay.ShowError(Shorten(message));
            DictationFailed?.Invoke(this, new DictationFailedEventArgs(message, result.IsConfigurationError));
            RunGuarded(() => HideOverlayAfterDelayAsync(ErrorOverlayDuration));
            return;
        }

        if (result.FinalText is null)
        {
            // The user cancelled in the preview dialog — not an error.
            _overlay.Hide();
            return;
        }

        _overlay.ShowSuccess();
        RunGuarded(() => HideOverlayAfterDelayAsync(SuccessOverlayDuration));
    }

    /// <summary>Hides the overlay after <paramref name="delay"/>, unless a new recording started.</summary>
    private async Task HideOverlayAfterDelayAsync(TimeSpan delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);

        lock (_gate)
        {
            if (_isRecording)
            {
                return; // A new session owns the overlay now.
            }
        }

        _overlay.Hide();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _hotkeyService.TogglePressed -= OnTogglePressed;
        _hotkeyService.PushToTalkPressed -= OnPushToTalkPressed;
        _hotkeyService.PushToTalkReleased -= OnPushToTalkReleased;
        _recorder.LevelChanged -= OnLevelChanged;
        _settingsService.SettingsChanged -= OnSettingsChanged;

        LastCapture?.Dispose();
        LastCapture = null;
    }

    private void OnTogglePressed(object? sender, EventArgs e)
        => RunGuarded(() => IsRecording ? StopRecordingAsync() : StartRecordingAsync());

    private void OnPushToTalkPressed(object? sender, EventArgs e)
        => RunGuarded(StartRecordingAsync);

    private void OnPushToTalkReleased(object? sender, EventArgs e)
        => RunGuarded(StopRecordingAsync);

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        try
        {
            _hotkeyService.Apply(settings.Recording);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply hotkey settings");
        }
    }

    private void OnLevelChanged(object? sender, float level)
    {
        _overlay.UpdateLevel(level);

        bool silenceTimedOut;
        lock (_gate)
        {
            if (!_isRecording)
            {
                return;
            }

            if (level >= SilenceThreshold)
            {
                _lastLoudTimestamp = _timeProvider.GetTimestamp();
                return;
            }

            var timeoutSeconds = _settingsService.Current.Recording.SilenceTimeoutSeconds;
            silenceTimedOut = timeoutSeconds > 0
                && _timeProvider.GetElapsedTime(_lastLoudTimestamp) >= TimeSpan.FromSeconds(timeoutSeconds);
        }

        if (silenceTimedOut)
        {
            _logger.LogInformation("Silence timeout reached; stopping recording");
            RunGuarded(StopRecordingAsync);
        }
    }

    /// <summary>Trims a failure message to a single short line that fits the overlay.</summary>
    private static string Shorten(string message)
    {
        const int maxLength = 90;
        var singleLine = message.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= maxLength ? singleLine : $"{singleLine[..maxLength].TrimEnd()}…";
    }

    /// <summary>Fires an async operation from a sync event handler, logging instead of throwing.</summary>
    private async void RunGuarded(Func<Task> operation)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dictation operation failed");
        }
    }
}
