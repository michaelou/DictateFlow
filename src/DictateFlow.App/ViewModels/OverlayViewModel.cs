using CommunityToolkit.Mvvm.ComponentModel;
using DictateFlow.Core.Models;

namespace DictateFlow.App.ViewModels;

/// <summary>
/// View model backing the on-screen dictation overlay. M2 uses the
/// <see cref="OverlayState.Listening"/> state with a live input level; later milestones
/// drive the remaining <see cref="OverlayState"/> values.
/// </summary>
public partial class OverlayViewModel : ObservableObject
{
    /// <summary>Gets or sets the current overlay state.</summary>
    [ObservableProperty]
    private OverlayState _state = OverlayState.Hidden;

    /// <summary>Gets or sets the normalized (0..1) input level shown by the level indicator.</summary>
    [ObservableProperty]
    private float _level;
}
