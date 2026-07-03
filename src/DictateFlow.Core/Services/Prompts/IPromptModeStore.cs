using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Prompts;

/// <summary>
/// Loads <see cref="PromptMode"/> definitions from the JSON files in the prompts directory
/// (one file per mode). Seeds the default modes on first run and tolerates malformed files.
/// </summary>
public interface IPromptModeStore
{
    /// <summary>Gets all successfully loaded prompt modes, ordered by name.</summary>
    IReadOnlyList<PromptMode> GetAll();

    /// <summary>Finds a mode by name (case-insensitive), or <see langword="null"/> when absent.</summary>
    /// <param name="name">The mode name to look up.</param>
    PromptMode? GetByName(string name);

    /// <summary>Re-scans the prompts directory so external edits take effect without a restart.</summary>
    void Reload();

    /// <summary>
    /// Writes the mode to <c>{Name}.json</c> (creating or overwriting it) and refreshes the
    /// loaded modes.
    /// </summary>
    /// <param name="mode">The mode to persist.</param>
    /// <exception cref="ArgumentException">The mode name is not a valid file name.</exception>
    void Save(PromptMode mode);

    /// <summary>
    /// Deletes the mode file whose name matches case-insensitively and refreshes the loaded
    /// modes; does nothing when no such file exists.
    /// </summary>
    /// <param name="name">The mode name to delete.</param>
    void Delete(string name);
}
