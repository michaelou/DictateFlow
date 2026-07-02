using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.ViewModels;

/// <summary>
/// View model backing the transcript window that shows the result of a dictation.
/// Copying is manual (Copy button) — automatic paste into the target app arrives in M5.
/// </summary>
public partial class TranscriptViewModel : ObservableObject
{
    private readonly ILogger<TranscriptViewModel> _logger;

    /// <summary>Initializes a new instance of the <see cref="TranscriptViewModel"/> class.</summary>
    /// <param name="logger">Receives diagnostic output.</param>
    public TranscriptViewModel(ILogger<TranscriptViewModel> logger)
    {
        _logger = logger;
    }

    /// <summary>Gets or sets the transcript text shown in the window.</summary>
    [ObservableProperty]
    private string _text = "";

    /// <summary>Gets or sets the status line under the transcript (e.g. copy confirmation).</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Raised when the window hosting this view model should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Copies the transcript to the clipboard.</summary>
    [RelayCommand]
    private void Copy()
    {
        try
        {
            Clipboard.SetText(Text);
            StatusMessage = "Copied to clipboard.";
        }
        catch (Exception ex)
        {
            // The clipboard can be locked by another process; never crash over it.
            _logger.LogWarning(ex, "Failed to copy transcript to clipboard");
            StatusMessage = "Could not access the clipboard — try again.";
        }
    }

    /// <summary>Closes the transcript window.</summary>
    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
