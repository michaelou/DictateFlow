using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DictateFlow.App.Services.Audio;
using DictateFlow.App.Services.CloudRecordings;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.CloudRecordings;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.ViewModels;

/// <summary>One transcribed cloud recording prepared for display.</summary>
public sealed class CloudRecordingItem
{
    /// <summary>Initializes a new instance of the <see cref="CloudRecordingItem"/> class.</summary>
    /// <param name="entry">The stored entry (UTC timestamp) to present.</param>
    public CloudRecordingItem(CloudRecordingEntry entry)
    {
        Entry = entry;
        ShortName = Path.GetFileName(entry.BlobName);
        TimestampText = entry.TranscribedUtc.ToLocalTime().ToString("g");
        Preview = SingleLinePreview(entry.Transcript);
        DurationText = entry.DurationSeconds is { } seconds and > 0
            ? TimeSpan.FromSeconds(seconds).ToString(@"m\:ss")
            : "";
    }

    /// <summary>Gets the underlying stored entry.</summary>
    public CloudRecordingEntry Entry { get; }

    /// <summary>Gets the blob's file name (without any virtual-folder prefix).</summary>
    public string ShortName { get; }

    /// <summary>Gets the transcription time formatted in local time.</summary>
    public string TimestampText { get; }

    /// <summary>Gets the single-line transcript preview shown in the list.</summary>
    public string Preview { get; }

    /// <summary>Gets the audio duration formatted as m:ss, or empty when unknown.</summary>
    public string DurationText { get; }

    /// <summary>Gets the full transcript (shown in the detail pane and copied by Copy).</summary>
    public string FullText => Entry.Transcript;

    /// <summary>Collapses text to a single trimmed line, ellipsized past 120 characters.</summary>
    private static string SingleLinePreview(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var singleLine = text.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= 120 ? singleLine : $"{singleLine[..120].TrimEnd()}…";
    }
}

/// <summary>
/// View model backing the Cloud Recordings review window: lists the transcribed recordings,
/// lets the user trigger an on-demand check for new ones, and plays a recording back in-app with
/// a seek bar. Refreshes itself when the background poller reports new transcriptions.
/// </summary>
public partial class CloudRecordingsViewModel : ObservableObject
{
    private readonly ICloudRecordingRepository _repository;
    private readonly ICloudTranscriptionService _cloudTranscription;
    private readonly ICloudRecordingSource _source;
    private readonly IRecordingPlayer _player;
    private readonly CloudRecordingPollerService _poller;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<CloudRecordingsViewModel> _logger;

    /// <summary>Local temp files downloaded for playback, keyed by blob name, cleaned up on close.</summary>
    private readonly Dictionary<string, string> _playbackCache = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _positionTimer;

    /// <summary>Suppresses the seek-on-change while the timer pushes the position into the slider.</summary>
    private bool _suppressSeek;
    private CancellationTokenSource? _checkCts;

    /// <summary>The blob name of the currently loaded track (identity; file names aren't unique across folders).</summary>
    private string? _currentBlobName;

