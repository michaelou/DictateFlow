namespace DictateFlow.App.Services;

/// <summary>
/// Surfaces dictation pipeline failures as tray notifications. Successful dictations end in
/// the target application (M5 output pipeline), so there is nothing to present on success.
/// Subscribes to the dictation controller when constructed; materialize it once at startup.
/// </summary>
public interface IDictationFailureNotifier : IDisposable
{
}
