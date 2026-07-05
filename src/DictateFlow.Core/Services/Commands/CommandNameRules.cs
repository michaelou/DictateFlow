namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// Validates user command names. A command name doubles as its JSON filename
/// (<c>{Name}.json</c>), so it must be a safe Windows filename. Shared by the store and the
/// editor view model so the two can never disagree — mirrors <c>PromptModeNameRules</c>.
/// </summary>
public static class CommandNameRules
{
    /// <summary>Longest accepted name; keeps the path well under Windows limits.</summary>
    public const int MaxLength = 100;

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Validates a command name (as typed, before trimming).</summary>
    /// <param name="name">The candidate command name.</param>
    /// <returns>A user-presentable error message, or <see langword="null"/> when the name is valid.</returns>
    public static string? Validate(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return "Name is required.";
        }

        if (trimmed.Length > MaxLength)
        {
            return $"Name must be at most {MaxLength} characters.";
        }

        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "Name contains characters that are not allowed in a file name.";
        }

        // Windows silently strips trailing dots; leading dots make hidden-looking files.
        if (trimmed.StartsWith('.') || trimmed.EndsWith('.'))
        {
            return "Name cannot start or end with a dot.";
        }

        if (ReservedDeviceNames.Contains(trimmed))
        {
            return $"'{trimmed}' is a reserved Windows device name.";
        }

        return null;
    }
}
