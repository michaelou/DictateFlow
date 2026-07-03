namespace DictateFlow.Core.Services.Prompts;

/// <summary>
/// Picks the prompt mode for a dictation from the per-application rules in settings,
/// falling back to the active mode when no rule matches.
/// </summary>
public interface IPromptModeSelector
{
    /// <summary>
    /// Selects the prompt-mode name for a dictation targeting <paramref name="applicationName"/>.
    /// Rules are matched case-insensitively against the process name (a trailing <c>.exe</c>
    /// on either side is ignored); the first match wins, and no match returns the
    /// <c>ActivePromptMode</c> setting.
    /// </summary>
    /// <param name="applicationName">The foreground process name captured at record-start (may be empty).</param>
    /// <returns>The name of the prompt mode to use.</returns>
    string SelectMode(string applicationName);
}
