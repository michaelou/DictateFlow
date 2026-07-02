namespace DictateFlow.App.Services;

/// <summary>
/// Opens application windows, enforcing single-instance behavior where required.
/// </summary>
public interface IWindowService
{
    /// <summary>
    /// Shows the Settings window. If it is already open, the existing instance is
    /// restored and focused instead of creating a second one.
    /// </summary>
    void ShowSettingsWindow();
}
