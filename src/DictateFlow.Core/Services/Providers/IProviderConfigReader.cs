namespace DictateFlow.Core.Services.Providers;

/// <summary>
/// Gives each provider access to its own configuration subsection under
/// <c>Providers.&lt;Kind&gt;.&lt;Name&gt;</c> in settings, deserialized into the provider's
/// own config type. Reads are tolerant — a missing or unreadable section yields a default
/// instance with a warning, never an exception — and go through the live settings, so edits
/// apply to the next call without a restart.
/// </summary>
public interface IProviderConfigReader
{
    /// <summary>
    /// Deserializes the <c>Providers.&lt;kind&gt;.&lt;providerName&gt;</c> section into
    /// <typeparamref name="T"/>. Missing sections and properties fall back to the defaults
    /// of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The provider's config type.</typeparam>
    /// <param name="kind">The provider slot the section lives under.</param>
    /// <param name="providerName">The provider name (case-insensitive).</param>
    T GetConfig<T>(ProviderKind kind, string providerName) where T : class, new();

    /// <summary>
    /// Writes <paramref name="config"/> back as the
    /// <c>Providers.&lt;kind&gt;.&lt;providerName&gt;</c> section of the in-memory settings
    /// (persisted by the next <see cref="ISettingsService.SaveAsync"/>). Used by the
    /// settings UI.
    /// </summary>
    /// <typeparam name="T">The provider's config type.</typeparam>
    /// <param name="kind">The provider slot the section lives under.</param>
    /// <param name="providerName">The provider name; replaces an existing section case-insensitively.</param>
    /// <param name="config">The config values to store.</param>
    void SetConfig<T>(ProviderKind kind, string providerName, T config) where T : class;
}
