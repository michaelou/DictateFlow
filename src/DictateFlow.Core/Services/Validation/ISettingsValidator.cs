using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Validation;

/// <summary>
/// Validates an <see cref="AppSettings"/> instance against the registered providers and the
/// loaded prompt modes. Errors mark configurations that must not be saved (and that make the
/// startup configuration unusable); warnings save but are surfaced to the user.
/// </summary>
public interface ISettingsValidator
{
    /// <summary>Validates <paramref name="settings"/> and returns every finding, errors first.</summary>
    /// <param name="settings">The settings instance to validate.</param>
    IReadOnlyList<SettingsValidationError> Validate(AppSettings settings);
}
