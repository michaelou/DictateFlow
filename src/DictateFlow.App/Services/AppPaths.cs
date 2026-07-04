using System.IO;
using DictateFlow.Core.Services;

namespace DictateFlow.App.Services;

/// <summary>
/// Production <see cref="IAppPaths"/> implementation rooted at <c>%APPDATA%\DictateFlow\</c>,
/// with the large machine-specific engine and model binaries under
/// <c>%LOCALAPPDATA%\DictateFlow\</c> so they never roam.
/// </summary>
public sealed class AppPaths : IAppPaths
{
    /// <summary>Initializes a new instance rooted at <c>%APPDATA%\DictateFlow\</c>.</summary>
    public AppPaths()
        : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DictateFlow"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DictateFlow"))
    {
    }

    /// <summary>Initializes a new instance rooted at an explicit directory (used by tests).</summary>
    /// <param name="rootDirectory">The root application data directory; also hosts the engine and model directories.</param>
    public AppPaths(string rootDirectory)
        : this(rootDirectory, rootDirectory)
    {
    }

    /// <summary>Initializes a new instance with separate roaming and local roots.</summary>
    /// <param name="rootDirectory">The root (roaming) application data directory.</param>
    /// <param name="localDataDirectory">The local (non-roaming) data directory for engines and models.</param>
    public AppPaths(string rootDirectory, string localDataDirectory)
    {
        RootDirectory = rootDirectory;
        _localDataDirectory = localDataDirectory;
    }

    private readonly string _localDataDirectory;

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
    public string CommandsDirectory => Path.Combine(RootDirectory, "Commands");

    /// <inheritdoc />
    public string EnginesDirectory => Path.Combine(_localDataDirectory, "Engines");

    /// <inheritdoc />
    public string ModelsDirectory => Path.Combine(_localDataDirectory, "Models");

    /// <inheritdoc />
    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(PromptsDirectory);
        Directory.CreateDirectory(CommandsDirectory);
        Directory.CreateDirectory(EnginesDirectory);
        Directory.CreateDirectory(ModelsDirectory);
    }
}
