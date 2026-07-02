using DictateFlow.Core.Services.Audio;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.Services;

/// <summary>
/// Default <see cref="IDictationFailureNotifier"/> implementation. Listens to
/// <see cref="IDictationController.DictationFailed"/> and raises a tray notification with
/// the pipeline's user-presentable message (the overlay's Error state is too brief to carry
/// details).
/// </summary>
public sealed class DictationFailureNotifier : IDictationFailureNotifier
{
    private readonly IDictationController _controller;
    private readonly ITrayIconService _trayIconService;
    private readonly ILogger<DictationFailureNotifier> _logger;

    /// <summary>Initializes a new instance of the <see cref="DictationFailureNotifier"/> class.</summary>
    /// <param name="controller">Raises the failure events this notifier surfaces.</param>
    /// <param name="trayIconService">Shows the failure notifications.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public DictationFailureNotifier(
        IDictationController controller,
        ITrayIconService trayIconService,
        ILogger<DictationFailureNotifier> logger)
    {
        _controller = controller;
        _trayIconService = trayIconService;
        _logger = logger;

        _controller.DictationFailed += OnDictationFailed;
    }

    /// <inheritdoc />
    public void Dispose() => _controller.DictationFailed -= OnDictationFailed;

    private void OnDictationFailed(object? sender, string message)
    {
        _logger.LogDebug("Notifying dictation failure: {Message}", message);
        _trayIconService.ShowErrorNotification("DictateFlow — dictation failed", message);
    }
}
