using System.Windows;
using System.Windows.Threading;
using DictateFlow.App.ViewModels;
using DictateFlow.App.Views;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Audio;

namespace DictateFlow.App.Services.Audio;

/// <summary>
/// Default <see cref="IRecordingOverlay"/> implementation. Owns the single
/// <see cref="OverlayWindow"/> instance (created lazily) and marshals every call onto the
/// UI dispatcher, so the dictation controller and audio threads can call it directly.
/// </summary>
public sealed class RecordingOverlayService : IRecordingOverlay, IDisposable
{
    private readonly OverlayViewModel _viewModel;
    private OverlayWindow? _window;

    /// <summary>Initializes a new instance of the <see cref="RecordingOverlayService"/> class.</summary>
    /// <param name="viewModel">The view model shared with the overlay window.</param>
    public RecordingOverlayService(OverlayViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    /// <inheritdoc />
    public void ShowListening() => OnUiThread(() =>
    {
        _viewModel.Level = 0f;
        ShowState(OverlayState.Listening);
    });

    /// <inheritdoc />
    public void UpdateLevel(float level) => OnUiThread(() => _viewModel.Level = level);

    /// <inheritdoc />
    public void ShowProcessing() => OnUiThread(() => ShowState(OverlayState.Processing));

    /// <inheritdoc />
    public void ShowSuccess() => OnUiThread(() => ShowState(OverlayState.Success));

    /// <inheritdoc />
    public void ShowError(string? message = null) => OnUiThread(() =>
    {
        _viewModel.ErrorMessage = string.IsNullOrWhiteSpace(message) ? null : message;
        ShowState(OverlayState.Error);
    });

    /// <inheritdoc />
    public void Hide() => OnUiThread(() =>
    {
        _viewModel.State = OverlayState.Hidden;
        _window?.FadeOut();
    });

    /// <inheritdoc />
    public void Dispose() => OnUiThread(() =>
    {
        _window?.Close();
        _window = null;
    });

    /// <summary>Puts the overlay window into <paramref name="state"/>, creating it on first use.</summary>
    private void ShowState(OverlayState state)
    {
        _window ??= new OverlayWindow { DataContext = _viewModel };
        _viewModel.State = state;
        _window.FadeIn();
    }

    /// <summary>
    /// Queues <paramref name="action"/> on the UI dispatcher without blocking the caller
    /// (audio callbacks must never wait on the UI thread). Dispatcher ordering keeps
    /// show/level/hide calls in sequence.
    /// </summary>
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
