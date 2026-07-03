using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Models;

/// <summary>
/// Downloads, verifies and manages local speech engines and their models under the
/// application's local data directories (<c>Engines\</c> and <c>Models\</c>). One
/// implementation exists per local engine (whisper.cpp today; Parakeet, Voxtral, ONNX
/// runtimes can add their own without touching the pipeline). All downloads are verified
/// (size + SHA-256) before they are registered; corrupted files are rejected and removed.
/// </summary>
public interface IModelManager
{
    /// <summary>Gets every component this manager can install, engines first, in catalog order.</summary>
    IReadOnlyList<ModelDefinition> AvailableComponents { get; }

    /// <summary>Enumerates the components that are currently installed.</summary>
    /// <param name="cancellationToken">Cancels the scan.</param>
    Task<IReadOnlyList<InstalledModel>> GetInstalledModelsAsync(CancellationToken cancellationToken);

    /// <summary>Checks whether a component is installed (fast: existence + size, no hashing).</summary>
    /// <param name="model">The component to check.</param>
    bool IsInstalled(ModelDefinition model);

    /// <summary>
    /// Downloads, verifies and installs one component. The finished installation is effective
    /// immediately — no restart is required. A failed verification removes the download and
    /// throws.
    /// </summary>
    /// <param name="model">The component to install.</param>
    /// <param name="progress">Receives the download progress as a fraction (0–1).</param>
    /// <param name="cancellationToken">Cancels the download; partial files are removed.</param>
    /// <exception cref="ProviderException">The download failed or the file did not verify.</exception>
    Task DownloadAsync(ModelDefinition model, IProgress<double> progress, CancellationToken cancellationToken);

    /// <summary>Removes an installed component from disk.</summary>
    /// <param name="model">The component to remove.</param>
    Task DeleteAsync(ModelDefinition model);

    /// <summary>
    /// Re-verifies an installed component against its expected size and SHA-256 checksum.
    /// </summary>
    /// <param name="model">The component to verify.</param>
    /// <param name="cancellationToken">Cancels the hashing.</param>
    /// <returns><see langword="true"/> when the installed file matches the catalog definition.</returns>
    Task<bool> VerifyAsync(ModelDefinition model, CancellationToken cancellationToken);
}
