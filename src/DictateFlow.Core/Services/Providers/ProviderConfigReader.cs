using System.Text.Json;
using DictateFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Providers;

/// <summary>
/// Default <see cref="IProviderConfigReader"/> implementation over
/// <see cref="AppSettings.Providers"/>. Section keys are matched case-insensitively
/// (settings files may be hand-edited); values are stored as raw JSON so provider projects
/// can define their own config types without Core knowing them.
/// </summary>
public sealed class ProviderConfigReader : IProviderConfigReader
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<ProviderConfigReader> _logger;

    /// <summary>Initializes a new instance of the <see cref="ProviderConfigReader"/> class.</summary>
    /// <param name="settingsService">Supplies the <c>Providers</c> sections, read per call.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public ProviderConfigReader(ISettingsService settingsService, ILogger<ProviderConfigReader> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <inheritdoc />
    public T GetConfig<T>(ProviderKind kind, string providerName) where T : class, new()
    {
        var section = GetSection(kind);
        var key = FindKey(section, providerName);
        if (key is null)
        {
            _logger.LogWarning(
                "No Providers.{Kind}.{ProviderName} section in settings; using defaults", kind, providerName);
            return new T();
        }

        try
        {
            return section[key].Deserialize<T>(SettingsService.SerializerOptions) ?? new T();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex, "Providers.{Kind}.{ProviderName} section could not be read; using defaults", kind, providerName);
            return new T();
        }
    }

    /// <inheritdoc />
    public void SetConfig<T>(ProviderKind kind, string providerName, T config) where T : class
    {
        var section = GetSection(kind);
        var key = FindKey(section, providerName) ?? providerName;
        section[key] = JsonSerializer.SerializeToElement(config, SettingsService.SerializerOptions);
    }

    /// <summary>Picks the per-kind section dictionary from the current settings.</summary>
    private Dictionary<string, JsonElement> GetSection(ProviderKind kind)
    {
        var providers = _settingsService.Current.Providers;
        return kind switch
        {
            ProviderKind.Transcription => providers.Transcription,
            ProviderKind.Llm => providers.Llm,
            ProviderKind.Output => providers.Output,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    /// <summary>
    /// Finds the stored key matching <paramref name="providerName"/> case-insensitively.
    /// Deserialized dictionaries use the default (case-sensitive) comparer, so the lookup
    /// cannot rely on the dictionary itself.
    /// </summary>
    private static string? FindKey(Dictionary<string, JsonElement> section, string providerName)
        => section.Keys.FirstOrDefault(k => string.Equals(k, providerName, StringComparison.OrdinalIgnoreCase));
}
