using DictateFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Audio;

/// <summary>
/// Default <see cref="IDictationController"/> implementation. Subscribes to hotkey and
/// settings events, drives the recorder and overlay, and auto-stops after
/// <see cref="RecordingSettings.SilenceTimeoutSeconds"/> of silence.
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

    private readonly IAudioRecorder _recorder;
    private readonly IHotkeyService _hotkeyService;
    private readonly IRecordingOverlay _overlay;
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
    /// <param name="settingsService">Supplies recording mode, hotkey and silence timeout.</param>
    /// <param name="timeProvider">Measures silence duration (replaceable in tests).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public DictationController(
        IAudioRecorder recorder,
        IHotkeyService hotkeyService,
        IRecordingOverlay overlay,
        ISettingsService settingsService,
        TimeProvider timeProvider,
        ILogger<DictationController> logger)
    {
        _recorder = recorder;
        _hotkeyService = hotkeyService;
        _overlay = overlay;
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

        try
        {
            var capture = await _recorder.StopAsync().ConfigureAwait(false);

            var previous = LastCapture;
            LastCapture = capture;
            previous?.Dispose();

            // 16 kHz × 16-bit × mono = 32,000 audio bytes per second after the 44-byte WAV header.
            var seconds = Math.Max(0, capture.Length - 44) / 32000.0;
            _logger.LogDebug("Recording stopped: {ByteCount} bytes, ~{DurationSeconds:F1} s of audio", capture.Length, seconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop recording");
        }
        finally
        {
            _overlay.Hide();
        }
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
