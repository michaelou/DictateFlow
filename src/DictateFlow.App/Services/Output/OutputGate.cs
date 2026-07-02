using System.Windows;
using System.Windows.Threading;
using DictateFlow.App.Interop;
using DictateFlow.App.ViewModels;
using DictateFlow.App.Views;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Pipeline;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.Services.Output;

/// <summary>
/// Default <see cref="IOutputGate"/> implementation. Reads <c>Output.Mode</c> on every call
/// (applied live): <c>Automatic</c> passes the draft text straight through (surfacing an
/// enhancement-fallback warning as a tray notification); <c>Preview</c> shows the modal
/// <see cref="PreviewWindow"/> and, when the user chooses Paste, re-focuses the original
/// target window before the pipeline delivers the text.
/// </summary>
public sealed class OutputGate : IOutputGate
{
    /// <summary>
    /// Pause after <c>SetForegroundWindow</c> before returning to the pipeline: the target
    /// needs time to process the activation before it can receive the injected input.
    /// </summary>
    private static readonly TimeSpan RefocusDelay = TimeSpan.FromMilliseconds(200);

    private readonly ISettingsService _settingsService;
    private readonly IForegroundAppService _foregroundAppService;
    private readonly Func<ITrayIconService> _trayIconServiceFactory;
    private readonly ILogger<OutputGate> _logger;

    /// <summary>Initializes a new instance of the <see cref="OutputGate"/> class.</summary>
    /// <param name="settingsService">Supplies the output mode, read per call.</param>
    /// <param name="foregroundAppService">Remembers the target window to re-focus after the preview.</param>
    /// <param name="trayIconServiceFactory">
    /// Lazily resolves the tray icon service used for enhancement-fallback warnings. A factory
    /// (not the instance) because the tray icon's view model depends on the dictation
    /// controller, which depends on the pipeline, which depends on this gate — resolving it
    /// eagerly would close a constructor cycle.
    /// </param>
    /// <param name="logger">Receives diagnostic output.</param>
    public OutputGate(
        ISettingsService settingsService,
        IForegroundAppService foregroundAppService,
        Func<ITrayIconService> trayIconServiceFactory,
        ILogger<OutputGate> logger)
    {
        _settingsService = settingsService;
        _foregroundAppService = foregroundAppService;
        _trayIconServiceFactory = trayIconServiceFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> ConfirmAsync(PipelineResult draft)
    {
        var mode = _settingsService.Current.Output.Mode;
        if (!string.Equals(mode, OutputModes.Preview, StringComparison.OrdinalIgnoreCase))
        {
            // Automatic mode: no dialog. The user still learns about an enhancement fallback.
            if (draft.ErrorMessage is not null)
            {
                _trayIconServiceFactory().ShowWarningNotification("DictateFlow", draft.ErrorMessage);
            }

            return draft.FinalText;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            _logger.LogWarning("Preview mode requested but no UI dispatcher is available; delivering without preview");
            return draft.FinalText;
        }

        var (outcome, text) = await dispatcher.InvokeAsync(
            () => ShowPreviewDialog(draft), DispatcherPriority.Normal);

        switch (outcome)
        {
            case PreviewOutcome.Paste:
                await RefocusTargetWindowAsync().ConfigureAwait(false);
                return text;
            case PreviewOutcome.CopyOnly:
                _logger.LogInformation("Preview closed with Copy only; text copied, nothing delivered");
                return null;
            default:
                _logger.LogInformation("Preview cancelled; dictation discarded");
                return null;
        }
    }

    /// <summary>Shows the modal preview dialog and returns the user's choice plus the edited text.</summary>
    private static (PreviewOutcome Outcome, string Text) ShowPreviewDialog(PipelineResult draft)
    {
        var viewModel = new PreviewViewModel
        {
            Text = draft.FinalText ?? "",
            RawTranscript = draft.RawTranscript ?? "",
            Warning = draft.ErrorMessage,
        };
        var window = new PreviewWindow { DataContext = viewModel };
        viewModel.CloseRequested += (_, _) => window.Close();

        window.ShowDialog();
        return (viewModel.Outcome, viewModel.Text);
    }

    /// <summary>
    /// Gives focus back to the window the user was dictating into (the preview dialog took
    /// it), then waits briefly so the injected paste/keystrokes land in the right place.
    /// </summary>
    private async Task RefocusTargetWindowAsync()
    {
        var handle = _foregroundAppService.LastCapturedWindowHandle;
        if (handle == 0)
        {
            _logger.LogWarning("No target window handle was captured; output goes to whichever window has focus");
            return;
        }

        if (!NativeMethods.SetForegroundWindow(handle))
        {
            _logger.LogWarning("SetForegroundWindow declined; output goes to whichever window has focus");
        }

        await Task.Delay(RefocusDelay).ConfigureAwait(false);
    }
}
