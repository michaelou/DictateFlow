using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Pipeline;

/// <summary>
/// Confirmation step between LLM enhancement and output delivery. The App implements it
/// with the preview dialog when <c>Output.Mode</c> is <c>Preview</c> and as a pass-through
/// for <c>Automatic</c> mode; the pipeline itself stays UI-free.
/// </summary>
public interface IOutputGate
{
    /// <summary>
    /// Offers the draft result for confirmation before it is written to history and output.
    /// On a draft, <see cref="PipelineResult.ErrorMessage"/> carries the enhancement-fallback
    /// warning when the LLM failed and <see cref="PipelineResult.FinalText"/> is the raw transcript.
    /// </summary>
    /// <param name="draft">The draft result to confirm.</param>
    /// <returns>The (possibly user-edited) text to deliver, or <see langword="null"/> to cancel.</returns>
    Task<string?> ConfirmAsync(PipelineResult draft);
}
