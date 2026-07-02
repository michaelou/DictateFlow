namespace DictateFlow.Core.Services;

/// <summary>
/// Identifies the foreground application. The dictation controller calls
/// <see cref="Capture"/> when recording starts (before any DictateFlow UI can steal focus)
/// so the prompt resolver can substitute <c>{{ApplicationName}}</c> with the app the user
/// was dictating into.
/// </summary>
public interface IForegroundAppService
{
    /// <summary>
    /// Gets the process name captured by the most recent <see cref="Capture"/> call
    /// (e.g. <c>OUTLOOK</c>), or an empty string when nothing has been captured or the
    /// foreground window could not be resolved.
    /// </summary>
    string LastCaptured { get; }

    /// <summary>
    /// Captures the current foreground process name and remembers it in
    /// <see cref="LastCaptured"/>. Returns an empty string (never throws) when unavailable.
    /// </summary>
    /// <returns>The captured process name, or an empty string.</returns>
    string Capture();
}
