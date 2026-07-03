using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DictateFlow.App.ViewModels;

/// <summary>
/// One entry in the tray's "Prompt Mode" submenu. Selecting it makes the mode active;
/// <see cref="IsActive"/> drives the check mark next to the current mode.
/// </summary>
public sealed partial class PromptModeMenuItem : ObservableObject
{
    private readonly Func<string, Task> _select;

    /// <summary>Initializes a new instance of the <see cref="PromptModeMenuItem"/> class.</summary>
    /// <param name="name">The prompt mode name (also the menu header).</param>
    /// <param name="description">Short description shown as the item's tooltip.</param>
    /// <param name="isActive">Whether this is the currently active mode.</param>
    /// <param name="select">Callback that activates this mode by name.</param>
    public PromptModeMenuItem(string name, string description, bool isActive, Func<string, Task> select)
    {
        Name = name;
        Description = description;
        _isActive = isActive;
        _select = select;
    }

    /// <summary>The prompt mode name; bound to the menu item header.</summary>
    public string Name { get; }

    /// <summary>Short description; bound to the menu item tooltip.</summary>
    public string Description { get; }

    /// <summary>Whether this mode is the active one; bound to the menu item check mark.</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>Activates this prompt mode.</summary>
    [RelayCommand]
    private Task Select() => _select(Name);
}
