using DictateFlow.Core.Services.Audio;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.Services;

/// <summary>
/// Default <see cref="IDictationFailureNotifier"/> implementation. Listens to
/// <see cref="IDictationController.DictationFailed"/> and raises a tray notification with
/// the pipeline's user-presentable message (the overlay's Error state is too brief to carry
/// details). Configuration errors get an "Open Settings" click action so the fix is one
/// click away.
/// </summary>
public sealed class DictationFailureNotifier : IDictationFailureNotifier
{
    private readonly IDictationController _controller;
    private readonly ITrayIconService _trayIconService;
    private readonly IWindowService _windowService;
    private readonly ILogger<DictationFailureNotifier> _logger;

    /// <summary>Initializes a new instance of the <see cref="DictationFailureNotifier"/> class.</summary>
    /// <param name="controller">Raises the failure events this notifier surfaces.</param>
    /// <param name="trayIconService">Shows the failure notifications.</param>
    /// <param name="windowService">Opens the Settings window from configuration-error notifications.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public DictationFailureNotifier(
        IDictationController controller,
        ITrayIconService trayIconService,
        IWindowService windowService,
        ILogger<DictationFailureNotifier> logger)
    {
        _controller = controller;
        _trayIconService = trayIconService;
        _windowService = windowService;
        _logger = logger;

        _controller.DictationFailed += OnDictationFailed;
    }

    /// <inheritdoc />
    public void Dispose() => _controller.DictationFailed -= OnDictationFailed;

    private void OnDictationFailed(object? sender, DictationFailedEventArgs e)
    {
        _logger.LogDebug(
            "Notifying dictation failure (configuration error: {IsConfigurationError}): {Message}",
            e.IsConfigurationError, e.Message);

        if (e.IsConfigurationError)
        {
            _trayIconService.ShowErrorNotification(
                "DictateFlow — dictation failed",
                $"{e.Message}\nClick here to open Settings.",
                onClick: _windowService.ShowSettingsWindow);
        }
        else
        {
            _trayIconService.ShowErrorNotification("DictateFlow — dictation failed", e.Message);
        }
    }
}
