using System.Windows;
using System.Windows.Threading;
using DictateFlow.App.ViewModels;
using DictateFlow.App.Views;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.Services;

/// <summary>
/// Default <see cref="IDictationResultPresenter"/> implementation. Listens to
/// <see cref="IDictationController"/> dictation events, shows the (single, reused)
/// <see cref="TranscriptWindow"/> for results (enhanced text, with the raw transcript in a
/// collapsed expander and a warning banner when enhancement fell back), and raises a tray
/// notification with the actionable message when transcription fails.
/// </summary>
public sealed class DictationResultPresenter : IDictationResultPresenter
{
    private readonly IDictationController _controller;
    private readonly ITrayIconService _trayIconService;
    private readonly TranscriptViewModel _viewModel;
    private readonly ILogger<DictationResultPresenter> _logger;
    private TranscriptWindow? _window;

    /// <summary>Initializes a new instance of the <see cref="DictationResultPresenter"/> class.</summary>
    /// <param name="controller">Raises the transcription events this presenter surfaces.</param>
    /// <param name="trayIconService">Shows failure notifications.</param>
    /// <param name="viewModel">The view model shared with the transcript window.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public DictationResultPresenter(
        IDictationController controller,
        ITrayIconService trayIconService,
        TranscriptViewModel viewModel,
        ILogger<DictationResultPresenter> logger)
    {
        _controller = controller;
        _trayIconService = trayIconService;
        _viewModel = viewModel;
        _logger = logger;

        _controller.DictationCompleted += OnDictationCompleted;
        _controller.DictationFailed += OnDictationFailed;
        _viewModel.CloseRequested += OnCloseRequested;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _controller.DictationCompleted -= OnDictationCompleted;
        _controller.DictationFailed -= OnDictationFailed;
        _viewModel.CloseRequested -= OnCloseRequested;

        OnUiThread(() =>
        {
            _window?.Close();
            _window = null;
        });
    }

    private void OnDictationCompleted(object? sender, DictationResult result) => OnUiThread(() =>
    {
        _viewModel.Text = result.Text;
        _viewModel.RawTranscript = result.RawTranscript;
        _viewModel.Warning = result.EnhancementWarning;
        _viewModel.StatusMessage = null;

        if (_window is null)
        {
            _window = new TranscriptWindow { DataContext = _viewModel };
            _window.Closed += (_, _) => _window = null;
        }

        _window.Show();
        _window.Activate();
    });

    private void OnDictationFailed(object? sender, ProviderException error)
    {
        _logger.LogDebug("Presenting dictation failure from {ProviderName}", error.ProviderName);
        var title = error.IsConfigurationError
            ? "DictateFlow — check your settings"
            : "DictateFlow — transcription failed";
        _trayIconService.ShowErrorNotification(title, error.Message);
    }

    private void OnCloseRequested(object? sender, EventArgs e) => OnUiThread(() => _window?.Close());

    /// <summary>Runs <paramref name="action"/> on the UI dispatcher (controller events fire on worker threads).</summary>
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
