namespace DictateFlow.Core.Services.Transfer;

/// <summary>
/// Exports the prompts folder as a <c>.zip</c> and imports such archives back. Only
/// top-level <c>*.json</c> entries are considered; entry paths are flattened to bare file
/// names on import so a crafted archive can never write outside the prompts folder.
/// </summary>
public interface IPromptsArchive
{
    /// <summary>Writes all prompt files into a new zip archive, replacing an existing file.</summary>
    /// <param name="zipFilePath">The destination archive path.</param>
    /// <returns>The number of prompt files written.</returns>
    int ExportZip(string zipFilePath);

    /// <summary>Lists the archive entries that would overwrite an existing prompt file.</summary>
    /// <param name="zipFilePath">The archive to inspect.</param>
    IReadOnlyList<string> GetConflictingFiles(string zipFilePath);

    /// <summary>Extracts the archive's prompt files into the prompts folder.</summary>
    /// <param name="zipFilePath">The archive to import.</param>
    /// <param name="overwriteExisting">
    /// When <see langword="false"/>, entries whose target file already exists are skipped.
    /// </param>
    /// <returns>The number of files written.</returns>
    int ImportZip(string zipFilePath, bool overwriteExisting);
}
