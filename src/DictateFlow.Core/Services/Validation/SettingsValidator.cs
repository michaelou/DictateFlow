using System.Text.Json;
using System.Text.RegularExpressions;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.Prompts;
using DictateFlow.Core.Services.Providers;

namespace DictateFlow.Core.Services.Validation;

/// <summary>
/// Default <see cref="ISettingsValidator"/> implementation. Provider config sections are
/// inspected at the JSON level (Core does not know the provider config types), so the same
/// rules apply to any registered provider: an active provider registered as requiring a
/// connection (see <see cref="ProviderRegistration.RequiresConnection"/>) must carry a
/// non-empty <c>Endpoint</c> (absolute https), <c>ApiKey</c> and <c>DeploymentName</c> —
/// local and mock providers are exempt — numeric fields present on the active section must
/// be in range, and entries of a transcription <c>Language</c> field must look like BCP-47
/// locale tags.
/// </summary>
public sealed class SettingsValidator : ISettingsValidator
{
    /// <summary>Lenient BCP-47 shape: a 2–3 letter language code plus optional subtags (en, en-US, zh-Hans-CN).</summary>
    private static readonly Regex LocaleTagPattern =
        new(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$", RegexOptions.Compiled);

    private readonly ProviderCatalog _catalog;
    private readonly IPromptModeStore _promptModeStore;

    /// <summary>Initializes a new instance of the <see cref="SettingsValidator"/> class.</summary>
    /// <param name="catalog">Enumerates the registered provider names.</param>
    /// <param name="promptModeStore">Supplies the loaded prompt modes for the mode-existence warnings.</param>
    public SettingsValidator(ProviderCatalog catalog, IPromptModeStore promptModeStore)
    {
        _catalog = catalog;
        _promptModeStore = promptModeStore;
    }

    /// <inheritdoc />
    public IReadOnlyList<SettingsValidationError> Validate(AppSettings settings)
    {
        var findings = new List<SettingsValidationError>();

        ValidateRecording(settings.Recording, findings);
        ValidateProvider(ProviderKind.Transcription, "Speech", settings.ActiveProviders.Transcription,
            settings.Providers.Transcription, findings);
        ValidateProvider(ProviderKind.Llm, "LLM", settings.ActiveProviders.Llm,
            settings.Providers.Llm, findings);
        ValidateOutputProvider(settings.ActiveProviders.Output, findings);
        ValidateHistory(settings.History, findings);
        ValidatePricing(settings.Pricing, findings);
        ValidatePromptModes(settings, findings);
        ValidateVoiceCommands(settings.VoiceCommands, findings);
        ValidateCloudRecordings(settings.CloudRecordings, findings);

        return [.. findings.OrderBy(f => f.Severity)];
    }

    private static void ValidateRecording(RecordingSettings recording, List<SettingsValidationError> findings)
    {
        var pushToTalkEmpty = string.IsNullOrWhiteSpace(recording.PushToTalkHotkey);
        var toggleEmpty = string.IsNullOrWhiteSpace(recording.ToggleHotkey);

        if (pushToTalkEmpty && toggleEmpty)
        {
            findings.Add(Error("General", "At least one recording hotkey (push-to-talk or toggle) is required."));
        }

        var pushToTalk = ValidateHotkey("push-to-talk", recording.PushToTalkHotkey, findings);
        var toggle = ValidateHotkey("toggle", recording.ToggleHotkey, findings);

        if (pushToTalk is not null && pushToTalk == toggle)
        {
            findings.Add(Error("General", "The push-to-talk and toggle hotkeys must be different."));
        }

        if (recording.SilenceTimeoutSeconds is < 1 or > 300)
        {
            findings.Add(Error("General", "Silence timeout must be between 1 and 300 seconds."));
        }
    }

    /// <summary>Validates one hotkey; empty is allowed (disabled). Returns the parsed chord, or <see langword="null"/> when empty or invalid.</summary>
    private static HotkeyChord? ValidateHotkey(string label, string? hotkey, List<SettingsValidationError> findings)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return null;
        }

        if (HotkeyParser.TryParse(hotkey, out var chord))
        {
            return chord;
        }

