namespace DictateFlow.Core.Services.Audio;

/// <summary>
/// Payload of <see cref="IDictationController.DictationFailed"/>: the user-presentable
/// failure message plus whether configuration is at fault (so notifications can offer an
/// "Open Settings" shortcut).
/// </summary>
public sealed class DictationFailedEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="DictationFailedEventArgs"/> class.</summary>
    /// <param name="message">User-presentable failure description.</param>
    /// <param name="isConfigurationError">Whether the failure is configuration-caused.</param>
    public DictationFailedEventArgs(string message, bool isConfigurationError)
    {
        Message = message;
        IsConfigurationError = isConfigurationError;
    }

    /// <summary>Gets the user-presentable failure description (never raw exception text).</summary>
    public string Message { get; }

    /// <summary>Gets a value indicating whether the failure is configuration-caused — the UI should offer a shortcut to Settings.</summary>
    public bool IsConfigurationError { get; }
}