    /// <summary>Initializes a new instance of the <see cref="CloudRecordingsViewModel"/> class.</summary>
    /// <param name="repository">Reads and mutates the stored cloud recordings.</param>
    /// <param name="cloudTranscription">Runs the on-demand "check for new recordings" workflow.</param>
    /// <param name="source">Downloads a recording for playback.</param>
    /// <param name="player">Plays the downloaded recording.</param>
    /// <param name="poller">The background poller; its event refreshes the list when new items arrive.</param>
    /// <param name="settingsService">Reports whether the feature is configured, for the status hint.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public CloudRecordingsViewModel(
        ICloudRecordingRepository repository,
        ICloudTranscriptionService cloudTranscription,
        ICloudRecordingSource source,
        IRecordingPlayer player,
        CloudRecordingPollerService poller,
        ISettingsService settingsService,
        ILogger<CloudRecordingsViewModel> logger)
    {
        _repository = repository;
        _cloudTranscription = cloudTranscription;
        _source = source;
        _player = player;
        _poller = poller;
        _settingsService = settingsService;
        _logger = logger;

        _player.StateChanged += OnPlayerStateChanged;
        _poller.NewRecordingsTranscribed += OnNewRecordingsTranscribed;

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _positionTimer.Tick += (_, _) => UpdatePositionFromPlayer();
    }

    /// <summary>Gets or sets the recordings currently shown, newest first.</summary>
    [ObservableProperty]
    private IReadOnlyList<CloudRecordingItem> _entries = [];

    /// <summary>Gets or sets the recording selected in the list; its full text fills the detail pane.</summary>
    [ObservableProperty]
    private CloudRecordingItem? _selectedEntry;

    /// <summary>Gets or sets a value indicating whether a check or a download is in progress.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Gets or sets the status line under the list.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Gets or sets the short name of the recording currently loaded for playback.</summary>
    [ObservableProperty]
    private string? _nowPlayingName;

    /// <summary>Gets or sets a value indicating whether playback is currently running.</summary>
    [ObservableProperty]
    private bool _isPlaying;

    /// <summary>Gets or sets the loaded track's total length in seconds (the seek bar maximum).</summary>
    [ObservableProperty]
    private double _durationSeconds;

    /// <summary>Gets or sets the current playback position in seconds (bound to the seek bar).</summary>
    [ObservableProperty]
    private double _positionSeconds;

    /// <summary>Gets the current position formatted as m:ss.</summary>
    public string PositionText => FormatTime(PositionSeconds);

    /// <summary>Gets the track length formatted as m:ss.</summary>
    public string DurationText => FormatTime(DurationSeconds);

    /// <summary>Seeks when the user drags the slider (but not when the timer pushes the position in).</summary>
    partial void OnPositionSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(PositionText));
        if (!_suppressSeek && _player.HasTrack)
        {
            _player.Seek(TimeSpan.FromSeconds(value));
        }
    }

    partial void OnDurationSecondsChanged(double value) => OnPropertyChanged(nameof(DurationText));

    /// <summary>Loads the stored recordings, newest first.</summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entries = await _repository.GetAllAsync(cancellationToken);
            Entries = [.. entries.Select(e => new CloudRecordingItem(e))];
            StatusMessage = BuildCountStatus(Entries.Count);
        }
        catch (OperationCanceledException)
        {
            // The window closed.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load cloud recordings");
            StatusMessage = "Could not load recordings.";
        }
    }

    /// <summary>Checks the container for new recordings and transcribes them, then refreshes.</summary>
    [RelayCommand]
    private async Task CheckNowAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (!_settingsService.Current.CloudRecordings.Enabled)
        {
            StatusMessage = "Cloud recordings are disabled — enable them in Settings → Cloud Recordings.";
            return;
        }

        IsBusy = true;
        _checkCts = new CancellationTokenSource();
        var progress = new Progress<string>(message => StatusMessage = message);
        try
        {
            var count = await _cloudTranscription.CheckAndTranscribeNewAsync(progress, _checkCts.Token);
            await RefreshAsync(_checkCts.Token);
            StatusMessage = count switch
            {
                0 => "No new recordings.",
                1 => "Transcribed 1 new recording.",
                _ => $"Transcribed {count} new recordings.",
            };
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Check cancelled.";
        }
        catch (ProviderException ex)
        {
            _logger.LogWarning(ex, "Cloud recordings check failed");
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cloud recordings check failed");
            StatusMessage = "Could not check for new recordings — see the log for details.";
        }
        finally
        {
            IsBusy = false;
            _checkCts?.Dispose();
            _checkCts = null;
        }
    }

    /// <summary>Copies a recording's full transcript to the clipboard.</summary>
    /// <param name="item">The recording to copy.</param>
    [RelayCommand]
    private void Copy(CloudRecordingItem? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(item.FullText);
            StatusMessage = "Copied to clipboard.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not copy a cloud recording transcript to the clipboard");
            StatusMessage = "Could not access the clipboard — try again.";
        }
    }

    /// <summary>Deletes one stored recording (the blob is left untouched) and refreshes the list.</summary>
    /// <param name="item">The recording to delete.</param>
    [RelayCommand]
    private async Task DeleteAsync(CloudRecordingItem? item)
    {
        if (item is null)
        {
            return;
        }

        var confirmed = MessageBox.Show(
            "Remove this transcription from the list? The recording stays in your Azure container, so a later check will transcribe it again.",
            "DictateFlow",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
        if (!confirmed)
        {
            return;
        }

        try
        {
            await _repository.DeleteAsync(item.Entry.Id);
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not delete cloud recording {Id}", item.Entry.Id);
            StatusMessage = "Could not delete the entry.";
        }
    }

    /// <summary>
    /// Plays the given recording, or toggles play/pause when it is already the loaded track.
    /// Downloads the blob to a temp file on first play.
    /// </summary>
    /// <param name="item">The recording to play.</param>
    [RelayCommand]
    private async Task PlayPauseAsync(CloudRecordingItem? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedEntry = item;

        // Same track already loaded — just toggle.
        if (_currentBlobName == item.Entry.BlobName && _player.HasTrack)
        {
            if (_player.IsPlaying)
            {
                _player.Pause();
            }
            else
            {
                _player.Play();
            }

            return;
        }

        try
        {
            var path = await EnsureDownloadedAsync(item.Entry.BlobName);
            _player.Load(path);
            _currentBlobName = item.Entry.BlobName;
            NowPlayingName = item.ShortName;

            // Reset the position (suppressed) before the duration changes the slider's maximum,
            // so the leftover position from a previous track can't be coerced into a stray seek.
            _suppressSeek = true;
            PositionSeconds = 0;
            _suppressSeek = false;
            DurationSeconds = _player.Duration.TotalSeconds;

            _player.Play();
        }
        catch (ProviderException ex)
        {
            _logger.LogWarning(ex, "Could not download recording '{Blob}' for playback", item.Entry.BlobName);
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not play recording '{Blob}'", item.Entry.BlobName);
            StatusMessage = "Could not play the recording — see the log for details.";
        }
    }

    /// <summary>Toggles play/pause for the currently loaded track (the transport-bar button).</summary>
    [RelayCommand]
    private void TogglePlayback()
    {
        if (!_player.HasTrack)
        {
            return;
        }

        if (_player.IsPlaying)
        {
            _player.Pause();
        }
        else
        {
            _player.Play();
        }
    }

    /// <summary>Stops playback and rewinds to the start.</summary>
    [RelayCommand]
    private void Stop() => _player.Stop();

    /// <summary>
    /// Releases the player, timer, event subscriptions and temp playback files. Called by the
    /// window service when the window closes.
    /// </summary>
    public void Cleanup()
    {
        _positionTimer.Stop();
        _checkCts?.Cancel();
        _player.StateChanged -= OnPlayerStateChanged;
        _poller.NewRecordingsTranscribed -= OnNewRecordingsTranscribed;
        _player.Dispose();

        foreach (var path in _playbackCache.Values)
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
                _logger.LogDebug(ex, "Could not delete temporary playback file '{Path}'", path);
            }
        }

        _playbackCache.Clear();
    }

    /// <summary>Downloads the blob to a temp file once, returning the cached path afterwards.</summary>
    private async Task<string> EnsureDownloadedAsync(string blobName)
    {
        if (_playbackCache.TryGetValue(blobName, out var cached) && File.Exists(cached))
        {
            return cached;
        }

        IsBusy = true;
        StatusMessage = "Downloading recording…";
        try
        {
            var directory = Path.Combine(Path.GetTempPath(), "DictateFlow", "CloudRecordings");
            Directory.CreateDirectory(directory);
            var extension = Path.GetExtension(blobName);
            var path = Path.Combine(
                directory, "play_" + Guid.NewGuid().ToString("N") + (string.IsNullOrEmpty(extension) ? ".m4a" : extension));

            await _source.DownloadToFileAsync(blobName, path, CancellationToken.None);
            _playbackCache[blobName] = path;
            StatusMessage = null;
            return path;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnPlayerStateChanged(object? sender, EventArgs e)
        => OnUiThread(() =>
        {
            IsPlaying = _player.IsPlaying;
            if (IsPlaying)
            {
                _positionTimer.Start();
            }
            else
            {
                _positionTimer.Stop();
                UpdatePositionFromPlayer();
            }
        });

    private void OnNewRecordingsTranscribed(object? sender, int count)
        => OnUiThread(() => _ = RefreshAsync(CancellationToken.None));

    private void UpdatePositionFromPlayer()
    {
        _suppressSeek = true;
        PositionSeconds = _player.Position.TotalSeconds;
        _suppressSeek = false;
    }

    private static string BuildCountStatus(int count)
        => count == 1 ? "1 recording" : $"{count} recordings";

    private static string FormatTime(double seconds)
        => seconds <= 0 ? "0:00" : TimeSpan.FromSeconds(seconds).ToString(@"m\:ss");

    /// <summary>Runs <paramref name="action"/> on the UI dispatcher (directly when already on it).</summary>
    private static void OnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