        findings.Add(Error("General", $"'{hotkey}' is not a valid {label} hotkey (expected e.g. Ctrl+Alt+D, RCtrl+RShift or Ctrl+Win)."));
        return null;
    }

    /// <summary>
    /// Validates one transcription/LLM slot: the active name must be registered and, when the
    /// provider registered as requiring a connection, fully configured. Local providers (e.g.
    /// whisper.cpp) and mocks need no endpoint or credentials.
    /// </summary>
    private void ValidateProvider(
        ProviderKind kind,
        string section,
        string activeName,
        Dictionary<string, JsonElement> configSections,
        List<SettingsValidationError> findings)
    {
        if (!TryResolveActiveName(kind, section, activeName, findings, out var resolvedName))
        {
            return;
        }

        var config = FindSection(configSections, resolvedName);

        if (_catalog.RequiresConnection(kind, resolvedName))
        {
            if (config is null)
            {
                findings.Add(Error(section, $"Provider '{resolvedName}' has no configuration section."));
                return;
            }

            ValidateConnectionFields(section, resolvedName, config.Value, findings);
        }

        if (config is not null)
        {
            ValidateNumericRanges(kind, section, config.Value, findings);

            if (kind == ProviderKind.Transcription)
            {
                ValidateLanguages(section, config.Value, findings);
            }
        }
    }

    /// <summary>
    /// Warns about entries of a comma-separated <c>Language</c> field that don't look like
    /// BCP-47 locale tags; empty is valid (auto-detect). A warning rather than an error: the
    /// service, not the app, decides which tags it accepts.
    /// </summary>
    private static void ValidateLanguages(string section, JsonElement config, List<SettingsValidationError> findings)
    {
        var language = GetString(config, "Language");
        if (string.IsNullOrWhiteSpace(language))
        {
            return;
        }

        foreach (var locale in language.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!LocaleTagPattern.IsMatch(locale))
            {
                findings.Add(Warning(section, $"'{locale}' does not look like a BCP-47 locale tag (expected e.g. en-US)."));
            }
        }
    }

    /// <summary>Endpoint/ApiKey/DeploymentName must be present and non-empty; the endpoint must be an absolute https URI.</summary>
    private static void ValidateConnectionFields(
        string section, string providerName, JsonElement config, List<SettingsValidationError> findings)
    {
        var endpoint = GetString(config, "Endpoint");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            findings.Add(Error(section, $"Provider '{providerName}': Endpoint is required."));
        }
        else if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            findings.Add(Error(section, $"Provider '{providerName}': '{endpoint}' is not an absolute https URL."));
        }

        if (string.IsNullOrWhiteSpace(GetString(config, "ApiKey")))
        {
            findings.Add(Error(section, $"Provider '{providerName}': API key is required."));
        }

        if (string.IsNullOrWhiteSpace(GetString(config, "DeploymentName")))
        {
            findings.Add(Error(section, $"Provider '{providerName}': Deployment name is required."));
        }
    }

    /// <summary>Range-checks the numeric fields the active section actually declares.</summary>
    private static void ValidateNumericRanges(
        ProviderKind kind, string section, JsonElement config, List<SettingsValidationError> findings)
    {
        if (TryGetNumber(config, "TimeoutSeconds", out var timeout) && timeout is < 1 or > 600)
        {
            findings.Add(Error(section, "Timeout must be between 1 and 600 seconds."));
        }

        if (kind != ProviderKind.Llm)
        {
            return;
        }

        if (TryGetNumber(config, "Temperature", out var temperature) && temperature is < 0 or > 2)
        {
            findings.Add(Error(section, "Temperature must be between 0 and 2."));
        }

        if (TryGetNumber(config, "MaxTokens", out var maxTokens) && maxTokens is < 1 or > 128000)
        {
            findings.Add(Error(section, "Max tokens must be between 1 and 128000."));
        }
    }

    private void ValidateOutputProvider(string activeName, List<SettingsValidationError> findings)
        => TryResolveActiveName(ProviderKind.Output, "Output", activeName, findings, out _);

    private static void ValidateHistory(HistorySettings history, List<SettingsValidationError> findings)
    {
        if (history.MaxEntries < 1)
        {
            findings.Add(Error("History", "History max entries must be at least 1."));
        }
    }

    private static void ValidatePricing(PricingSettings pricing, List<SettingsValidationError> findings)
    {
        if (pricing.SpeechPerMinute < 0 || pricing.LlmPromptPer1M < 0 || pricing.LlmCompletionPer1M < 0)
        {
            findings.Add(Error("Pricing", "Pricing rates cannot be negative."));
        }
    }

    /// <summary>Mode existence is a warning: modes are user files that may be restored later.</summary>
    private void ValidatePromptModes(AppSettings settings, List<SettingsValidationError> findings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ActivePromptMode)
            && _promptModeStore.GetByName(settings.ActivePromptMode) is null)
        {
            findings.Add(Warning("Prompts", $"Active prompt mode '{settings.ActivePromptMode}' does not exist in the prompts folder."));
        }

        foreach (var rule in settings.ApplicationRules)
        {
            if (!string.IsNullOrWhiteSpace(rule.PromptMode) && _promptModeStore.GetByName(rule.PromptMode) is null)
            {
                findings.Add(Warning("Rules", $"Rule for '{rule.ProcessName}' uses unknown prompt mode '{rule.PromptMode}'."));
            }
        }
    }

    /// <summary>Voice command rules only matter while the feature is enabled; a disabled section stays silent.</summary>
    private static void ValidateVoiceCommands(VoiceCommandSettings voiceCommands, List<SettingsValidationError> findings)
    {
        if (!voiceCommands.Enabled)
        {
            return;
        }

        if (voiceCommands.WakePhraseEnabled && string.IsNullOrWhiteSpace(voiceCommands.WakePhrase))
        {
            findings.Add(Error("Voice Commands", "A wake phrase is required while the wake phrase is enabled."));
        }

        if (voiceCommands.CommandTimeoutSeconds is < 1 or > 600)
        {
            findings.Add(Error("Voice Commands", "Command timeout must be between 1 and 600 seconds."));
        }
    }

    /// <summary>
    /// Cloud recording settings only matter while the feature is enabled; a disabled section
    /// stays silent. When enabled, the connection string and container are required and the
    /// polling interval must be at least a minute. A bad config just means the poller no-ops —
    /// unlike Speech/LLM there is no in-memory fallback, so these are still errors surfaced in
    /// the Settings UI.
    /// </summary>
    private static void ValidateCloudRecordings(CloudRecordingsSettings cloud, List<SettingsValidationError> findings)
    {
        if (!cloud.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(cloud.ConnectionString))
        {
            findings.Add(Error("Cloud Recordings", "An Azure Storage connection string is required while cloud recordings are enabled."));
        }

        if (string.IsNullOrWhiteSpace(cloud.ContainerName))
        {
            findings.Add(Error("Cloud Recordings", "A container name is required while cloud recordings are enabled."));
        }

        if (cloud.PollingIntervalMinutes < 1)
        {
            findings.Add(Error("Cloud Recordings", "The polling interval must be at least 1 minute."));
        }
    }

    /// <summary>
    /// Resolves the configured active name to its registered spelling. An empty name selects
    /// the first registered provider (the built-in default) and is valid; an unknown name is
    /// an error.
    /// </summary>
    private bool TryResolveActiveName(
        ProviderKind kind, string section, string activeName,
        List<SettingsValidationError> findings, out string resolvedName)
    {
        var names = _catalog.GetNames(kind);
        if (string.IsNullOrWhiteSpace(activeName))
        {
            resolvedName = names.FirstOrDefault() ?? "";
            return resolvedName.Length > 0;
        }

        if (_catalog.TryGetRegisteredName(kind, activeName, out resolvedName))
        {
            return true;
        }

        findings.Add(Error(section,
            $"Unknown {kind} provider '{activeName}'. Valid providers: {string.Join(", ", names)}."));
        return false;
    }

    /// <summary>Finds a provider config section by name, case-insensitively.</summary>
    private static JsonElement? FindSection(Dictionary<string, JsonElement> sections, string providerName)
    {
        var key = sections.Keys.FirstOrDefault(
            k => string.Equals(k, providerName, StringComparison.OrdinalIgnoreCase));
        return key is null ? null : sections[key];
    }

    /// <summary>Reads a string property case-insensitively; <see langword="null"/> when absent or not a string.</summary>
    private static string? GetString(JsonElement config, string propertyName)
        => FindProperty(config, propertyName) is { ValueKind: JsonValueKind.String } value
            ? value.GetString()
            : null;

    /// <summary>Reads a numeric property case-insensitively.</summary>
    private static bool TryGetNumber(JsonElement config, string propertyName, out double number)
    {
        if (FindProperty(config, propertyName) is { ValueKind: JsonValueKind.Number } value)
        {
            number = value.GetDouble();
            return true;
        }

        number = 0;
        return false;
    }

    /// <summary>Finds an object property case-insensitively (settings files may be hand-edited).</summary>
    private static JsonElement? FindProperty(JsonElement config, string propertyName)
    {
        if (config.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in config.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static SettingsValidationError Error(string section, string message)
        => new(section, message, SettingsValidationSeverity.Error);

    private static SettingsValidationError Warning(string section, string message)
        => new(section, message, SettingsValidationSeverity.Warning);
}
