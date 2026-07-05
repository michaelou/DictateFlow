using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// CRUD over the user-defined command files (one <c>{Name}.json</c> per command in the commands
/// directory). Mirrors <c>IPromptModeStore</c>: it is the editing seam the settings UI drives,
/// separate from the read-only <see cref="ICommandDefinitionSource"/> the matcher consumes (the
/// same <c>CommandStore</c> plays both roles). Only user commands are editable — the built-in
/// command sets are shipped as code and never touched here.
/// </summary>
public interface IVoiceCommandStore
{
    /// <summary>Gets the user commands that currently load successfully, in file-name order.</summary>
    IReadOnlyList<CommandDefinition> GetUserCommands();

    /// <summary>Re-scans the commands directory so external edits take effect without a restart.</summary>
    void Reload();

    /// <summary>
    /// Writes the command to <c>{Name}.json</c> (creating or overwriting it) using the nested-action
    /// on-disk schema, then refreshes the loaded commands.
    /// </summary>
    /// <param name="command">The command to persist.</param>
    /// <exception cref="ArgumentException">The name is not a valid file name, or the action configuration is rejected.</exception>
    void Save(CommandDefinition command);

    /// <summary>
    /// Deletes the command file whose name matches case-insensitively and refreshes the loaded
    /// commands; does nothing when no such file exists.
    /// </summary>
    /// <param name="name">The command name (and file name, without extension) to delete.</param>
    void Delete(string name);
}
