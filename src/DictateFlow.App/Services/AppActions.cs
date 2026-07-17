using System.Windows;
using System.Windows.Threading;
using DictateFlow.Core.Services.Updates;

namespace DictateFlow.App.Services;

/// <summary>
/// Default <see cref="IAppActions"/> implementation over <see cref="IWindowService"/> and
/// <see cref="IUpdateService"/>. Window operations are marshalled onto the UI dispatcher so the
/// voice command pipeline (which runs on a background thread) can open windows safely.
/// </summary>
public sealed class AppActions : IAppActions
{
    private readonly IWindowService _windowService;
    private readonly IUpdateService _updateService;

    /// <summary>Initializes a new instance of the <see cref="AppActions"/> class.</summary>
    /// <param name="windowService">Opens the application windows.</param>
    /// <param name="updateService">Checks GitHub for a newer release.</param>
    public AppActions(IWindowService windowService, IUpdateService updateService)
    {
        _windowService = windowService;
        _updateService = updateService;
    }

    /// <inheritdoc />
    public void OpenSettings() => OnUiThread(() => _windowService.ShowSettingsWindow());

    /// <inheritdoc />
    public void OpenSettings(string section) => OnUiThread(() => _windowService.ShowSettingsWindow(section));

    /// <inheritdoc />
    public void ShowHistory() => OnUiThread(() => _windowService.ShowHistoryWindow());

    /// <inheritdoc />
    public void OpenCostDashboard() => OnUiThread(() => _windowService.ShowCostDashboardWindow());

    /// <inheritdoc />
    public void OpenDictatePad() => OnUiThread(() => _windowService.ShowDictatePadWindow());

    /// <inheritdoc />
    public async Task CheckForUpdatesAsync()
    {
        var result = await _updateService.CheckForUpdatesAsync().ConfigureAwait(false);
        OnUiThread(() => _windowService.ShowUpdateWindow(result));
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
