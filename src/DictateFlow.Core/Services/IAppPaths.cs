namespace DictateFlow.Core.Services;

/// <summary>
/// Resolves the file-system locations where DictateFlow stores user data.
/// The production implementation roots everything under <c>%APPDATA%\DictateFlow\</c>;
/// tests substitute an implementation rooted in a temporary directory.
/// </summary>
public interface IAppPaths
{
    /// <summary>Gets the root application data directory.</summary>
    string RootDirectory { get; }

    /// <summary>Gets the full path of the <c>settings.json</c> file.</summary>
    string SettingsFilePath { get; }

    /// <summary>Gets the full path of the <c>dictateflow.db</c> SQLite database file.</summary>
    string DatabaseFilePath { get; }

    /// <summary>Gets the directory that receives rolling log files.</summary>
    string LogsDirectory { get; }

    /// <summary>Gets the directory that holds prompt definition files (populated in M4).</summary>
    string PromptsDirectory { get; }

    /// <summary>
    /// Gets the directory that holds local inference engines (e.g. whisper.cpp), one
    /// subdirectory per engine. Rooted under local (non-roaming) application data in
    /// production — engine binaries are large and machine-specific.
    /// </summary>
    string EnginesDirectory { get; }

    /// <summary>
    /// Gets the directory that holds local model files, one subdirectory per engine.
    /// Rooted under local (non-roaming) application data in production.
    /// </summary>
    string ModelsDirectory { get; }

    /// <summary>Creates the full application data directory tree if any part of it is missing.</summary>
    void EnsureCreated();
}
