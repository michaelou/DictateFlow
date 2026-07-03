using System.Text.Json;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.Prompts;
using DictateFlow.Core.Services.Providers;

namespace DictateFlow.Core.Services.Validation;

/// <summary>
/// Default <see cref="ISettingsValidator"/> implementation. Provider config sections are
/// inspected at the JSON level (Core does not know the provider config types), so the same
/// rules apply to any registered provider: an active non-mock transcription/LLM provider must
/// carry a non-empty <c>Endpoint</c> (absolute https), <c>ApiKey</c> and
/// <c>DeploymentName</c>, and numeric fields present on the active section must be in range.
/// </summary>
public sealed class SettingsValidator : ISettingsValidator
{
    private const string MockProviderName = "Mock";

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

        return [.. findings.OrderBy(f => f.Severity)];
    }

    private static void ValidateRecording(RecordingSettings recording, List<SettingsValidationError> findings)
    {
        if (string.IsNullOrWhiteSpace(recording.Hotkey))
        {
            findings.Add(Error("General", "A hotkey is required."));
        }
        else if (!HotkeyParser.TryParse(recording.Hotkey, out _))
        {
            findings.Add(Error("General", $"'{recording.Hotkey}' is not a valid hotkey (expected e.g. Ctrl+Alt+D)."));
        }

        if (recording.SilenceTimeoutSeconds is < 1 or > 300)
        {
            findings.Add(Error("General", "Silence timeout must be between 1 and 300 seconds."));
        }
    }

    /// <summary>Validates one transcription/LLM slot: the active name must be registered and, unless it is the mock, fully configured.</summary>
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
        var isMock = string.Equals(resolvedName, MockProviderName, StringComparison.OrdinalIgnoreCase);

        if (!isMock)
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
