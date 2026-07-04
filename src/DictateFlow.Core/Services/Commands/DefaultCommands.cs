using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// The small set of built-in launch commands shipped as code (not user JSON), so common
/// launches work out of the box. Deliberately minimal — users extend the set by dropping JSON
/// files into the commands directory (seeded with matching examples on first run).
/// </summary>
public static class DefaultCommands
{
    /// <summary>Gets all built-in command definitions.</summary>
    public static IReadOnlyList<CommandDefinition> All { get; } =
    [
        new CommandDefinition
        {
            Name = "Open Notepad",
            Phrases = ["open notepad"],
            ActionType = ProcessStartAction.RegistrationName,
            ActionValue = "notepad.exe",
        },
        new CommandDefinition
        {
            Name = "Open Downloads",
            Phrases = ["open downloads", "open my downloads"],
            ActionType = OpenFolderAction.RegistrationName,
            ActionValue = "Downloads",
        },
        new CommandDefinition
        {
            Name = "Search the web",
            Phrases = ["search for", "search the web for"],
            ActionType = OpenUrlAction.RegistrationName,
            ActionValue = $"https://www.google.com/search?q={CommandArgumentPlaceholder.Token}",
        },
    ];
}

/// <summary>Exposes the built-in <see cref="DefaultCommands"/> to the command matcher.</summary>
public sealed class BuiltInCommandDefinitionSource : ICommandDefinitionSource
{
    /// <inheritdoc />
    public IReadOnlyList<CommandDefinition> GetDefinitions() => DefaultCommands.All;
}
