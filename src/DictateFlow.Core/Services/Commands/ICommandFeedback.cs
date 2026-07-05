using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// Receives the two lifecycle points of a voice command that the app surfaces to the user:
/// the moment a command is recognized (before confirmation and execution) and the moment it
/// finishes. The App layer implements this with the overlay's command-executing state and the
/// command sounds (issue #30); Core ships <see cref="NullCommandFeedback"/> as a no-op default
/// so the pipeline works without any UI.
/// </summary>
/// <remarks>
/// Feedback is presentation only and must never influence whether a command runs — the
/// <see cref="VoiceCommandService"/> guards every call so a misbehaving implementation cannot
/// break command handling. <see cref="OnCommandCompleted"/> also fires for the
/// <see cref="CommandOutcomeStatus.Unknown"/> outcome, which has no recognized command and so
/// is never preceded by <see cref="OnCommandRecognized"/>.
/// </remarks>
public interface ICommandFeedback
{
    /// <summary>
    /// Signals that <paramref name="command"/> was matched and is about to be confirmed and
    /// executed. Fires once per matched command, before any confirmation prompt.
    /// </summary>
    /// <param name="command">The command that was recognized.</param>
    void OnCommandRecognized(CommandDefinition command);

    /// <summary>Signals that a detected command reached its terminal <paramref name="outcome"/>.</summary>
    /// <param name="outcome">How the command ended.</param>
    void OnCommandCompleted(CommandOutcome outcome);
}
