using DictateFlow.Core.Models;

namespace DictateFlow.Providers.Parakeet;

/// <summary>
/// The pinned Parakeet downloads: the NVIDIA Parakeet TDT 0.6B v3 model (int8 ONNX export
/// from the sherpa-onnx Hugging Face collection) as its four component files. Inference runs
/// in-process through the sherpa-onnx runtime shipped as a NuGet dependency, so unlike
/// whisper.cpp there is no engine download. Sizes and SHA-256 checksums are pinned alongside
/// the URLs so every download is verified against a known-good value; bumping the model
/// version means updating this file.
/// </summary>
public static class ParakeetModelCatalog
{
    /// <summary>The engine family name; groups files under <c>Models\Parakeet</c>.</summary>
    public const string EngineName = "Parakeet";

    /// <summary>The Hugging Face repository the component files are downloaded from.</summary>
    private const string RepoBase = "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main/";

    /// <summary>Display name of the model shown on the Speech settings page.</summary>
    public const string ModelDisplayName = "Parakeet TDT 0.6B v3 (int8)";

    /// <summary>Gets the transducer encoder (the bulk of the model).</summary>
    public static ModelDefinition Encoder { get; } = new(
        EngineName,
        "encoder.int8.onnx",
        "Parakeet TDT v3 encoder",
        ModelComponentKind.Model,
        new Uri(RepoBase + "encoder.int8.onnx"),
        SizeBytes: 652_184_281,
        Sha256: "acfc2b4456377e15d04f0243af540b7fe7c992f8d898d751cf134c3a55fd2247");

    /// <summary>Gets the transducer decoder.</summary>
    public static ModelDefinition Decoder { get; } = new(
        EngineName,
        "decoder.int8.onnx",
        "Parakeet TDT v3 decoder",
        ModelComponentKind.Model,
        new Uri(RepoBase + "decoder.int8.onnx"),
        SizeBytes: 11_845_275,
        Sha256: "179e50c43d1a9de79c8a24149a2f9bac6eb5981823f2a2ed88d655b24248db4e");

    /// <summary>Gets the transducer joiner.</summary>
    public static ModelDefinition Joiner { get; } = new(
        EngineName,
        "joiner.int8.onnx",
        "Parakeet TDT v3 joiner",
        ModelComponentKind.Model,
        new Uri(RepoBase + "joiner.int8.onnx"),
        SizeBytes: 6_355_277,
        Sha256: "3164c13fc2821009440d20fcb5fdc78bff28b4db2f8d0f0b329101719c0948b3");

    /// <summary>Gets the token table.</summary>
    public static ModelDefinition Tokens { get; } = new(
        EngineName,
        "tokens.txt",
        "Parakeet TDT v3 tokens",
        ModelComponentKind.Model,
        new Uri(RepoBase + "tokens.txt"),
        SizeBytes: 93_939,
        Sha256: "d58544679ea4bc6ac563d1f545eb7d474bd6cfa467f0a6e2c1dc1c7d37e3c35d");

    /// <summary>Gets all downloadable components; all four are required for transcription.</summary>
    public static IReadOnlyList<ModelDefinition> All { get; } = [Encoder, Decoder, Joiner, Tokens];
}
