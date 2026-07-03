namespace DictateFlow.Core.Services.Validation;

/// <summary>How severe a settings validation finding is.</summary>
public enum SettingsValidationSeverity
{
    /// <summary>The setting is unusable; saving it must be blocked.</summary>
    Error,

    /// <summary>The setting is suspicious but usable; it saves with a visible note.</summary>
    Warning,
}

/// <summary>One settings validation finding.</summary>
/// <param name="Section">The settings page/section the finding belongs to (e.g. <c>General</c>, <c>LLM</c>).</param>
/// <param name="Message">The human-readable problem description, naming the offending field.</param>
/// <param name="Severity">Whether the finding blocks saving or is informational.</param>
public sealed record SettingsValidationError(string Section, string Message, SettingsValidationSeverity Severity);
