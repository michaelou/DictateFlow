using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Output;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.Services.Output;

/// <summary>
/// Delivers text by placing it on the clipboard and sending Ctrl+V to the foreground window,
/// then restores the previous clipboard text. Fast and reliable in most applications; the
/// simulated-keyboard provider covers apps that block programmatic paste. Clipboard access
/// runs on the WPF UI thread (thread affinity); the keystroke injection does not need it.
/// </summary>
public sealed class ClipboardPasteOutputProvider : IOutputProvider
{
    /// <summary>Attempts to open the clipboard before giving up (other apps briefly lock it).</summary>
    private const int ClipboardRetryCount = 3;

    /// <summary>Pause between clipboard open attempts.</summary>
    private static readonly TimeSpan ClipboardRetryDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>Pause between setting the clipboard and sending Ctrl+V, so the set is visible to the target.</summary>
    private static readonly TimeSpan PreKeystrokeDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Pause between sending Ctrl+V and restoring the previous clipboard content. The target
    /// application processes the paste asynchronously — restoring earlier would make it paste
    /// the OLD clipboard content instead of the dictated text.
    /// </summary>
    private static readonly TimeSpan PasteCompletionDelay = TimeSpan.FromMilliseconds(300);

    private readonly ILogger<ClipboardPasteOutputProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="ClipboardPasteOutputProvider"/> class.</summary>
    /// <param name="logger">Receives diagnostic output.</param>
    public ClipboardPasteOutputProvider(ILogger<ClipboardPasteOutputProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => OutputProviderNames.ClipboardPaste;

    /// <inheritdoc />
    public async Task OutputAsync(string text)
    {
        // 1. Snapshot the current clipboard text so it can be restored after the paste.
        //    Non-text content (images, files) is not snapshotted and is lost — acceptable for V1.
        var snapshot = await OnUiThreadAsync(SnapshotClipboardText).ConfigureAwait(false);
        if (snapshot is null)
        {
            _logger.LogDebug("Clipboard held no text; previous content will not be restored");
        }

        // 2. Put the dictated text on the clipboard (with retries — the clipboard is a
        //    system-wide resource another app may briefly hold open).
        await SetClipboardTextWithRetryAsync(text).ConfigureAwait(false);

        // 3. Paste into the app that has focus.
        await Task.Delay(PreKeystrokeDelay).ConfigureAwait(false);
        var accepted = KeyboardInjector.SendCtrlV();
        _logger.LogDebug("Ctrl+V sent ({AcceptedEvents}/4 events accepted)", accepted);

        // 4. Restore the snapshot once the target has had time to complete the paste.
        await Task.Delay(PasteCompletionDelay).ConfigureAwait(false);
        if (snapshot is not null)
        {
            await OnUiThreadAsync(() => RestoreClipboardText(snapshot)).ConfigureAwait(false);
        }
    }

    /// <summary>Returns the current clipboard text, or <see langword="null"/> when it holds none.</summary>
    private string? SnapshotClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the clipboard for the pre-paste snapshot");
            return null;
        }
    }

    /// <summary>Sets the clipboard text, retrying <c>CLIPBRD_E_CANT_OPEN</c>-style contention.</summary>
    private async Task SetClipboardTextWithRetryAsync(string text)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await OnUiThreadAsync(() =>
                {
                    Clipboard.SetText(text);
                    return true;
                }).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is COMException or ExternalException && attempt < ClipboardRetryCount)
            {
                _logger.LogDebug(ex, "Clipboard busy on attempt {Attempt}/{Total}; retrying", attempt, ClipboardRetryCount);
                await Task.Delay(ClipboardRetryDelay).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new ProviderException(
                    Name,
                    "Could not access the Windows clipboard — another application may be holding it. Try again.",
                    ex);
            }
        }
    }

    /// <summary>Restores the pre-paste clipboard text; failures are logged, never thrown (the paste already succeeded).</summary>
    private bool RestoreClipboardText(string snapshot)
    {
        try
        {
            Clipboard.SetText(snapshot);
            _logger.LogDebug("Previous clipboard text restored");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not restore the previous clipboard text");
        }

        return true;
    }

    /// <summary>Runs <paramref name="func"/> on the WPF UI thread (clipboard calls require it).</summary>
    private static async Task<T> OnUiThreadAsync<T>(Func<T> func)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return func();
        }

        return await dispatcher.InvokeAsync(func, DispatcherPriority.Normal);
    }
}
