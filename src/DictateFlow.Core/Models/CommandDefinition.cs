namespace DictateFlow.Core.Models;

/// <summary>
/// One executable voice command: the spoken phrases that trigger it and the action it runs.
/// Definitions come exclusively from <c>ICommandDefinitionSource</c>s — built-in commands
/// registered in code and user commands loaded from JSON files (issue #27). Command matching
/// only ever selects from these definitions; free transcript text can never become one.
/// </summary>
public sealed class CommandDefinition
{
    /// <summary>Gets or sets the display name shown in confirmations and feedback (e.g. <c>Open Notepad</c>).</summary>
    public string Name { get; set; } = "";

    /// <summary>Gets or sets a value indicating whether the command participates in matching.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the spoken phrases that trigger the command (e.g. <c>open notepad</c>).
    /// Matching is case-insensitive and ignores punctuation; a phrase may also match as a
    /// prefix, in which case the rest of the utterance becomes the command argument.
    /// </summary>
    public List<string> Phrases { get; set; } = [];

    /// <summary>
    /// Gets or sets the action type name, matching a type registered through the command
    /// action registration extensions (case-insensitive, e.g. <c>ProcessStart</c>). An
    /// unknown type is rejected — it never executes.
    /// </summary>
    public string ActionType { get; set; } = "";

    /// <summary>
    /// Gets or sets the configured action payload (e.g. <c>notepad.exe</c> for
    /// <c>ProcessStart</c>); its meaning belongs to the action type. Empty when the action
    /// needs no configured value.
    /// </summary>
    public string ActionValue { get; set; } = "";

    /// <summary>
    /// Gets or sets a value indicating whether the user must approve execution in a
    /// confirmation dialog. Independent of the global require-confirmation setting, which
    /// forces confirmation for every command.
    /// </summary>
    public bool RequiresConfirmation { get; set; }
}
