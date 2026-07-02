namespace DictateFlow.App.Services;

/// <summary>
/// Presents dictation results to the user: shows the transcript window on success and a
/// tray notification on provider failure. Subscribes to the dictation controller when
/// constructed; materialize it once at startup.
/// </summary>
public interface IDictationResultPresenter : IDisposable
{
}
