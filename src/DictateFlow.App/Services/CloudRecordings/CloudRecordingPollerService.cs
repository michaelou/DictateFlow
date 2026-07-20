using DictateFlow.Core.Services;
using DictateFlow.Core.Services.CloudRecordings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.Services.CloudRecordings;

/// <summary>
/// The background poller that checks the configured Azure Blob container for new recordings on
/// an interval and transcribes them. This is DictateFlow's first hosted service; it starts with
/// the host and stops on shutdown. The interval and the enabled switch are read from settings on
/// every iteration, so toggling the feature or changing the interval applies without a restart.
/// A tray notification announces new transcriptions, and <see cref="NewRecordingsTranscribed"/>
/// lets an open review window refresh itself.
/// </summary>
public sealed class CloudRecordingPollerService : BackgroundService
{
    /// <summary>Grace period before the first check so startup (including settings load) can finish.</summary>
    private static readonly TimeSpan StartupGrace = TimeSpan.FromSeconds(10);

    private readonly ICloudTranscriptionService _cloudTranscription;
    private readonly ISettingsService _settingsService;
    private readonly ICloudRecordingNotifier _notifier;
    private readonly ILogger<CloudRecordingPollerService> _logger;

    /// <summary>Initializes a new instance of the <see cref="CloudRecordingPollerService"/> class.</summary>
    /// <param name="cloudTranscription">Runs the list-download-decode-transcribe-store workflow.</param>
    /// <param name="settingsService">Supplies the enabled switch and polling interval, read per iteration.</param>
    /// <param name="notifier">Shows the persistent new-recordings notification.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public CloudRecordingPollerService(
        ICloudTranscriptionService cloudTranscription,
        ISettingsService settingsService,
        ICloudRecordingNotifier notifier,
        ILogger<CloudRecordingPollerService> logger)
    {
        _cloudTranscription = cloudTranscription;
        _settingsService = settingsService;
        _notifier = notifier;
        _logger = logger;
    }

    /// <summary>Raised (with the count) after a periodic check transcribes new recordings.</summary>
    public event EventHandler<int>? NewRecordingsTranscribed;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await DelayAsync(StartupGrace, stoppingToken).ConfigureAwait(false))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_settingsService.Current.CloudRecordings.Enabled)
            {
                await RunCheckAsync(stoppingToken).ConfigureAwait(false);
            }

            var minutes = Math.Max(1, _settingsService.Current.CloudRecordings.PollingIntervalMinutes);
            if (!await DelayAsync(TimeSpan.FromMinutes(minutes), stoppingToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    private async Task RunCheckAsync(CancellationToken stoppingToken)
    {
        try
        {
            var count = await _cloudTranscription.CheckAndTranscribeNewAsync(null, stoppingToken).ConfigureAwait(false);
            if (count <= 0)
            {
                return;
            }

            NewRecordingsTranscribed?.Invoke(this, count);
            _notifier.ShowNewRecordings(count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            // A failed poll (bad config, network) must never take the app down; the next tick retries.
            _logger.LogError(ex, "Cloud recordings poll failed");
        }
    }

    /// <summary>Waits <paramref name="delay"/>; returns <see langword="false"/> when cancelled (shutting down).</summary>
    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
