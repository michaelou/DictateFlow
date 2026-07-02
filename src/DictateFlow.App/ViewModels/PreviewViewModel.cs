using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DictateFlow.App.ViewModels;

/// <summary>How the user closed the preview dialog.</summary>
public enum PreviewOutcome
{
    /// <summary>The dialog was cancelled (button or window close); nothing is delivered or saved.</summary>
    Cancel,

    /// <summary>The (possibly edited) text should be pasted into the original target window.</summary>
    Paste,

    /// <summary>The (possibly edited) text was copied to the clipboard; no paste, no history entry.</summary>
    CopyOnly,
}

/// <summary>
/// View model backing the preview dialog shown when <c>Output.Mode</c> is <c>Preview</c>:
/// an editable copy of the enhanced text, the raw transcript for reference, and the
/// Paste / Copy only / Cancel choice surfaced through <see cref="Outcome"/>.
/// </summary>
public partial class PreviewViewModel : ObservableObject
{
    /// <summary>Gets or sets the editable text that will be delivered on Paste.</summary>
    [ObservableProperty]
    private string _text = "";

    /// <summary>Gets or sets the raw transcript shown in the collapsed reference expander.</summary>
    [ObservableProperty]
    private string _rawTranscript = "";

    /// <summary>Gets or sets the warning banner text (LLM fallback); <see langword="null"/> hides the banner.</summary>
    [ObservableProperty]
    private string? _warning;

    /// <summary>Gets or sets the status line shown next to the buttons (e.g. a clipboard error).</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Gets how the dialog was closed; <see cref="PreviewOutcome.Cancel"/> until a button decides otherwise.</summary>
    public PreviewOutcome Outcome { get; private set; } = PreviewOutcome.Cancel;

    /// <summary>Raised when the window hosting this view model should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Confirms the edited text for delivery into the original target window.</summary>
    [RelayCommand]
    private void Paste()
    {
        Outcome = PreviewOutcome.Paste;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Copies the edited text to the clipboard without pasting it anywhere.</summary>
    [RelayCommand]
    private void CopyOnly()
    {
        try
        {
            Clipboard.SetText(Text);
        }
        catch (Exception)
        {
            // The clipboard can be locked by another process; keep the dialog open so the user can retry.
            StatusMessage = "Could not access the clipboard — try again.";
            return;
        }

        Outcome = PreviewOutcome.CopyOnly;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Discards the dictation.</summary>
    [RelayCommand]
    private void Cancel()
    {
        Outcome = PreviewOutcome.Cancel;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
