using DictateFlow.Core.Services.Audio;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace DictateFlow.App.Services.Audio;

/// <summary>
/// <see cref="IRecordingPlayer"/> backed by NAudio: a <see cref="MediaFoundationReader"/> feeds a
/// <see cref="WaveOutEvent"/>, so any Media-Foundation-decodable format (including <c>.m4a</c>)
/// plays. All mutable state is guarded by a lock because <see cref="WaveOutEvent"/> raises
/// <see cref="WaveOutEvent.PlaybackStopped"/> on a background thread.
/// </summary>
public sealed class RecordingPlayer : IRecordingPlayer
{
    private readonly ILogger<RecordingPlayer> _logger;
    private readonly object _gate = new();

    private WaveOutEvent? _output;
    private MediaFoundationReader? _reader;
    private bool _stopping;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="RecordingPlayer"/> class.</summary>
    /// <param name="logger">Receives diagnostic output.</param>
    public RecordingPlayer(ILogger<RecordingPlayer> logger) => _logger = logger;

    /// <inheritdoc />
    public event EventHandler? StateChanged;

    /// <inheritdoc />
    public bool IsPlaying
    {
        get
        {
            lock (_gate)
            {
                return _output?.PlaybackState == PlaybackState.Playing;
            }
        }
    }

    /// <inheritdoc />
    public bool HasTrack
    {
        get
        {
            lock (_gate)
            {
                return _reader is not null;
            }
        }
    }

    /// <inheritdoc />
    public TimeSpan Position
    {
        get
        {
            lock (_gate)
            {
                return _reader?.CurrentTime ?? TimeSpan.Zero;
            }
        }
    }

    /// <inheritdoc />
    public TimeSpan Duration
    {
        get
        {
            lock (_gate)
            {
                return _reader?.TotalTime ?? TimeSpan.Zero;
            }
        }
    }

    /// <inheritdoc />
    public void Load(string filePath)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            DisposeCurrentLocked();

            try
            {
                _reader = new MediaFoundationReader(filePath);
                _output = new WaveOutEvent();
                _output.PlaybackStopped += OnPlaybackStopped;
                _output.Init(_reader);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not load recording '{Path}' for playback", filePath);
                DisposeCurrentLocked();
                throw;
            }
        }

        RaiseStateChanged();
    }

    /// <inheritdoc />
    public void Play()
    {
        lock (_gate)
        {
            _output?.Play();
        }

        RaiseStateChanged();
    }

    /// <inheritdoc />
    public void Pause()
    {
        lock (_gate)
        {
            _output?.Pause();
        }

        RaiseStateChanged();
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_gate)
        {
            if (_output is null || _reader is null)
            {
                return;
            }

            // Suppress the end-of-track auto-rewind logic; this is an explicit stop.
            _stopping = true;
            _output.Stop();
            _reader.CurrentTime = TimeSpan.Zero;
            _stopping = false;
        }

        RaiseStateChanged();
    }

    /// <inheritdoc />
    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            if (_reader is null)
            {
                return;
            }

            var clamped = position < TimeSpan.Zero
                ? TimeSpan.Zero
                : position > _reader.TotalTime ? _reader.TotalTime : position;
            _reader.CurrentTime = clamped;
        }

        RaiseStateChanged();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisposeCurrentLocked();
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            _logger.LogWarning(e.Exception, "Playback stopped with an error");
        }

        lock (_gate)
        {
            // Natural end-of-track: rewind so the next Play starts over. An explicit Stop has
            // already handled the rewind and set _stopping.
            if (!_stopping && _reader is not null)
            {
                _reader.CurrentTime = TimeSpan.Zero;
            }
        }

        RaiseStateChanged();
    }

    /// <summary>Tears down the current output and reader. Must be called inside the lock.</summary>
    private void DisposeCurrentLocked()
    {
        if (_output is not null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            try
            {
                _output.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Disposing the audio output failed");
            }

            _output = null;
        }

        _reader?.Dispose();
        _reader = null;
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
