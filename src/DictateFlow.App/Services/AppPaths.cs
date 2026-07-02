using System.IO;
using DictateFlow.Core.Services;

namespace DictateFlow.App.Services;

/// <summary>
/// Production <see cref="IAppPaths"/> implementation rooted at <c>%APPDATA%\DictateFlow\</c>.
/// </summary>
public sealed class AppPaths : IAppPaths
{
    /// <summary>Initializes a new instance rooted at <c>%APPDATA%\DictateFlow\</c>.</summary>
    public AppPaths()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DictateFlow"))
    {
    }

    /// <summary>Initializes a new instance rooted at an explicit directory (used by tests).</summary>
    /// <param name="rootDirectory">The root application data directory.</param>
    public AppPaths(string rootDirectory)
    {
        RootDirectory = rootDirectory;
    }

    /// <inheritdoc />
    public string RootDirectory { get; }

    /// <inheritdoc />
    public string SettingsFilePath => Path.Combine(RootDirectory, "settings.json");

    /// <inheritdoc />
    public string DatabaseFilePath => Path.Combine(RootDirectory, "dictateflow.db");

    /// <inheritdoc />
    public string LogsDirectory => Path.Combine(RootDirectory, "logs");

    /// <inheritdoc />
    public string PromptsDirectory => Path.Combine(RootDirectory, "Prompts");

    /// <inheritdoc />
    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(PromptsDirectory);
    }
}
