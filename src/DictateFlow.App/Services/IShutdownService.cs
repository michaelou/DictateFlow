namespace DictateFlow.App.Services;

/// <summary>
/// Requests a clean application shutdown (tray icon disposal, host stop and log flush
/// are performed by the application exit pipeline).
/// </summary>
public interface IShutdownService
{
    /// <summary>Initiates application shutdown.</summary>
    void Shutdown();
}
