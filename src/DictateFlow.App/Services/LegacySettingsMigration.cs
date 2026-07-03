using System.Text.Json;
using System.Text.Json.Nodes;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Transcription;
using DictateFlow.Providers.AzureFoundry;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.Services;

/// <summary>
/// Migrates a pre-M7 <c>settings.json</c> — the flat <c>Speech</c>/<c>Llm</c> sections plus
/// <c>Output.Provider</c> — to the named-provider schema (<c>ActiveProviders</c> +
/// <c>Providers</c>). The old sections become the <c>AzureFoundry</c> subsections verbatim
/// (they have the same shape), and the old "endpoint empty → mock" behavior is preserved by
/// activating <c>Mock</c> when no endpoint was configured. All other sections are left
/// untouched. Idempotent: a file that already has <c>ActiveProviders</c> is never modified.
/// </summary>
public sealed class LegacySettingsMigration : ISettingsMigration
{
    private readonly ILogger<LegacySettingsMigration> _logger;

    /// <summary>Initializes a new instance of the <see cref="LegacySettingsMigration"/> class.</summary>
    /// <param name="logger">Receives diagnostic output.</param>
    public LegacySettingsMigration(ILogger<LegacySettingsMigration> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool TryMigrate(JsonObject settings)
    {
        if (Find(settings, "ActiveProviders") is not null
            || (Find(settings, "Speech") is null && Find(settings, "Llm") is null))
        {
            return false;
        }

        var speech = Remove(settings, "Speech");
        var llm = Remove(settings, "Llm");
        var outputProvider = Remove(settings.Find("Output") as JsonObject, "Provider");

        settings["ActiveProviders"] = new JsonObject
        {
            ["Transcription"] = HasEndpoint(speech)
                ? AzureFoundryProviders.RegistrationName
                : MockTranscriptionProvider.RegistrationName,
            ["Llm"] = HasEndpoint(llm)
                ? AzureFoundryProviders.RegistrationName
                : MockLLMProvider.RegistrationName,
            ["Output"] = outputProvider?.GetValue<string>() ?? "",
        };

        settings["Providers"] = new JsonObject
        {
            ["Transcription"] = new JsonObject
            {
                [AzureFoundryProviders.RegistrationName] = speech ?? JsonSerializer.SerializeToNode(new AzureFoundryTranscriptionConfig()),
                [MockTranscriptionProvider.RegistrationName] = JsonSerializer.SerializeToNode(new MockTranscriptionConfig()),
            },
            ["Llm"] = new JsonObject
            {
                [AzureFoundryProviders.RegistrationName] = llm ?? JsonSerializer.SerializeToNode(new AzureFoundryLlmConfig()),
                [MockLLMProvider.RegistrationName] = JsonSerializer.SerializeToNode(new MockLlmConfig()),
            },
            ["Output"] = new JsonObject(),
        };

        _logger.LogInformation(
            "Migrated legacy settings: Speech → Providers.Transcription, Llm → Providers.Llm, Output.Provider → ActiveProviders.Output");
        return true;
    }

    /// <summary>Whether the legacy section carries a non-empty <c>Endpoint</c> (real provider configured).</summary>
    private static bool HasEndpoint(JsonNode? legacySection)
        => legacySection is JsonObject section
            && section.Find("Endpoint") is JsonValue value
            && !string.IsNullOrWhiteSpace(value.GetValue<string>());

    /// <summary>Finds a property case-insensitively (hand-edited files may vary in casing).</summary>
    private static JsonNode? Find(JsonObject? obj, string propertyName) => obj?.Find(propertyName);

    /// <summary>Removes a property case-insensitively and returns its (detached) value.</summary>
    private static JsonNode? Remove(JsonObject? obj, string propertyName)
    {
        if (obj is null)
        {
            return null;
        }

        var key = obj.FindKey(propertyName);
        if (key is null)
        {
            return null;
        }

        var node = obj[key];
        obj.Remove(key);
        return node;
    }
}

/// <summary>Case-insensitive property lookups over <see cref="JsonObject"/>.</summary>
internal static class JsonObjectExtensions
{
    /// <summary>Finds the stored key matching <paramref name="propertyName"/> case-insensitively.</summary>
    public static string? FindKey(this JsonObject obj, string propertyName)
        => obj.Select(p => p.Key)
            .FirstOrDefault(k => string.Equals(k, propertyName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Gets the value of the property matching <paramref name="propertyName"/> case-insensitively.</summary>
    public static JsonNode? Find(this JsonObject obj, string propertyName)
    {
        var key = obj.FindKey(propertyName);
        return key is null ? null : obj[key];
    }
}
