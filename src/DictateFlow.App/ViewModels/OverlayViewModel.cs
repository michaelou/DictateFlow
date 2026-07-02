using CommunityToolkit.Mvvm.ComponentModel;
using DictateFlow.Core.Models;

namespace DictateFlow.App.ViewModels;

/// <summary>
/// View model backing the on-screen dictation overlay: the current
/// <see cref="OverlayState"/>, the live input level while listening, and a short failure
/// summary while in the Error state.
/// </summary>
public partial class OverlayViewModel : ObservableObject
{
    /// <summary>Gets or sets the current overlay state.</summary>
    [ObservableProperty]
    private OverlayState _state = OverlayState.Hidden;

    /// <summary>Gets or sets the normalized (0..1) input level shown by the level indicator.</summary>
    [ObservableProperty]
    private float _level;

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
