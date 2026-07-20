using System.Windows;
using System.Windows.Threading;
using DictateFlow.App.Views;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.Services.CloudRecordings;

/// <summary>
/// Default <see cref="ICloudRecordingNotifier"/> implementation. Owns a single
/// <see cref="CloudRecordingToastWindow"/> instance, created lazily and reused across checks.
/// Clicking the toast opens the Cloud Recordings window (via <see cref="IAppActions"/>) and
/// closes the toast; the close button just closes it. All window work runs on the UI dispatcher.
/// </summary>
public sealed class CloudRecordingNotifier : ICloudRecordingNotifier, IDisposable
{
    private readonly IAppActions _appActions;
    private readonly ILogger<CloudRecordingNotifier> _logger;
    private CloudRecordingToastWindow? _toast;

    /// <summary>Initializes a new instance of the <see cref="CloudRecordingNotifier"/> class.</summary>
    /// <param name="appActions">Opens the Cloud Recordings review window when the toast is clicked.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public CloudRecordingNotifier(IAppActions appActions, ILogger<CloudRecordingNotifier> logger)
    {
        _appActions = appActions;
        _logger = logger;
    }

    /// <inheritdoc />
    public void ShowNewRecordings(int count) => OnUiThread(() =>
    {
        try
        {
            _toast ??= CreateToast();
            _toast.Message = count == 1
                ? "1 new recording transcribed."
                : $"{count} new recordings transcribed.";
            _toast.ShowToast();
        }
        catch (Exception ex)
        {
            // A notification must never take the app down.
            _logger.LogWarning(ex, "Could not show the cloud recordings notification");
        }
    });

    /// <inheritdoc />
    public void Dispose() => OnUiThread(() =>
    {
        _toast?.Close();
        _toast = null;
    });

    private CloudRecordingToastWindow CreateToast()
    {
        var toast = new CloudRecordingToastWindow();
        toast.Clicked += (_, _) =>
        {
            HideToast();
            _appActions.ShowCloudRecordings();
        };
        toast.Dismissed += (_, _) => HideToast();
        // A user-closed window can't be re-shown; drop the reference so the next batch rebuilds it.
        toast.Closed += (_, _) => _toast = null;
        return toast;
    }

    private void HideToast()
    {
        _toast?.Close();
        _toast = null;
    }

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
            dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
        }
    }
}
