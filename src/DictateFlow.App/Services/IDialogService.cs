namespace DictateFlow.App.Services;

/// <summary>
/// File pickers and confirmation prompts, abstracted so view models that import/export
/// files stay testable without a desktop session.
/// </summary>
public interface IDialogService
{
    /// <summary>Shows a save-file dialog.</summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="filter">A <c>Description|*.ext</c> file filter.</param>
    /// <param name="defaultFileName">The pre-filled file name.</param>
    /// <returns>The chosen path, or <see langword="null"/> when cancelled.</returns>
    string? ShowSaveFile(string title, string filter, string defaultFileName);

    /// <summary>Shows an open-file dialog.</summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="filter">A <c>Description|*.ext</c> file filter.</param>
    /// <returns>The chosen path, or <see langword="null"/> when cancelled.</returns>
    string? ShowOpenFile(string title, string filter);

    /// <summary>Asks a yes/no question; No is the default answer.</summary>
    /// <param name="title">The prompt title.</param>
    /// <param name="message">The question text.</param>
    /// <returns><see langword="true"/> when the user chose Yes.</returns>
    bool Confirm(string title, string message);

    /// <summary>Asks a yes/no/cancel question; Cancel is the default answer.</summary>
    /// <param name="title">The prompt title.</param>
    /// <param name="message">The question text.</param>
    /// <returns><see langword="true"/> for Yes, <see langword="false"/> for No, <see langword="null"/> for Cancel.</returns>
    bool? ConfirmYesNoCancel(string title, string message);
}
