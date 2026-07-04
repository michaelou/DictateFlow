namespace DictateFlow.Core.Models;

/// <summary>
/// Outcome of one command action execution. <see cref="Message"/> is always user-presentable:
/// the confirmation shown on success (e.g. <c>Opening Notepad</c>) or the curated failure
/// description — raw exception text stays in the log.
/// </summary>
/// <param name="Success">Whether the action completed.</param>
/// <param name="Message">User-presentable confirmation or failure description.</param>
public sealed record CommandResult(bool Success, string Message)
{
    /// <summary>Creates a success result carrying the user-facing confirmation message.</summary>
    /// <param name="message">The confirmation shown to the user (e.g. <c>Opening Notepad</c>).</param>
    public static CommandResult Ok(string message) => new(true, message);

    /// <summary>Creates a failure result carrying the user-facing error description.</summary>
    /// <param name="message">The curated failure description shown to the user.</param>
    public static CommandResult Fail(string message) => new(false, message);
}
