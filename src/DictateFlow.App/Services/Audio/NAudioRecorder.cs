using System.IO;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using Microsoft.Extensions.Logging;
using NAudio.Utils;
using NAudio.Wave;

namespace DictateFlow.App.Services.Audio;

/// <summary>
/// NAudio-based <see cref="IAudioRecorder"/>: captures 16 kHz / 16-bit / mono audio
/// (the format speech APIs expect) from the configured device into an in-memory WAV.
/// </summary>
/// <remarks>
/// The capture device is resolved whenever settings change (not on each start) so that
/// starting a recording stays fast. <see cref="StopAsync"/> without a matching
/// <see cref="StartAsync"/> throws <see cref="InvalidOperationException"/> — callers
/// (the dictation controller) are expected to guard recording state themselves.
/// <c>DataAvailable</c> fires on an NAudio worker thread; all mutable state is guarded
/// by a lock and UI concerns are left to event subscribers.
/// </remarks>
public sealed class NAudioRecorder : IAudioRecorder, IDisposable
{
    /// <summary><see cref="WaveInEvent.DeviceNumber"/> value selecting the default device (WAVE_MAPPER).</summary>
    private const int DefaultDevice = -1;

    private static readonly WaveFormat CaptureFormat = new(16000, 16, 1);

    private readonly ISettingsService _settingsService;
    private readonly ILogger<NAudioRecorder> _logger;
    private readonly object _gate = new();

    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private MemoryStream? _buffer;
    private TaskCompletionSource? _stopped;
    private int _deviceNumber = DefaultDevice;
    private bool _deviceResolved;
    private bool _isRecording;

    /// <summary>Initializes a new instance of the <see cref="NAudioRecorder"/> class.</summary>
    /// <param name="settingsService">Supplies the configured microphone device id.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public NAudioRecorder(ISettingsService settingsService, ILogger<NAudioRecorder> logger)
    {
        _settingsService = settingsService;
        _logger = logger;

        // Pre-resolve the device on every settings change so StartAsync stays fast.
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
    public event EventHandler<float>? LevelChanged;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // waveInOpen can block for a few milliseconds; run off the caller's thread so the
        // hotkey-to-recording path stays responsive.
        return Task.Run(
            () =>
            {
                lock (_gate)
                {
                    if (_waveIn is not null)
                    {
                        throw new InvalidOperationException("A capture session is already running.");
                    }

                    if (!_deviceResolved)
                    {
                        ResolveDevice(_settingsService.Current.Recording.MicrophoneDeviceId);
                    }

                    try
                    {
                        _buffer = new MemoryStream();
                        _writer = new WaveFileWriter(new IgnoreDisposeStream(_buffer), CaptureFormat);
                        _stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        _waveIn = new WaveInEvent
                        {
                            DeviceNumber = _deviceNumber,
                            WaveFormat = CaptureFormat,
                            BufferMilliseconds = 50,
                        };
                        _waveIn.DataAvailable += OnDataAvailable;
                        _waveIn.RecordingStopped += OnRecordingStopped;
                        _waveIn.StartRecording();
                        _isRecording = true;
                    }
                    catch
                    {
                        _writer?.Dispose();
                        _buffer?.Dispose();
                        CleanupLocked();
                        throw;
                    }
                }

                _logger.LogDebug("Audio capture started on device {DeviceNumber}", _deviceNumber);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream> StopAsync()
    {
        WaveInEvent waveIn;
        TaskCompletionSource stopped;

        lock (_gate)
        {
            if (_waveIn is null)
            {
                throw new InvalidOperationException("StopAsync was called without a running capture session.");
            }

            waveIn = _waveIn;
            stopped = _stopped!;
            _isRecording = false;
        }

        waveIn.StopRecording();

        // RecordingStopped fires once the driver has flushed its final buffers; the timeout
        // guards against a wedged driver never raising it.
        await Task.WhenAny(stopped.Task, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);

        lock (_gate)
        {
            var capture = _buffer!;
            _writer!.Dispose(); // finalizes the WAV header; IgnoreDisposeStream keeps the buffer open
            CleanupLocked();

            capture.Position = 0;
            _logger.LogDebug("Audio capture stopped: {ByteCount} bytes", capture.Length);
            return capture;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _settingsService.SettingsChanged -= OnSettingsChanged;

        lock (_gate)
        {
            _writer?.Dispose();
            _buffer?.Dispose();
            CleanupLocked();
        }
    }

    /// <summary>Releases the capture session. Must be called inside the lock.</summary>
    private void CleanupLocked()
    {
        if (_waveIn is not null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.RecordingStopped -= OnRecordingStopped;
            _waveIn.Dispose();
        }

        _waveIn = null;
        _writer = null;
        _buffer = null;
        _stopped = null;
        _isRecording = false;
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
        => ResolveDevice(settings.Recording.MicrophoneDeviceId);

    private void ResolveDevice(string? deviceId)
    {
        var resolved = DefaultDevice;

        if (deviceId is not null)
        {
            try
            {
                for (var n = 0; n < WaveInEvent.DeviceCount; n++)
                {
                    if (WaveInEvent.GetCapabilities(n).ProductName == deviceId)
                    {
                        resolved = n;
                        break;
                    }
                }

                if (resolved == DefaultDevice)
                {
                    _logger.LogWarning("Configured microphone '{DeviceId}' not found; using the default device", deviceId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Microphone enumeration failed; using the default device");
            }
        }

        lock (_gate)
        {
            _deviceNumber = resolved;
            _deviceResolved = true;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_gate)
        {
            if (_writer is null)
            {
                return;
            }

            _writer.Write(e.Buffer, 0, e.BytesRecorded);
        }

        LevelChanged?.Invoke(this, ComputePeak(e.Buffer, e.BytesRecorded));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            _logger.LogError(e.Exception, "Audio capture stopped with an error");
        }

        _stopped?.TrySetResult();
    }

    /// <summary>Computes the peak amplitude of a 16-bit PCM buffer, normalized to 0..1.</summary>
    private static float ComputePeak(byte[] buffer, int bytesRecorded)
    {
        var peak = 0;
        for (var i = 0; i + 1 < bytesRecorded; i += 2)
        {
            int sample = Math.Abs((short)(buffer[i] | (buffer[i + 1] << 8)));
            if (sample > peak)
            {
                peak = sample;
            }
        }

        return peak / 32768f;
    }
}
