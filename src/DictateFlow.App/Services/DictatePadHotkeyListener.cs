using DictateFlow.Core.Services.Audio;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.Services;

/// <summary>
/// Bridges the global DictatePad hotkey to the window-opening action. It subscribes to
/// <see cref="IHotkeyService.DictatePadPressed"/> in its constructor (the same
/// constructor-subscription pattern the dictation controller uses to arm its hotkeys), so it
/// must be materialized at startup. Opening a window is an App-layer concern, which is why this
/// lives here rather than in the Core dictation controller.
/// </summary>
public sealed class DictatePadHotkeyListener : IDisposable
{
    private readonly IHotkeyService _hotkeyService;
    private readonly IAppActions _appActions;
    private readonly ILogger<DictatePadHotkeyListener> _logger;

    /// <summary>Initializes a new instance of the <see cref="DictatePadHotkeyListener"/> class.</summary>
    /// <param name="hotkeyService">Raises the DictatePad hotkey event.</param>
    /// <param name="appActions">Opens the DictatePad window (marshals to the UI thread).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public DictatePadHotkeyListener(
        IHotkeyService hotkeyService,
        IAppActions appActions,
        ILogger<DictatePadHotkeyListener> logger)
    {
        _hotkeyService = hotkeyService;
        _appActions = appActions;
        _logger = logger;
        _hotkeyService.DictatePadPressed += OnDictatePadPressed;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _hotkeyService.DictatePadPressed -= OnDictatePadPressed;
    }

    private void OnDictatePadPressed(object? sender, EventArgs e)
    {
        _logger.LogInformation("DictatePad hotkey pressed; opening the scratchpad");
        _appActions.OpenDictatePad();
    }
}
