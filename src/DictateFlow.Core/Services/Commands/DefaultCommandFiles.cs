namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// The example command files seeded into the commands directory on first run (only when it
/// contains no <c>.json</c> files, so user edits and deletions are never overwritten). They
/// give users copy-ready templates for each launch action type — mirroring how the prompt modes
/// seed the prompts directory.
/// </summary>
public static class DefaultCommandFiles
{
    /// <summary>Gets the seed files, keyed by the file name (without extension) to write them under.</summary>
    public static IReadOnlyList<(string FileName, CommandDefinitionFile Command)> All { get; } =
    [
        ("Open Notepad", new CommandDefinitionFile
        {
            Name = "Open Notepad",
            Enabled = true,
            Phrases = ["open notepad"],
            Action = new CommandActionFile { Type = ProcessStartAction.RegistrationName, Value = "notepad.exe" },
            RequiresConfirmation = false,
        }),
        ("Open Downloads", new CommandDefinitionFile
        {
            Name = "Open Downloads",
            Enabled = true,
            Phrases = ["open downloads"],
            Action = new CommandActionFile { Type = OpenFolderAction.RegistrationName, Value = "Downloads" },
            RequiresConfirmation = false,
        }),
        ("Search Google", new CommandDefinitionFile
        {
            Name = "Search Google",
            Enabled = true,
            Phrases = ["search for", "google"],
            Action = new CommandActionFile
            {
                Type = OpenUrlAction.RegistrationName,
                Value = $"https://www.google.com/search?q={CommandArgumentPlaceholder.Token}",
            },
            RequiresConfirmation = false,
        }),
    ];
}
