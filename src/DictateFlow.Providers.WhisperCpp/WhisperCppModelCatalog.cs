using DictateFlow.Core.Models;

namespace DictateFlow.Providers.WhisperCpp;

/// <summary>
/// The pinned whisper.cpp downloads: one engine release (Windows x64 CPU build from the
/// GitHub releases) and the supported ggml models (from the ggerganov/whisper.cpp Hugging
/// Face repository). Sizes and SHA-256 checksums are pinned alongside the URLs so every
/// download is verified against a known-good value; bumping the engine version means
/// updating this file.
/// </summary>
public static class WhisperCppModelCatalog
{
    /// <summary>The engine family name; groups files under <c>Engines\Whisper</c> and <c>Models\Whisper</c>.</summary>
    public const string EngineName = "Whisper";

    /// <summary>The pinned whisper.cpp release version.</summary>
    public const string EngineVersion = "1.9.1";

    /// <summary>The executable (within the extracted engine) that runs transcriptions.</summary>
    public const string EngineExecutableName = "whisper-cli.exe";

    /// <summary>Catalog id of the Whisper Small model (the recommended default).</summary>
    public const string SmallModelId = "ggml-small";

    /// <summary>Catalog id of the Whisper Medium model.</summary>
    public const string MediumModelId = "ggml-medium";

    /// <summary>Gets the engine definition: the whisper.cpp Windows x64 CPU binary archive.</summary>
    public static ModelDefinition Engine { get; } = new(
        EngineName,
        "engine",
        $"Whisper.cpp engine {EngineVersion}",
        ModelComponentKind.Engine,
        new Uri($"https://github.com/ggml-org/whisper.cpp/releases/download/v{EngineVersion}/whisper-bin-x64.zip"),
        SizeBytes: 7_982_101,
        Sha256: "7d8be46ecd31828e1eb7a2ecdd0d6b314feafd82163038ab6092594b0a063539");

    /// <summary>Gets the Whisper Small model definition (recommended default: fast, low memory, good multilingual).</summary>
    public static ModelDefinition Small { get; } = new(
        EngineName,
        SmallModelId,
        "Whisper Small",
        ModelComponentKind.Model,
        new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin"),
        SizeBytes: 487_601_967,
        Sha256: "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b");

    /// <summary>Gets the Whisper Medium model definition (better multilingual accuracy, slower and heavier).</summary>
    public static ModelDefinition Medium { get; } = new(
        EngineName,
        MediumModelId,
        "Whisper Medium",
        ModelComponentKind.Model,
        new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin"),
        SizeBytes: 1_533_763_059,
        Sha256: "6c14d5adee5f86394037b4e4e8b59f1673b6cee10e3cf0b11bbdbee79c156208");

    /// <summary>Gets all downloadable components, engine first.</summary>
    public static IReadOnlyList<ModelDefinition> All { get; } = [Engine, Small, Medium];

    /// <summary>Gets the model definitions (without the engine), in recommendation order.</summary>
    public static IReadOnlyList<ModelDefinition> Models { get; } = [Small, Medium];

    /// <summary>Finds a model by its catalog id (case-insensitive), e.g. from provider config.</summary>
    /// <param name="id">The configured model id.</param>
    /// <returns>The matching definition, or <see langword="null"/> when unknown.</returns>
    public static ModelDefinition? FindModel(string id)
        => Models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
}
