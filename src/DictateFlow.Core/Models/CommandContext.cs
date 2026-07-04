namespace DictateFlow.Core.Models;

/// <summary>
/// Everything a command action receives for one execution. The only free-text field is
/// <see cref="Argument"/> — action implementations treat it as data (a note body, a time
/// phrase), never as something to execute.
/// </summary>
/// <param name="CommandName">Display name of the matched command.</param>
/// <param name="ActionType">The action type name the command resolved to.</param>
/// <param name="ActionValue">The configured action payload from the command definition (e.g. <c>notepad.exe</c>).</param>
/// <param name="Argument">
/// The utterance remainder after the matched phrase, verbatim (e.g. <c>in 10 minutes to call
/// Marko</c> after <c>remind me</c>); empty when the phrase matched the whole utterance.
/// </param>
/// <param name="Transcript">The full raw transcript the command was detected in, for logging and diagnostics.</param>
/// <param name="TimestampUtc">When execution started, in UTC.</param>
public sealed record CommandContext(
    string CommandName,
    string ActionType,
    string ActionValue,
    string Argument,
    string Transcript,
    DateTime TimestampUtc);
