using DictateFlow.Core.Services;

namespace DictateFlow.Tests;

/// <summary>
/// <see cref="IAppPaths"/> implementation rooted in a unique temporary directory,
/// deleted when the test disposes it.
/// </summary>
public sealed class TestAppPaths : IAppPaths, IDisposable
{
    /// <summary>Initializes a new instance rooted in a fresh temp directory.</summary>
    public TestAppPaths()
    {
        RootDirectory = Path.Combine(Path.GetTempPath(), "DictateFlowTests", Guid.NewGuid().ToString("N"));
        EnsureCreated();
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
    public string CommandsDirectory => Path.Combine(RootDirectory, "Commands");

    /// <inheritdoc />
    public string EnginesDirectory => Path.Combine(RootDirectory, "Engines");

    /// <inheritdoc />
    public string ModelsDirectory => Path.Combine(RootDirectory, "Models");

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

    /// <summary>Deletes the temporary directory tree.</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort — the OS temp cleaner will pick up leftovers.
        }
    }
}
