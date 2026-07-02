using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Transcription;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Audio;

/// <summary>
/// Default <see cref="IDictationController"/> implementation. Subscribes to hotkey and
/// settings events, drives the recorder and overlay, auto-stops after
/// <see cref="RecordingSettings.SilenceTimeoutSeconds"/> of silence, and transcribes each
/// completed capture through <see cref="ITranscriptionProvider"/>.
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

    /// <summary>How long the Error overlay state stays visible before auto-hiding.</summary>
    private static readonly TimeSpan ErrorOverlayDuration = TimeSpan.FromSeconds(2.5);

    private readonly IAudioRecorder _recorder;
    private readonly IHotkeyService _hotkeyService;
    private readonly IRecordingOverlay _overlay;
    private readonly ITranscriptionProvider _transcriptionProvider;
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
    /// <param name="transcriptionProvider">Converts completed captures into text.</param>
    /// <param name="settingsService">Supplies recording mode, hotkey and silence timeout.</param>
    /// <param name="timeProvider">Measures silence duration (replaceable in tests).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public DictationController(
        IAudioRecorder recorder,
        IHotkeyService hotkeyService,
        IRecordingOverlay overlay,
        ITranscriptionProvider transcriptionProvider,
        ISettingsService settingsService,
        TimeProvider timeProvider,
        ILogger<DictationController> logger)
    {
        _recorder = recorder;
        _hotkeyService = hotkeyService;
        _overlay = overlay;
        _transcriptionProvider = transcriptionProvider;
        _settingsService = settingsService;
        _timeProvider = timeProvider;
        _logger = logger;

        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
        _hotkeyService.HotkeyReleased += OnHotkeyReleased;
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
    public event EventHandler<TranscriptionResult>? TranscriptionCompleted;

    /// <inheritdoc />
    public event EventHandler<ProviderException>? TranscriptionFailed;

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
            _logger.LogDebug("Capture contains no audio; skipping transcription");
            _overlay.Hide();
            return;
        }

        await TranscribeAsync(capture).ConfigureAwait(false);
    }

    /// <summary>
    /// Transcribes a completed capture: shows the Processing overlay, raises
    /// <see cref="TranscriptionCompleted"/> on success, and on failure raises
    /// <see cref="TranscriptionFailed"/> and briefly shows the Error overlay.
    /// </summary>
    private async Task TranscribeAsync(Stream capture)
    {
        _overlay.ShowProcessing();
        try
        {
            capture.Position = 0;
            var result = await _transcriptionProvider.TranscribeAsync(capture, CancellationToken.None).ConfigureAwait(false);

            _overlay.Hide();
            _logger.LogInformation(
                "Transcription completed: {CharCount} characters from ~{DurationSeconds:F1} s of audio",
                result.Text.Length, result.AudioDurationSeconds);
            TranscriptionCompleted?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            var failure = ex as ProviderException
                ?? new ProviderException("Transcription", $"Transcription failed unexpectedly: {ex.Message}", ex);
            _logger.LogError(ex, "Transcription failed ({ProviderName})", failure.ProviderName);

            _overlay.ShowError();
            TranscriptionFailed?.Invoke(this, failure);
            RunGuarded(HideErrorOverlayAfterDelayAsync);
        }
    }

    /// <summary>Hides the Error overlay after a short delay, unless a new recording started.</summary>
    private async Task HideErrorOverlayAfterDelayAsync()
    {
        await Task.Delay(ErrorOverlayDuration).ConfigureAwait(false);

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
        _hotkeyService.HotkeyPressed -= OnHotkeyPressed;
        _hotkeyService.HotkeyReleased -= OnHotkeyReleased;
        _recorder.LevelChanged -= OnLevelChanged;
        _settingsService.SettingsChanged -= OnSettingsChanged;

        LastCapture?.Dispose();
        LastCapture = null;
    }

    private bool IsPushToTalk
        => !string.Equals(_settingsService.Current.Recording.Mode, RecordingModes.Toggle, StringComparison.OrdinalIgnoreCase);

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        if (IsPushToTalk)
        {
            RunGuarded(StartRecordingAsync);
        }
        else
        {
            RunGuarded(() => IsRecording ? StopRecordingAsync() : StartRecordingAsync());
        }
    }

    private void OnHotkeyReleased(object? sender, EventArgs e)
    {
        if (IsPushToTalk)
        {
            RunGuarded(StopRecordingAsync);
        }
    }

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
