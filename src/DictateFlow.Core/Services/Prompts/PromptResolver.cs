using System.Text.RegularExpressions;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Providers;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Prompts;

/// <summary>
/// Default <see cref="IPromptResolver"/> implementation. Replaces the <c>{{Variable}}</c>
/// tokens (case-insensitive) in the selected mode's system prompt:
/// <c>{{Transcript}}</c>, <c>{{ApplicationName}}</c>, <c>{{Mode}}</c>, <c>{{CurrentDate}}</c>,
/// <c>{{TechnicalDictionary}}</c> and <c>{{ReplacementDictionary}}</c>. Unknown tokens are left
/// as-is with a warning; an unknown mode falls back to <c>Raw</c>.
/// </summary>
public sealed partial class PromptResolver : IPromptResolver
{
    private readonly IPromptModeStore _modeStore;
    private readonly ISettingsService _settingsService;
    private readonly IProviderConfigReader _configReader;
    private readonly IForegroundAppService _foregroundAppService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PromptResolver> _logger;

    /// <summary>Initializes a new instance of the <see cref="PromptResolver"/> class.</summary>
    /// <param name="modeStore">Supplies the prompt modes.</param>
    /// <param name="settingsService">Supplies the technical dictionary and the active LLM provider name.</param>
    /// <param name="configReader">Supplies the active LLM provider's sampling defaults.</param>
    /// <param name="foregroundAppService">Supplies the application name captured at record-start.</param>
    /// <param name="timeProvider">Supplies the current date (replaceable in tests).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public PromptResolver(
        IPromptModeStore modeStore,
        ISettingsService settingsService,
        IProviderConfigReader configReader,
        IForegroundAppService foregroundAppService,
        TimeProvider timeProvider,
        ILogger<PromptResolver> logger)
    {
        _modeStore = modeStore;
        _settingsService = settingsService;
        _configReader = configReader;
        _foregroundAppService = foregroundAppService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public PromptContext Resolve(string transcript, string modeName)
    {
        var mode = _modeStore.GetByName(modeName);
        if (mode is null)
        {
            _logger.LogWarning("Prompt mode '{ModeName}' not found; falling back to '{Fallback}'",
                modeName, DefaultPromptModes.RawModeName);
            // The built-in Raw definition covers a prompts folder that lost its Raw.json.
            mode = _modeStore.GetByName(DefaultPromptModes.RawModeName) ?? DefaultPromptModes.Raw;
        }

        // A mode that skips the LLM needs no resolved prompt or sampling parameters.
        if (!mode.LlmEnabled)
        {
            _logger.LogDebug("Prompt mode '{ModeName}' has the LLM disabled; skipping prompt resolution", mode.Name);
            return new PromptContext("", transcript, Temperature: 0, MaxTokens: 0, mode.Name, LlmEnabled: false);
        }

        var settings = _settingsService.Current;
        var systemPrompt = TokenRegex().Replace(mode.SystemPrompt, match => ResolveToken(match, transcript, mode.Name, settings));

        // Sampling defaults live in the active LLM provider's config section; sections
        // without them (e.g. the mock's) yield the built-in defaults.
        var samplingDefaults = _configReader.GetConfig<LlmSamplingDefaults>(
            ProviderKind.Llm, settings.ActiveProviders.Llm);

        return new PromptContext(
            systemPrompt,
            transcript,
            mode.Temperature ?? samplingDefaults.Temperature,
            samplingDefaults.MaxTokens,
            mode.Name);
    }

    /// <summary>Produces the replacement for one <c>{{Variable}}</c> token.</summary>
    private string ResolveToken(Match match, string transcript, string modeName, AppSettings settings)
        => match.Groups[1].Value.ToLowerInvariant() switch
        {
            "transcript" => transcript,
            "applicationname" => _foregroundAppService.LastCaptured,
            "mode" => modeName,
            "currentdate" => _timeProvider.GetLocalNow().ToString("yyyy-MM-dd"),
            "technicaldictionary" => string.Join(", ", settings.TechnicalDictionary),
            "replacementdictionary" => FormatReplacements(settings.Replacements),
            _ => KeepUnknownToken(match.Value),
        };

    /// <summary>
    /// Renders the replacement dictionary as a comma-separated <c>from → to</c> list for the
    /// <c>{{ReplacementDictionary}}</c> variable, so a prompt can tell the model which
    /// corrections were already applied and must not be reversed. Empty when nothing is configured.
    /// </summary>
    private static string FormatReplacements(IReadOnlyList<ReplacementRule> replacements)
        => string.Join(", ", replacements
            .Where(r => !string.IsNullOrEmpty(r.From))
            .Select(r => $"{r.From} → {r.To}"));

    /// <summary>Logs and preserves a token the resolver does not recognize.</summary>
    private string KeepUnknownToken(string token)
    {
        _logger.LogWarning("Unknown prompt variable {Token} left unresolved", token);
        return token;
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex TokenRegex();
}
