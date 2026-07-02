using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Prompts;

/// <summary>
/// Default <see cref="IPromptModeSelector"/> implementation. Reads the
/// <c>ApplicationRules</c> and <c>ActivePromptMode</c> settings on every call, so rule
/// changes apply from the very next dictation without a restart.
/// </summary>
public sealed class PromptModeSelector : IPromptModeSelector
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<PromptModeSelector> _logger;

    /// <summary>Initializes a new instance of the <see cref="PromptModeSelector"/> class.</summary>
    /// <param name="settingsService">Supplies the application rules and the active mode fallback.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public PromptModeSelector(ISettingsService settingsService, ILogger<PromptModeSelector> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string SelectMode(string applicationName)
    {
        var settings = _settingsService.Current;
        var processName = StripExeSuffix(applicationName);

        if (!string.IsNullOrWhiteSpace(processName))
        {
            foreach (var rule in settings.ApplicationRules)
            {
                if (string.Equals(StripExeSuffix(rule.ProcessName), processName, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(rule.PromptMode))
                {
                    _logger.LogDebug(
                        "Prompt mode '{PromptMode}' selected by application rule '{ProcessName}' for foreground app '{ApplicationName}'",
                        rule.PromptMode, rule.ProcessName, applicationName);
                    return rule.PromptMode;
                }
            }
        }

        _logger.LogDebug(
            "No application rule matched foreground app '{ApplicationName}'; using active prompt mode '{PromptMode}'",
            applicationName, settings.ActivePromptMode);
        return settings.ActivePromptMode;
    }

    /// <summary>Trims whitespace and a trailing <c>.exe</c> so both rule and capture shapes match.</summary>
    private static string StripExeSuffix(string name)
    {
        var trimmed = name.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? trimmed[..^4] : trimmed;
    }
}
