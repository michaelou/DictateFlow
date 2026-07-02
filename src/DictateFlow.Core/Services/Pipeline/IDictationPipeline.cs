using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Pipeline;

/// <summary>
/// The formal dictation pipeline: everything that happens after a capture completes.
/// Implementations never throw — every failure becomes a failed <see cref="PipelineResult"/>.
/// </summary>
public interface IDictationPipeline
{
    /// <summary>Runs transcription → prompt resolution → LLM → history → output.</summary>
    /// <param name="request">The completed capture plus the target-application context.</param>
    /// <param name="cancellationToken">Cancels the in-flight provider calls.</param>
    /// <returns>The outcome; see <see cref="PipelineResult"/> for the success/cancel/failure shapes.</returns>
    Task<PipelineResult> RunAsync(PipelineRequest request, CancellationToken cancellationToken);
}
