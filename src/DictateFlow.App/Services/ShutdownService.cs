using System.Windows;

namespace DictateFlow.App.Services;

/// <summary>
/// Default <see cref="IShutdownService"/> implementation that shuts down the WPF application,
/// which triggers <see cref="Application.OnExit"/> where the tray icon and host are torn down.
/// </summary>
public sealed class ShutdownService : IShutdownService
{
    /// <inheritdoc />
    public void Shutdown() => Application.Current.Shutdown();
}
