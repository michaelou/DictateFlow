using CommunityToolkit.Mvvm.ComponentModel;
using DictateFlow.Core.Models;

namespace DictateFlow.App.ViewModels;

/// <summary>
/// View model backing the on-screen dictation overlay: the current
/// <see cref="OverlayState"/>, the live input level and partial transcript while listening,
/// and a short failure summary while in the Error state.
/// </summary>
public partial class OverlayViewModel : ObservableObject
{
    /// <summary>How many characters of the partial transcript the overlay shows (the most recent tail).</summary>
    private const int TranscriptDisplayLength = 160;

    /// <summary>Gets or sets the current overlay state.</summary>
    [ObservableProperty]
    private OverlayState _state = OverlayState.Hidden;

    /// <summary>Gets or sets the normalized (0..1) input level shown by the level indicator.</summary>
    [ObservableProperty]
    private float _level;

    /// <summary>
    /// Gets or sets the name of the prompt mode selected for the current dictation, shown above
    /// the status line while listening and processing; empty when there is no mode to show.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPromptMode))]
    private string _promptMode = "";

    /// <summary>Gets a value indicating whether there is a prompt mode to show.</summary>
    public bool HasPromptMode => PromptMode.Length > 0;

    /// <summary>
    /// Gets or sets the partial transcript received from streaming transcription; empty when
    /// streaming is not active.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PartialTranscriptDisplay))]
    [NotifyPropertyChangedFor(nameof(HasPartialTranscript))]
    private string _partialTranscript = "";

    /// <summary>Gets a value indicating whether there is a partial transcript to show.</summary>
    public bool HasPartialTranscript => PartialTranscript.Length > 0;

    /// <summary>
    /// Gets the transcript text the overlay shows: the most recent tail, so the words being
    /// spoken right now stay visible while the overlay keeps its compact size.
    /// </summary>
    public string PartialTranscriptDisplay
        => PartialTranscript.Length <= TranscriptDisplayLength
            ? PartialTranscript
            : $"…{PartialTranscript[^TranscriptDisplayLength..].TrimStart()}";

    /// <summary>
    /// Gets or sets the short failure summary shown in the Error state;
    /// <see langword="null"/> falls back to the generic "Dictation failed" text.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErrorDisplayText))]
    private string? _errorMessage;

    /// <summary>Gets the text the overlay shows in the Error state.</summary>
    public string ErrorDisplayText
        => ErrorMessage is null ? "⚠️ Dictation failed" : $"⚠️ {ErrorMessage}";
}
