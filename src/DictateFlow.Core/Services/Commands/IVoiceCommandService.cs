using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// The voice command branch of the dictation pipeline: detection → matching → confirmation →
/// execution. Runs on the raw transcript before any LLM enhancement, so no AI-generated text
/// can ever become a command. Implementations never throw — every problem becomes a
/// <see cref="CommandOutcome"/>.
/// </summary>
public interface IVoiceCommandService
{
    /// <summary>Handles <paramref name="transcript"/> as a voice command, when it is one.</summary>
    /// <param name="transcript">The raw transcript of the utterance.</param>
    /// <param name="cancellationToken">Cancels detection and execution; the command timeout applies on top.</param>
    /// <returns>
    /// The command outcome, or <see langword="null"/> when the utterance is normal dictation
    /// (voice commands disabled, wake phrase not spoken, or — with the wake phrase disabled —
    /// no command phrase matched) and the pipeline should continue unchanged.
    /// </returns>
    Task<CommandOutcome?> TryHandleAsync(string transcript, CancellationToken cancellationToken);
}
