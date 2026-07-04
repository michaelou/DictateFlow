namespace DictateFlow.Core.Models;

/// <summary>How one detected voice command ended.</summary>
public enum CommandOutcomeStatus
{
    /// <summary>The command executed and its action reported success.</summary>
    Executed,

    /// <summary>The command was matched but execution failed (action error, timeout, or unknown action type).</summary>
    Failed,

    /// <summary>The command required confirmation and the user declined (or no confirmation UI was available — deny is the default).</summary>
    Declined,

    /// <summary>The utterance carried the wake phrase but matched no configured command; nothing executed.</summary>
    Unknown,
}

/// <summary>
/// The result of handling one utterance as a voice command, carried on the pipeline result so
/// the UI can show command-specific feedback. Only produced when the utterance was treated as
/// a command — a normal dictation run has none.
/// </summary>
/// <param name="Status">How the command ended.</param>
/// <param name="CommandName">Display name of the matched command; <see langword="null"/> when nothing matched.</param>
/// <param name="Message">User-presentable feedback (confirmation, failure description, or the unknown-command notice).</param>
public sealed record CommandOutcome(CommandOutcomeStatus Status, string? CommandName, string Message);
