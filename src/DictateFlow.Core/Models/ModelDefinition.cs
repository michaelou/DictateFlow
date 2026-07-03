namespace DictateFlow.Core.Models;

/// <summary>What kind of local component a <see cref="ModelDefinition"/> describes.</summary>
public enum ModelComponentKind
{
    /// <summary>An inference engine (executable plus support files), e.g. whisper.cpp.</summary>
    Engine,

    /// <summary>A model weights file consumed by an engine, e.g. <c>ggml-small.bin</c>.</summary>
    Model,
}

/// <summary>
/// One downloadable local speech component — an engine or a model — as published by its
/// provider's catalog. Definitions are static data (URL, size, checksum); installation state
/// lives with <c>IModelManager</c>.
/// </summary>
/// <param name="EngineName">The engine family the component belongs to (e.g. <c>Whisper</c>); groups files on disk.</param>
/// <param name="Id">Stable identifier unique within the engine (e.g. <c>ggml-small</c>), also used in provider config.</param>
/// <param name="DisplayName">Name shown in the Local Models settings page.</param>
/// <param name="Kind">Whether this is the engine itself or a model file.</param>
/// <param name="DownloadUri">Where the file is downloaded from.</param>
/// <param name="SizeBytes">Exact size of the download in bytes; part of verification.</param>
/// <param name="Sha256">Lowercase hex SHA-256 of the download; part of verification.</param>
public sealed record ModelDefinition(
    string EngineName,
    string Id,
    string DisplayName,
    ModelComponentKind Kind,
    Uri DownloadUri,
    long SizeBytes,
    string Sha256);

/// <summary>An installed local component discovered by <c>IModelManager</c>.</summary>
/// <param name="Definition">The catalog definition the installation matches.</param>
/// <param name="InstallPath">The installed file (model) or directory (engine) on disk.</param>
/// <param name="SizeBytes">Size on disk of the installed model file, or the download size for engines.</param>
public sealed record InstalledModel(
    ModelDefinition Definition,
    string InstallPath,
    long SizeBytes);
